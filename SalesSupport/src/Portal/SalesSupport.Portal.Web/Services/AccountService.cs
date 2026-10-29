using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>パスワード確認・OTP確認・ログイン完了の結果を区別します。</summary>
public enum LoginOutcome { Succeeded, Rejected, RequiresOtp, Expired, Throttled, DeliveryFailed }

/// <summary>認証結果と、保護された戻り先・入場制限案内の要否です。</summary>
public sealed record LoginResult(LoginOutcome Outcome, bool RequiresPrivateNotice = false, string? ReturnUrl = null);

/// <summary>認証方式に固有の確認を行い、完了後だけ共有セッションを開始します。</summary>
public interface IAccountService
{
    /// <summary>外部LoginIDとパスワードを検証し、必要ならメールOTPへ進みます。</summary>
    Task<LoginResult> SignInAsync(string? loginId, string? password, string? returnUrl = null, CancellationToken ct = default);
    /// <summary>パスワード確認済みの試行でOTPを検証し、一度だけ消費します。</summary>
    Task<LoginResult> VerifyOtpAsync(string? code, bool rememberBrowser, CancellationToken ct = default);
    /// <summary>一時認証に束縛された利用者へ、制限付きでOTPを再送します。</summary>
    Task<LoginResult> ResendOtpAsync(CancellationToken ct = default);
    /// <summary>OTP画面を表示できる一時Cookieがあるかを確認します。</summary>
    Task<bool> HasPendingLoginAsync();
}

