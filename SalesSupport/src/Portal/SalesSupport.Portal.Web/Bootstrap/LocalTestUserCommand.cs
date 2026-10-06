using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>対話コンソールまたはTSVの標準入力から受け取る、保存前の試験ユーザー情報です。</summary>
public sealed record LocalTestUserInput(string LoginId, string Email, string DisplayName, string Password, string RoleCode);

/// <summary>ローカル試験用ユーザーの作成結果です。</summary>
public enum LocalTestUserOutcome { Created, Rejected, Failed }

/// <summary>Identityを経由して試験用ユーザーと利用者設定を保存する境界です。</summary>
public interface ILocalTestUserProvisioner
{
    /// <summary>一人分の試験用ユーザーと利用者設定を同じトランザクションで確定します。</summary>
    Task<LocalTestUserOutcome> CreateAsync(LocalTestUserInput input, CancellationToken ct = default);
}

/// <summary>メールを使わず、開発用の試験ユーザーを作成します。</summary>
public sealed class LocalTestUserProvisioner(PortalDbContext db, UserManager<ApplicationUser> users) : ILocalTestUserProvisioner
{
    /// <summary>Identityの検証とDB制約を適用し、一人分だけ作成します。</summary>
    public async Task<LocalTestUserOutcome> CreateAsync(LocalTestUserInput input, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = input.LoginId,
                Email = input.Email,
                EmailConfirmed = true,
                DisplayName = input.DisplayName,
                RoleCode = input.RoleCode,
                IsActive = true,
                LockoutEnabled = true
            };
            if (!(await users.CreateAsync(user, input.Password)).Succeeded) return LocalTestUserOutcome.Rejected;

            db.UserPreferences.Add(new UserPreference
            {
                UserId = user.Id,
                SystemNoticeMailEnabled = false,
                FavoriteToolNoticeMailEnabled = false
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return LocalTestUserOutcome.Created;
        }
        catch (DbUpdateException)
        {
            return LocalTestUserOutcome.Failed;
        }
    }
}

/// <summary>開発環境でだけ試験用ユーザーを対話入力で追加する導入補助コマンドです。</summary>
public sealed class LocalTestUserCommand(IHostEnvironment environment, IOptions<CommonOptions> commonOptions,
    ILocalTestUserProvisioner provisioner, ILocalTestUserConsole console)
{
    /// <summary>Web起動や初期管理者作成と区別するコマンド名です。</summary>
    public const string Name = "add-test-user";

    /// <summary>追加コマンドだけを指定した場合に実行対象とします。</summary>
    public static bool IsRequested(string[] args) => args.Length == 1 && args[0] == Name;

    /// <summary>二つの開発環境設定を確認し、秘密入力を表示せず一人分を作成します。</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!environment.IsDevelopment() || commonOptions.Value.EnvironmentCode != "DEVELOPMENT")
        {
            console.WriteLine("試験用ユーザーの追加は開発環境でだけ実行できます。");
            return 2;
        }

        console.WriteLine("試験用ユーザーのログインIDを入力してください。");
        var loginId = console.ReadLine();
        console.WriteLine("試験用ユーザーのメールアドレスを入力してください。");
        var email = console.ReadLine()?.Trim();
        console.WriteLine("試験用ユーザーの表示名を入力してください。");
        var displayName = console.ReadLine()?.Trim();
        console.WriteLine("試験用ユーザーのロール（A、B、C、D、ADMIN）を入力してください。");
        var roleCode = console.ReadLine()?.Trim();
        string? password = null;
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                console.WriteLine("パスワードを入力してください（入力内容は表示されません）。");
                var candidate = console.ReadSecret();
                console.WriteLine("確認のため、同じパスワードをもう一度入力してください。");
                var confirmation = console.ReadSecret();
                if (candidate == confirmation)
                {
                    password = candidate;
                    break;
                }
                console.WriteLine(attempt < 3
                    ? "パスワードと確認入力が一致しません。もう一度入力してください。"
                    : "パスワードと確認入力が一致しません。");
            }
        }
        catch (InvalidOperationException)
        {
            console.WriteLine("パスワードは対話コンソールから入力してください。");
            return 3;
        }

        if (string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256 || roleCode is not ("A" or "B" or "C" or "D" or "ADMIN")
            || !CommonValidation.IsEmail(email) || email!.Length > 256 || string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > 100 || string.IsNullOrEmpty(password))
        {
            console.WriteLine("入力条件を満たしていません。ログインID、メールアドレス、表示名、ロール、パスワードを確認してください。");
            return 3;
        }

        LocalTestUserOutcome outcome;
        try { outcome = await provisioner.CreateAsync(new LocalTestUserInput(loginId, email, displayName, password, roleCode), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { outcome = LocalTestUserOutcome.Failed; }
        console.WriteLine(outcome switch
        {
            LocalTestUserOutcome.Created => "試験用ユーザーを作成しました。",
            LocalTestUserOutcome.Rejected => "ユーザーを登録できませんでした。ログインID・メールアドレスの重複や入力条件を確認してください。",
            _ => "ユーザーを作成できませんでした。開発用DBの接続とテーブル状態を確認してください。"
        });
        return outcome == LocalTestUserOutcome.Created ? 0 : 1;
    }
}
