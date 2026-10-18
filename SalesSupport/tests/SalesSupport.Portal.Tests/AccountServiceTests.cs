using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>実際のIdentity・Cookieと使い捨てDBで、OTP認証の状態遷移を確認します。</summary>
public sealed class AccountServiceTests
{
    /// <summary>パスワード照合後もOTP完了まで共有Cookieと最終利用日時を作りません。</summary>
    [Fact]
    public async Task PasswordRequiresOtpAndCodeIsConsumedOnlyOnce()
    {
        await using var test = await Harness.CreateAsync();
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.LoginAsync()).Outcome);
        Assert.False(test.Jar.ContainsKey(SharedCookieContract.CookieName));
        Assert.Null(await test.QueryAsync(db => db.Users.Select(x => x.LastAccessAt).SingleAsync()));
        var pendingCookie = test.Jar[".SalesSupport.OtpPending"];
        var result = await test.VerifyAsync(true);
        Assert.Equal(LoginOutcome.Succeeded, result.Outcome);
        Assert.Equal("/sample/input?item=1", result.ReturnUrl);
        Assert.True(test.Jar.ContainsKey(SharedCookieContract.CookieName));
        Assert.True(test.Jar.ContainsKey(".SalesSupport.OtpRemember"));
        test.Jar[".SalesSupport.OtpPending"] = pendingCookie;
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
    }

    /// <summary>メール形式をLoginIDとして代用せず、資格情報不正でも送信しません。</summary>
    [Theory]
    [InlineData("staff@example.invalid", "ValidPassword!123")]
    [InlineData("staff001", "wrong")]
    public async Task InvalidCredentialsNeverSend(string loginId, string password)
    {
        await using var test = await Harness.CreateAsync();
        var result = await test.CallAsync(service => service.SignInAsync(loginId, password));
        Assert.Equal(LoginOutcome.Rejected, result.Outcome);
        Assert.Empty(test.Mail.Messages);
    }

    /// <summary>同じブラウザーではログアウト後もOTPを省略し、固定期限30日で再び要求します。</summary>
    [Fact]
    public async Task RememberBrowserSurvivesLogoutButDoesNotSlide()
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        await test.VerifyAsync(true);
        var remembered = test.Jar[".SalesSupport.OtpRemember"];
        test.Jar.Remove(SharedCookieContract.CookieName);
        test.Time.Advance(TimeSpan.FromDays(29));
        Assert.Equal(LoginOutcome.Succeeded, (await test.LoginAsync()).Outcome);
        Assert.Single(test.Mail.Messages);
        Assert.Equal(remembered, test.Jar[".SalesSupport.OtpRemember"]);
        test.Time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.LoginAsync()).Outcome);
        Assert.Equal(2, test.Mail.Messages.Count);
    }

    /// <summary>記憶を選択しなかった場合と別ブラウザーではOTPを要求します。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoRememberOrNewBrowserRequiresOtp(bool newBrowser)
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        await test.VerifyAsync(newBrowser);
        if (newBrowser) test.Jar.Clear();
        test.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.LoginAsync()).Outcome);
    }

    /// <summary>管理者にも同じOTPと任意の30日記憶を適用します。</summary>
    [Fact]
    public async Task AdministratorUsesSameOtpPolicy()
    {
        await using var test = await Harness.CreateAsync();
        await test.ChangeUserAsync(user => user.RoleCode = "ADMIN");
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.LoginAsync()).Outcome);
        Assert.Equal(LoginOutcome.Succeeded, (await test.VerifyAsync(true)).Outcome);
        Assert.Equal(LoginOutcome.Succeeded, (await test.LoginAsync()).Outcome);
    }

    /// <summary>記憶済みCookieも資格情報・有効状態の変更で使用できなくなります。</summary>
    [Theory]
    [InlineData("stamp", LoginOutcome.RequiresOtp)]
    [InlineData("email", LoginOutcome.RequiresOtp)]
    [InlineData("login", LoginOutcome.RequiresOtp)]
    [InlineData("inactive", LoginOutcome.Rejected)]
    [InlineData("unconfirmed", LoginOutcome.Rejected)]
    [InlineData("role", LoginOutcome.Rejected)]
    public async Task ChangedUserInvalidatesTrust(string change, LoginOutcome outcome)
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        await test.VerifyAsync(true);
        await test.ChangeUserAsync(user =>
        {
            if (change == "stamp") user.SecurityStamp = Guid.NewGuid().ToString();
            if (change == "email") user.Email = "new@example.invalid";
            if (change == "login") user.UserName = "new001";
            if (change == "inactive") user.IsActive = false;
            if (change == "unconfirmed") user.EmailConfirmed = false;
            if (change == "role") user.RoleCode = "UNKNOWN";
        });
        test.Time.Advance(TimeSpan.FromSeconds(60));
        var result = await test.CallAsync(service => service.SignInAsync(change == "login" ? "new001" : "staff001", Harness.Password));
        Assert.Equal(outcome, result.Outcome);
    }

    /// <summary>コードは5分ちょうどで無効、一時認証は15分で無効になります。</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    public async Task ExpiryNeverCompletesLogin(int minutes)
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        test.Time.Advance(TimeSpan.FromMinutes(minutes));
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
        Assert.False(test.Jar.ContainsKey(SharedCookieContract.CookieName));
    }

    /// <summary>同じ一時Cookieでも送信前と再送後の古いコードは使用できません。</summary>
    [Fact]
    public async Task ResendRotatesTokenAndEnforcesAccountLimit()
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        var oldCode = test.Mail.Code;
        Assert.Equal(LoginOutcome.Throttled, (await test.CallAsync(service => service.ResendOtpAsync())).Outcome);
        test.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.CallAsync(service => service.ResendOtpAsync())).Outcome);
        Assert.Equal(LoginOutcome.Rejected, (await test.CallAsync(service => service.VerifyOtpAsync(oldCode, false))).Outcome);
        for (var i = 0; i < 3; i++)
        {
            test.Time.Advance(TimeSpan.FromSeconds(60));
            Assert.Equal(LoginOutcome.RequiresOtp, (await test.CallAsync(service => service.ResendOtpAsync())).Outcome);
        }
        test.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(LoginOutcome.Throttled, (await test.CallAsync(service => service.ResendOtpAsync())).Outcome);
        Assert.Equal(LoginOutcome.Throttled, (await test.LoginAsync()).Outcome);
        Assert.Equal(5, test.Mail.Messages.Count);
        test.Time.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.LoginAsync()).Outcome);
    }

    /// <summary>不正な形式も失敗に数え、パスワードの成功や再送でOTP失敗を消しません。</summary>
    [Fact]
    public async Task FiveFailuresLockAccountAcrossPasswordRestarts()
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        for (var i = 0; i < 4; i++) Assert.Equal(LoginOutcome.Rejected,
            (await test.CallAsync(service => service.VerifyOtpAsync("bad", false))).Outcome);
        test.Time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(LoginOutcome.RequiresOtp, (await test.LoginAsync()).Outcome);
        Assert.Equal(LoginOutcome.Rejected, (await test.CallAsync(service => service.VerifyOtpAsync("bad", false))).Outcome);
        Assert.Equal(LoginOutcome.Rejected, (await test.LoginAsync()).Outcome);
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
        Assert.Equal(2, test.Mail.Messages.Count);
    }

    /// <summary>SMTP受付失敗・結果不明のコードを入力しても認証を完了しません。</summary>
    [Theory]
    [InlineData(DeliveryOutcome.Failed)]
    [InlineData(DeliveryOutcome.Unknown)]
    public async Task MailFailureCannotBeUsed(DeliveryOutcome outcome)
    {
        await using var test = await Harness.CreateAsync();
        test.Mail.Outcome = outcome;
        Assert.Equal(LoginOutcome.DeliveryFailed, (await test.LoginAsync()).Outcome);
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(true)).Outcome);
        Assert.False(test.Jar.ContainsKey(".SalesSupport.OtpRemember"));
    }

    /// <summary>本人情報がOTP待ちの間に変わった場合は正しいコードでも拒否します。</summary>
    [Fact]
    public async Task PendingLoginIsBoundToCredentialGeneration()
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        await test.ChangeUserAsync(user => user.SecurityStamp = Guid.NewGuid().ToString());
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
    }

    /// <summary>一時Cookieなしでは検証・再送を実行できません。</summary>
    [Fact]
    public async Task MissingPendingCookieRejectsVerifyAndResend()
    {
        await using var test = await Harness.CreateAsync();
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
        Assert.Equal(LoginOutcome.Expired, (await test.CallAsync(service => service.ResendOtpAsync())).Outcome);
        Assert.Empty(test.Mail.Messages);
    }

    /// <summary>送信待機中に別要求が状態を変更した場合、追跡キャッシュで新しい要求を上書きしません。</summary>
    [Fact]
    public async Task DeliveryDoesNotOverwriteStateChangedDuringSmtp()
    {
        await using var test = await Harness.CreateAsync();
        test.Mail.BeforeResult = async () =>
        {
            await using var scope = test.Provider.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Users.SingleAsync();
            var state = EmailOtpState.Read(await users.GetAuthenticationTokenAsync(user, EmailOtpState.LoginProvider, EmailOtpState.TokenName));
            state.Consume();
            Assert.True((await users.SetAuthenticationTokenAsync(user, EmailOtpState.LoginProvider, EmailOtpState.TokenName, JsonSerializer.Serialize(state))).Succeeded);
        };
        Assert.Equal(LoginOutcome.Expired, (await test.LoginAsync()).Outcome);
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
    }

    /// <summary>別のログイン試行を開始した後、以前のブラウザー試行からOTPを使用できません。</summary>
    [Fact]
    public async Task LatestLoginAttemptInvalidatesPreviousPendingCookie()
    {
        await using var test = await Harness.CreateAsync();
        await test.LoginAsync();
        var oldPending = test.Jar[".SalesSupport.OtpPending"];
        test.Time.Advance(TimeSpan.FromSeconds(60));
        await test.LoginAsync();
        test.Jar[".SalesSupport.OtpPending"] = oldPending;
        Assert.Equal(LoginOutcome.Expired, (await test.VerifyAsync(false)).Outcome);
    }

    /// <summary>DB・鍵・メールを外部へ出さず、要求ごとにDIスコープとCookieを往復させます。</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public const string Password = "ValidPassword!123";
        public ServiceProvider Provider { get; private set; } = null!;
        public Dictionary<string, string> Jar { get; } = [];
        public FakeTime Time { get; } = new();
        public CapturedMail Mail { get; } = new();

        /// <summary>標準TokenProviderと実Cookie処理を使い、SQL行ロックだけをテスト環境で置換します。</summary>
        public static async Task<Harness> CreateAsync()
        {
            var test = new Harness();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAuthentication(SharedCookieContract.Scheme).AddCookie(SharedCookieContract.Scheme,
                options => options.Cookie.Name = SharedCookieContract.CookieName);
            PortalLoginCookies.Register(services);
            var database = Guid.NewGuid().ToString();
            services.AddDbContext<PortalDbContext>(options => options.UseInMemoryDatabase(database)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).AddInterceptors(InMemoryLockQuery.Instance));
            services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
            }).AddEntityFrameworkStores<PortalDbContext>().AddSignInManager()
                .AddTokenProvider<EmailTokenProvider<ApplicationUser>>(TokenOptions.DefaultEmailProvider);
            services.AddSingleton<IApplicationClock>(new ApplicationClock(test.Time));
            services.AddSingleton<ISystemSettingsReader, PublicSettings>();
            services.AddSingleton<IActivityLogger, SilentActivity>();
            services.AddSingleton<IMailSender>(test.Mail);
            services.AddScoped<CurrentUserAccessor>();
            services.AddScoped<PortalLoginCookies>();
            services.AddScoped<IAccountService, AccountService>();
            test.Provider = services.BuildServiceProvider();
            await using var scope = test.Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Roles.AddRange(new Role { RoleCode = "A", RoleName = "ロールA" }, new Role { RoleCode = "ADMIN", RoleName = "管理者" });
            await db.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "staff001", Email = "staff@example.invalid",
                EmailConfirmed = true, DisplayName = "試験利用者", RoleCode = "A", IsActive = true, LockoutEnabled = true }, Password)).Succeeded);
            return test;
        }

        /// <summary>既知の試験IDでログインを開始します。</summary>
        public Task<LoginResult> LoginAsync() => CallAsync(service => service.SignInAsync("staff001", Password, "/sample/input?item=1"));
        /// <summary>メモリー内で捕捉したメールのコードだけを入力します。</summary>
        public Task<LoginResult> VerifyAsync(bool remember) => CallAsync(service => service.VerifyOtpAsync(Mail.Code, remember));

        /// <summary>一要求のCookie発行を次の要求へ渡します。</summary>
        public async Task<LoginResult> CallAsync(Func<IAccountService, Task<LoginResult>> action)
        {
            await using var scope = Provider.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("portal.test");
            context.Request.Headers.Cookie = string.Join("; ", Jar.Select(x => $"{x.Key}={x.Value}"));
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
            var result = await action(scope.ServiceProvider.GetRequiredService<IAccountService>());
            foreach (var cookie in SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.Select(value => value!).ToArray()))
            {
                if (cookie.Expires < DateTimeOffset.UtcNow) Jar.Remove(cookie.Name.ToString());
                else Jar[cookie.Name.ToString()] = cookie.Value.ToString();
            }
            return result;
        }

        /// <summary>スコープを分けて保存済みデータだけを読みます。</summary>
        public async Task<T> QueryAsync<T>(Func<PortalDbContext, Task<T>> query)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<PortalDbContext>());
        }

        /// <summary>Identity API経由で外部連携によるユーザー変更を再現します。</summary>
        public async Task ChangeUserAsync(Action<ApplicationUser> change)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var user = await db.Users.SingleAsync();
            change(user);
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().UpdateAsync(user)).Succeeded);
        }

        /// <summary>メモリー内の試験リソースだけを破棄します。</summary>
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>本番コードのSQLヒントだけを除去し、フィルターはInMemoryで評価する試験専用の変換です。</summary>
    private sealed class InMemoryLockQuery : ExpressionVisitor, IQueryExpressionInterceptor
    {
        public static readonly InMemoryLockQuery Instance = new();
        /// <summary>SQLロックを実行せず、認証の業務フローだけを試験します。</summary>
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) => Visit(queryExpression);
        /// <summary>FromSqlのクエリルートを通常のEntityクエリへ置換します。</summary>
        protected override Expression VisitExtension(Expression node) => node is EntityQueryRootExpression root && root.GetType().Name == "FromSqlQueryRootExpression"
            ? new EntityQueryRootExpression(root.EntityType) : base.VisitExtension(node);
    }

    /// <summary>OTPの業務期限だけを進め、Identityの暗号処理は標準のまま使用します。</summary>
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        /// <summary>試験中の業務UTC時刻を返します。</summary>
        public override DateTimeOffset GetUtcNow() => now;
        /// <summary>実際に待機せず期限を進めます。</summary>
        public void Advance(TimeSpan span) => now += span;
    }

    /// <summary>OTPを外部送信せずメモリー内で捕捉します。</summary>
    private sealed class CapturedMail : IMailSender
    {
        public List<MailRequest> Messages { get; } = [];
        public DeliveryOutcome Outcome { get; set; } = DeliveryOutcome.Succeeded;
        public Func<Task>? BeforeResult { get; set; }
        public string Code => Messages.Count == 0 ? "000000" : Regex.Match(Messages[^1].Body, "[0-9]{6}").Value;
        /// <summary>指定されたSMTP結果だけを返します。</summary>
        public async Task<MailSendResult> SendAsync(MailRequest request, CancellationToken ct = default)
        {
            Messages.Add(request);
            if (BeforeResult is not null) await BeforeResult();
            return new MailSendResult(Outcome);
        }
    }

    /// <summary>認証後のサイト公開状態を試験用の公開状態へ固定します。</summary>
    private sealed class PublicSettings : ISystemSettingsReader
    {
        /// <summary>公開サイトとしてログインを完了します。</summary>
        public Task<string> GetPublicationStatusAsync(CancellationToken ct = default) => Task.FromResult("PUBLIC");
        /// <summary>この試験では非公開案内を取得しません。</summary>
        public Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>認証に業務日付を使用しません。</summary>
        public Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default) => throw new InvalidOperationException();
    }

    /// <summary>操作結果を外部へ保存しない試験用ログです。</summary>
    private sealed class SilentActivity : IActivityLogger
    {
        /// <summary>コードやメール本文をログへ渡しません。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }
}
