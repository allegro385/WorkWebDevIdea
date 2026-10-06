using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>メールと分離したログインIDでIdentity標準のハッシュ検証へ進むことを確認します。</summary>
public sealed class AccountServiceTests
{
    /// <summary>未確認メールでも外部ログインIDで資格情報を照合し、メール入力では照合しません。</summary>
    [Fact]
    public async Task CredentialsAreLookedUpByLoginIdAndEmailConfirmationIsNotRequired()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication(SharedCookieContract.Scheme).AddCookie(SharedCookieContract.Scheme);
        services.AddDbContext<PortalDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedEmail = false)
            .AddEntityFrameworkStores<PortalDbContext>().AddSignInManager<InspectSignIn>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = (InspectSignIn)scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        db.Roles.Add(new Role { RoleCode = "A", RoleName = "ロールA" });
        await db.SaveChangesAsync();
        var user = new ApplicationUser { UserName = "staff001", Email = "staff@example.invalid", DisplayName = "試験利用者",
            RoleCode = "A", IsActive = true, EmailConfirmed = false, LockoutEnabled = true };
        Assert.True((await users.CreateAsync(user, "ValidPassword!123")).Succeeded);
        var service = new AccountService(db, users, signIn, new RejectSettings(), new ApplicationClock(TimeProvider.System),
            new CurrentUserAccessor(), new SilentActivity());
        await service.SignInAsync("staff001", "ValidPassword!123");
        Assert.Equal(user.Id, signIn.CheckedUserId);
        Assert.True(signIn.PasswordVerified);
        signIn.CheckedUserId = null;
        await service.SignInAsync("staff@example.invalid", "ValidPassword!123");
        Assert.Null(signIn.CheckedUserId);
    }

    /// <summary>Identity標準の資格情報確認を行い、Cookie発行・SQL行ロック処理の前で終了します。</summary>
    private sealed class InspectSignIn(UserManager<ApplicationUser> users, IHttpContextAccessor http,
        IUserClaimsPrincipalFactory<ApplicationUser> claims, IOptions<IdentityOptions> options,
        ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<ApplicationUser> confirmation)
        : SignInManager<ApplicationUser>(users, http, claims, options, logger, schemes, confirmation)
    {
        public Guid? CheckedUserId { get; set; }
        public bool PasswordVerified { get; private set; }

        /// <summary>実際のIdentity照合結果を保持し、SQL Serverなしのテストで共有Cookieを発行しません。</summary>
        public override async Task<SignInResult> CheckPasswordSignInAsync(ApplicationUser user, string password, bool lockoutOnFailure)
        {
            CheckedUserId = user.Id;
            PasswordVerified = (await base.CheckPasswordSignInAsync(user, password, lockoutOnFailure)).Succeeded;
            return SignInResult.Failed;
        }
    }

    /// <summary>Cookieを発行しない試験でサイト設定へ進むことを検出します。</summary>
    private sealed class RejectSettings : ISystemSettingsReader
    {
        /// <summary>認証未完了のためサイト公開状態の取得を拒否します。</summary>
        public Task<string> GetPublicationStatusAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>認証未完了のため案内文の取得を拒否します。</summary>
        public Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>資格情報照合に業務日付を使用しません。</summary>
        public Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default) => throw new InvalidOperationException();
    }

    /// <summary>テストで秘密を含まない操作結果を外部へ保存しません。</summary>
    private sealed class SilentActivity : IActivityLogger
    {
        /// <summary>業務結果を変更せずログ保存だけを省略します。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }
}