/// <summary>Identity APIとユーザー行ロックで認証を直列化し、メール送信を保存処理から分離します。</summary>
public sealed class AccountService(PortalDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    ISystemSettingsReader settings, IApplicationClock clock, CurrentUserAccessor current, IActivityLogger activity,
    PortalLoginCookies cookies, IMailSender mail, IOptions<CommonOptions> common) : IAccountService
{
    /// <summary>ID・パスワードだけでは共有Cookieを発行せず、OTPまたは有効なブラウザー記憶を要求します。</summary>
    public async Task<LoginResult> SignInAsync(string? loginId, string? password, string? returnUrl = null, CancellationToken ct = default)
    {
        await cookies.ClearPendingAsync();
        if (string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256 || string.IsNullOrEmpty(password)) return await RejectAsync(ct);
        var normalized = users.NormalizeName(loginId);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedUserName == normalized).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (userId == Guid.Empty) return await RejectAsync(ct);
        returnUrl = CommonValidation.IsLocalPath(returnUrl) ? returnUrl : null;
        ApplicationUser? user;
        EmailOtpState state;
        string code;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            user = await LoadUserAsync(userId, ct);
            if (!await IsEligibleAsync(user, ct)) return await RejectAsync(ct);
            // パスワードの成功だけでは失敗回数を消しません。OTPを含めた認証完了時にリセットします。
            if (!await users.CheckPasswordAsync(user!, password))
            {
                await RecordFailureAsync(user!, ct);
                await transaction.CommitAsync(ct);
                return await RejectAsync(ct);
            }
            if (await cookies.IsRememberedAsync(user!))
            {
                state = await ReadStateAsync(user!);
                state.Consume();
                await SaveStateAsync(user!, state);
                await RecordSessionAsync(user!);
                await transaction.CommitAsync(ct);
                return await CompleteAsync(user!, returnUrl, ct);
            }
            state = await ReadStateAsync(user!);
            if (!state.CanSend(clock.GetUtcNow())) return new(LoginOutcome.Throttled);
            state.AttemptId = Guid.NewGuid();
            state.PendingUntil = clock.GetUtcNow().AddMinutes(15);
            state.SecurityStamp = user!.SecurityStamp!;
            state.LoginId = user.UserName!;
            state.Email = user.Email!;
            code = await PrepareSendAsync(user, state);
            await transaction.CommitAsync(ct);
        }
        await cookies.SetPendingAsync(userId, state.AttemptId, returnUrl);
        return await DeliverAsync(userId, state, code, ct);
    }

    /// <summary>パスワード確認済み試行の期限・資格情報の世代と、コードの一回消費を同時に検証します。</summary>
    public async Task<LoginResult> VerifyOtpAsync(string? code, bool rememberBrowser, CancellationToken ct = default)
    {
        var pending = await cookies.GetPendingAsync();
        if (pending is null) return new(LoginOutcome.Expired);
        ApplicationUser? user;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            user = await LoadUserAsync(pending.UserId, ct);
            if (!await IsEligibleAsync(user, ct)) return new(LoginOutcome.Expired);
            var state = await ReadStateAsync(user!);
            if (!Matches(state, pending, user!)) return new(LoginOutcome.Expired);
            if (!state.Delivered || state.ExpiresAt <= clock.GetUtcNow()) return new(LoginOutcome.Expired);
            var valid = code is { Length: 6 } && code.All(x => x is >= '0' and <= '9')
                && await users.VerifyUserTokenAsync(user!, TokenOptions.DefaultEmailProvider, state.Purpose, code);
            if (!valid)
            {
                await RecordFailureAsync(user!, ct);
                await transaction.CommitAsync(ct);
                await activity.WriteAsync(new ActivityEvent("LOGIN", "FAILURE", "INVALID_CREDENTIALS"), ct);
                return new(LoginOutcome.Rejected);
            }
            state.Consume();
            await SaveStateAsync(user!, state);
            await RecordSessionAsync(user!);
            await transaction.CommitAsync(ct);
        }
        if (rememberBrowser) await cookies.RememberAsync(user!);
        return await CompleteAsync(user!, pending.ReturnUrl, ct);
    }

    /// <summary>再送前にDBで送信枠を確保し、HTTP要求やプロセス再起動で回数を戻さないようにします。</summary>
    public async Task<LoginResult> ResendOtpAsync(CancellationToken ct = default)
    {
        var pending = await cookies.GetPendingAsync();
        if (pending is null) return new(LoginOutcome.Expired);
        EmailOtpState state;
        string code;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var user = await LoadUserAsync(pending.UserId, ct);
            if (!await IsEligibleAsync(user, ct)) return new(LoginOutcome.Expired);
            state = await ReadStateAsync(user!);
            if (!Matches(state, pending, user!)) return new(LoginOutcome.Expired);
            if (!state.CanSend(clock.GetUtcNow())) return new(LoginOutcome.Throttled);
            code = await PrepareSendAsync(user!, state);
            await transaction.CommitAsync(ct);
        }
        return await DeliverAsync(pending.UserId, state, code, ct);
    }

    /// <summary>画面表示では送信・DB更新をせず、一時Cookieの存在だけを確認します。</summary>
    public async Task<bool> HasPendingLoginAsync() => await cookies.GetPendingAsync() is not null;

    /// <summary>ロック取得後に追跡中の古いユーザー属性を再読込します。</summary>
    private async Task<ApplicationUser?> LoadUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.LockUserAsync(userId, ct);
        if (user is not null) await db.Entry(user).ReloadAsync(ct);
        return user;
    }

    /// <summary>未確認メール・ロック対象外・未定義ロールなどの不完全な連携ユーザーを拒否します。</summary>
    private async Task<bool> IsEligibleAsync(ApplicationUser? user, CancellationToken ct) => user is not null && user.IsActive
        && user.LockoutEnabled && user.EmailConfirmed && CommonValidation.IsEmail(user.Email) && !string.IsNullOrEmpty(user.SecurityStamp)
        && !await users.IsLockedOutAsync(user) && await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == user.RoleCode, ct);

    /// <summary>OTP要求を資格情報の世代と本人のブラウザー試行へ束縛します。</summary>
    private bool Matches(EmailOtpState state, PendingLogin pending, ApplicationUser user) => state.AttemptId == pending.AttemptId
        && state.AttemptId != Guid.Empty && state.PendingUntil > clock.GetUtcNow() && state.SecurityStamp == user.SecurityStamp
        && state.LoginId == user.UserName && state.Email == user.Email;

    /// <summary>Identityトークンのメタデータを取得します。秘密のOTPはDBへ保存しません。</summary>
    private async Task<EmailOtpState> ReadStateAsync(ApplicationUser user)
    {
        // SMTP待機中に別要求が更新したTokenを、同一Contextの追跡キャッシュから読まないようにします。
        foreach (var entry in db.ChangeTracker.Entries<IdentityUserToken<Guid>>().Where(x => x.Entity.UserId == user.Id).ToArray())
            entry.State = EntityState.Detached;
        return EmailOtpState.Read(await users.GetAuthenticationTokenAsync(user, EmailOtpState.LoginProvider, EmailOtpState.TokenName));
    }

    /// <summary>Identity API経由で保存し、更新失敗を許可側へ倒しません。</summary>
    private async Task SaveStateAsync(ApplicationUser user, EmailOtpState state) => Ensure(await users.SetAuthenticationTokenAsync(user,
        EmailOtpState.LoginProvider, EmailOtpState.TokenName, JsonSerializer.Serialize(state)));

    /// <summary>送信の予約・前コードの無効化と標準Providerによる生成を同じ保存単位で行います。</summary>
    private async Task<string> PrepareSendAsync(ApplicationUser user, EmailOtpState state)
    {
        var now = clock.GetUtcNow();
        state.TokenId = Guid.NewGuid();
        state.ExpiresAt = now.AddMinutes(5);
        state.Delivered = false;
        state.SendTimes.Add(now);
        await SaveStateAsync(user, state);
        return await users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, state.Purpose);
    }

    /// <summary>ロックを保持せずSMTPへ一度だけ送り、結果不明も認証可能な送信として扱いません。</summary>
    private async Task<LoginResult> DeliverAsync(Guid userId, EmailOtpState sent, string code, CancellationToken ct)
    {
        var result = await mail.SendAsync(new([sent.Email], [], [], $"{common.Value.SiteName} ログイン確認コード",
            $"ログイン確認コード：{code}\n有効期限は発行から5分です。\n心当たりがない場合は入力せず、社内のシステム担当へお問い合わせください。"), ct);
        if (result.Outcome != DeliveryOutcome.Succeeded) return new(LoginOutcome.DeliveryFailed);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LoadUserAsync(userId, ct);
        if (!await IsEligibleAsync(user, ct)) return new(LoginOutcome.Expired);
        var state = await ReadStateAsync(user!);
        if (state.TokenId != sent.TokenId || !Matches(state, new(userId, sent.AttemptId, null), user!)
            || state.ExpiresAt <= clock.GetUtcNow()) return new(LoginOutcome.Expired);
        state.Delivered = true;
        await SaveStateAsync(user!, state);
        await transaction.CommitAsync(ct);
        return new(LoginOutcome.RequiresOtp);
    }

    /// <summary>パスワードとOTPの失敗をアカウント単位で累積し、5回で30分ロックします。</summary>
    private async Task RecordFailureAsync(ApplicationUser user, CancellationToken ct)
    {
        Ensure(await users.AccessFailedAsync(user));
        if (await users.IsLockedOutAsync(user)) await activity.WriteAsync(new ActivityEvent("ACCOUNT_LOCK", "SUCCESS"), ct);
    }

    /// <summary>認証完了時にだけ失敗回数と最終利用日時を確定します。</summary>
    private async Task RecordSessionAsync(ApplicationUser user)
    {
        Ensure(await users.ResetAccessFailedCountAsync(user));
        user.LastAccessAt = clock.GetUtcNow().UtcDateTime;
        Ensure(await users.UpdateAsync(user));
    }

    /// <summary>認証方式に固有の確認が完了した後、共通の共有セッションを開始します。</summary>
    private async Task<LoginResult> CompleteAsync(ApplicationUser user, string? returnUrl, CancellationToken ct)
    {
        await cookies.ClearPendingAsync();
        current.SetVerified(new CurrentUser(user.Id, user.DisplayName, user.RoleCode));
        await signIn.SignInAsync(user, SharedCookieContract.CreateSignInProperties(clock.GetUtcNow()));
        await activity.WriteAsync(new ActivityEvent("LOGIN", "SUCCESS"), ct);
        var publication = await settings.GetPublicationStatusAsync(ct);
        return new(LoginOutcome.Succeeded, publication == "PRIVATE" && user.RoleCode != "ADMIN", returnUrl);
    }

    /// <summary>ログインIDや失敗理由を画面へ返さず、結果だけを記録します。</summary>
    private async Task<LoginResult> RejectAsync(CancellationToken ct)
    {
        await activity.WriteAsync(new ActivityEvent("LOGIN", "FAILURE", "INVALID_CREDENTIALS"), ct);
        return new(LoginOutcome.Rejected);
    }

    /// <summary>Identity保存の競合・検証失敗では認証を完了させません。</summary>
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new DbUpdateConcurrencyException("認証状態を更新できませんでした。");
    }
}
