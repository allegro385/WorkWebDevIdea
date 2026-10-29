using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.DataExport;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.HttpClients;
using SalesSupport.Common.Logging;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Common.Mail;
using SalesSupport.Common.UI;
using SalesSupport.Common.Validation;

namespace SalesSupport.Common.DependencyInjection;

/// <summary>共通基盤をホストへ登録します。</summary>
public static class CommonServiceExtensions
{
    /// <summary>共通データ取得・入力出力・MVC・CSRFフィルターを登録します。起動モードを確定してから共通設定ファイルを読み、Identity StoreはPortalが登録します。</summary>
    /// <param name="services">共通基盤を登録するサービスです。</param>
    /// <param name="kind">PortalまたはToolを指定します。</param>
    /// <param name="environment">共通設定ファイルの基準になるコンテンツルートと、単独開発を検証する実行環境です。</param>
    /// <param name="hostConfiguration">起動モードと共通設定ファイルの場所を読むホスト設定です。</param>
    public static IServiceCollection AddSalesSupportCommon(this IServiceCollection services, ApplicationKind kind, IHostEnvironment environment, IConfiguration hostConfiguration)
    {
        var mode = ToolExecutionModeSettings.Read(hostConfiguration, kind, environment);
        var standalone = mode == ToolExecutionMode.Standalone;
        // 単独開発は共通設定ファイルを読まず、ToolId・アプリ名はホスト設定（起動プロファイルの環境変数）から取得します。
        var common = standalone ? null : CommonConfiguration.Load(environment.ContentRootPath, hostConfiguration[CommonConfiguration.PathKey]);
        var configuration = common?.Values ?? hostConfiguration;
        // 認証モードは共通設定ファイルの値で決めます。単独開発はこのファイルを読まないため常にメールOTPのままです。
        var authenticationMode = standalone ? SiteAuthenticationMode.EmailOtp : SiteAuthenticationModeSettings.Read(configuration);
        var anonymous = !standalone && authenticationMode == SiteAuthenticationMode.Anonymous;
        // 通信方式も共通設定ファイルの値で決めます。単独開発はこのファイルを読まないため常にHTTPSの扱いです。
        var transportMode = standalone ? SiteTransportMode.Https : SiteTransportModeSettings.Read(configuration);
        var cookieSecurePolicy = transportMode == SiteTransportMode.Http ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        // 接続文字列はCommonが保持し、Portalと各ツールへはIConnectionStringProviderで渡します。単独開発は接続文字列を持ちません。
        var connections = new ConnectionStringProvider(standalone ? new ConfigurationBuilder().Build() : configuration, requireConnectionString: !standalone);
        services.AddSingleton<IConnectionStringProvider>(connections);
        services.AddOptions<CommonOptions>().Configure(options =>
        {
            options.Kind = kind;
            options.EnvironmentCode = standalone ? "DEVELOPMENT" : configuration["SalesSupport:EnvironmentCode"] ?? "";
            options.SiteName = configuration["SalesSupport:Site:Name"] ?? "";
            options.ApplicationName = configuration["SalesSupport:Application:Name"] ?? "";
            options.ToolId = configuration["SalesSupport:Application:ToolId"];
            options.PortalBaseUrl = standalone ? "" : configuration["SalesSupport:Portal:BaseUrl"] ?? "";
            options.ExecutionMode = mode;
            options.AuthenticationMode = authenticationMode;
            options.TransportMode = transportMode;
        }).Validate(x => x.EnvironmentCode is "DEVELOPMENT" or "PRODUCTION", "SalesSupport:EnvironmentCodeが不正です。")
          .Validate(x => !string.IsNullOrWhiteSpace(x.SiteName) && x.SiteName.Length <= 100, "Site:Nameが不正です。")
          .Validate(x => !string.IsNullOrWhiteSpace(x.ApplicationName) && x.ApplicationName.Length <= 100, "Application:Nameが不正です。")
          .Validate(x => kind == ApplicationKind.Portal || !string.IsNullOrWhiteSpace(x.ToolId) && x.ToolId.Length <= 20, "Application:ToolIdが必要です。")
          // 単独開発はPortalへのリンクを作らないため、Portal URLを要求しません。
          // スキームはサイトの通信方式に一致させ、HTTP運用の期間だけhttpのURLを受け付けます。
          .Validate(x => x.IsStandalone || Uri.TryCreate(x.PortalBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == x.UrlScheme && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && x.PortalBaseUrl.EndsWith('/'), "Portal:BaseUrlが不正です。")
          .ValidateOnStart();
        services.AddDbContextFactory<CommonDbContext>(options =>
        {
            if (standalone) options.UseSqlServer();
            else options.UseSqlServer(connections.SalesSupportDatabase);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
        services.AddDbContextFactory<LogDbContext>(options =>
        {
            if (standalone) options.UseSqlServer();
            else options.UseSqlServer(connections.SalesSupportDatabase);
        });
        services.AddOptions<LoggingOptions>().Bind(configuration.GetSection("SalesSupport:Logging"))
            .Validate(x => x.TimeoutSeconds > 0, "Logging:TimeoutSecondsが不正です。").ValidateOnStart();
        AddStorage(services, common);
        AddMail(services, configuration, kind, anonymous);
        AddHttp(services, configuration);
        services.AddOptions<ProxyOptions>().Bind(configuration.GetSection("SalesSupport:Proxy"))
            .Validate(x => x.KnownProxies.All(address => System.Net.IPAddress.TryParse(address, out _)), "Proxy:KnownProxiesが不正です。").ValidateOnStart();
        services.AddScoped<RequestCorrelation>();
        services.AddScoped<CommonLogger>();
        services.AddScoped<IUsageLogger>(provider => provider.GetRequiredService<CommonLogger>());
        services.AddScoped<IActivityLogger>(provider => provider.GetRequiredService<CommonLogger>());
        services.AddScoped<ISystemErrorLogger>(provider => provider.GetRequiredService<CommonLogger>());
        services.AddScoped<IErrorHandler, CommonErrorHandler>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IApplicationClock, ApplicationClock>();
        services.AddScoped<ISystemSettingsReader, DatabaseSettingsReader>();
        services.AddScoped<IBusinessDateProvider, BusinessDateProvider>();
        services.AddScoped<ICodeMasterReader, CodeMasterReader>();
        services.AddScoped<IUploadPolicyProvider, UploadPolicyProvider>();
        services.AddSingleton<IUploadValidator, UploadValidator>();
        services.AddScoped<IFileStorage, FileStorage.FileStorage>();
        services.AddScoped<IMailSender, MailSender>();
        services.AddSingleton<IMailTemplateRenderer, MailTemplateRenderer>();
        services.AddScoped<IJsonHttpClient, JsonHttpClient>();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUserAccessor>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<IAccessEvaluator, AccessEvaluator>();
        services.AddSingleton<IDelimitedTextWriter, DelimitedTextWriter>();
        services.AddScoped<ISalesSupportLinks, SalesSupportLinks>();
        services.AddScoped<IToolPageShell, ToolPageShell>();
        services.AddScoped<IToolUsageLogger, ToolUsageLogger>();
        services.AddScoped<SharedCookieEvents>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            // CSRFトークンのCookieも認証Cookieと同じ条件で送信します。
            options.Cookie.SecurePolicy = cookieSecurePolicy;
        });
        // 更新要求のCSRF検証を全ホストへ既定で適用します。Portalと各ツールで重複登録せず、個別のControllerで無効化しません。
        services.AddControllersWithViews(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
        services.AddHttpClient();
        services.AddHttpContextAccessor();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CommonAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, CommonAuthorizationResultHandler>();
        // 単独開発は既定の認証方式でCookieを読まず、自端末からの対象ツール利用だけを既定認可で許可します。
        // 匿名モードは既定の認証方式で固定の共通ユーザーを確定し、サイト・ツールの判定は通常どおりDBで行います。
        var defaultScheme = standalone ? StandaloneToolAuthenticationHandler.SchemeName
            : anonymous ? AnonymousSiteAuthenticationHandler.SchemeName : SharedCookieContract.Scheme;
        services.AddAuthorization(options =>
        {
            // 管理者の明示ポリシーは共有Cookieの実ユーザーだけを対象とし、単独開発・匿名モードでは満たしません。
            options.AddPolicy("SalesSupportAdmin", policy => policy.AddAuthenticationSchemes(SharedCookieContract.Scheme).RequireAuthenticatedUser().AddRequirements(new CommonAccessRequirement(Admin: true)));
            // ツールの明示ポリシーは匿名モードでも機能させるため、そのモードの認証方式を使います。管理者の明示ポリシーは共有Cookieのままです。
            var toolScheme = anonymous ? AnonymousSiteAuthenticationHandler.SchemeName : SharedCookieContract.Scheme;
            options.AddPolicy("SalesSupportTool", policy => policy.AddAuthenticationSchemes(toolScheme).RequireAuthenticatedUser().AddRequirements(new CommonAccessRequirement(Tool: true)));
            var fallback = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(defaultScheme);
            if (!standalone) fallback.RequireAuthenticatedUser();
            options.FallbackPolicy = fallback.AddRequirements(new CommonAccessRequirement(Tool: kind == ApplicationKind.Tool)).Build();
        });
        services.AddOptions<Microsoft.AspNetCore.Identity.IdentityOptions>();
        var authentication = services.AddAuthentication(defaultScheme);
        if (standalone) authentication.AddScheme<AuthenticationSchemeOptions, StandaloneToolAuthenticationHandler>(StandaloneToolAuthenticationHandler.SchemeName, _ => { });
        if (anonymous) authentication.AddScheme<AuthenticationSchemeOptions, AnonymousSiteAuthenticationHandler>(AnonymousSiteAuthenticationHandler.SchemeName, _ => { });
        authentication.AddCookie(SharedCookieContract.Scheme, options =>
        {
            options.Cookie.Name = SharedCookieContract.CookieName;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.SlidingExpiration = true;
            options.EventsType = typeof(SharedCookieEvents);
        });
        // 単独開発（共通設定ファイルなし）はPortalと鍵を共有しないため、ASP.NET Core標準の保存先の鍵でCSRFトークン等を保護します。
        if (common is null)
        {
            services.AddDataProtection();
            return services;
        }
        // 設定内のフォルダーは共通設定ファイルからの相対パスで指定するため、起動時に絶対パスへ解決します。
        var keyDirectory = common.ResolvePath(configuration["SalesSupport:DataProtection:KeyDirectory"], "SalesSupport:DataProtection:KeyDirectory");
        if (string.IsNullOrWhiteSpace(keyDirectory) || !Directory.Exists(keyDirectory))
            throw new ConfigurationException("SalesSupport:DataProtection:KeyDirectory");
        var protection = services.AddDataProtection().SetApplicationName("SalesSupport").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("共有鍵の保護にはWindows DPAPIが必要です。");
        protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        return services;
    }

    /// <summary>一時保存領域を使うツールで、領域の未設定を起動時の構成エラーとして検出します。</summary>
    /// <param name="services">検証を追加するサービスです。</param>
    /// <returns>検証を追加したサービスです。</returns>
    /// <remarks>単独開発はファイルを扱わず保存領域を持たないため対象外です。一時保存を使わないツールはこの呼出しを省きます。</remarks>
    public static IServiceCollection RequireTemporaryStorage(this IServiceCollection services) =>
        services.AddOptions<StorageOptions>()
            .Validate<IOptions<CommonOptions>>((storage, common) => common.Value.IsStandalone || !string.IsNullOrWhiteSpace(storage.TemporaryRoot), "Storage:TemporaryRootが必要です。")
            .ValidateOnStart().Services;

    /// <summary>設定された保存領域だけを検証します。起動処理でフォルダーを作成しません。単独開発（共通設定ファイルなし）では保存領域を提供しません。</summary>
    private static void AddStorage(IServiceCollection services, CommonConfiguration? common)
    {
        if (common is null)
        {
            services.AddOptions<StorageOptions>();
            return;
        }
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<StorageOptions>, StoragePathValidation>();
        services.AddOptions<StorageOptions>().Bind(common.Values.GetSection("SalesSupport:Storage"))
            // 保存領域も共通設定ファイルからの相対パスで指定するため、検証より前に絶対パスへ解決します。
            .PostConfigure(options =>
            {
                options.TemporaryRoot = common.ResolvePath(options.TemporaryRoot, "Storage:TemporaryRoot");
                options.PermanentRoot = common.ResolvePath(options.PermanentRoot, "Storage:PermanentRoot");
            })
            .Validate(x => x.CleanupTimeoutSeconds > 0, "Storage:CleanupTimeoutSecondsが不正です。")
            .Validate(x => IsUsableRoot(x.TemporaryRoot), "Storage:TemporaryRootが不正です。")
            .Validate(x => IsUsableRoot(x.PermanentRoot), "Storage:PermanentRootが不正です。")
            .Validate(x => IsSeparated(x.TemporaryRoot, x.PermanentRoot), "Storage:TemporaryRootとPermanentRootを分離してください。")
            .ValidateOnStart();
    }

    /// <summary>メールを使用するホストだけSMTP設定を要求します。匿名モードはメールを使わないため無効にします。</summary>
    private static void AddMail(IServiceCollection services, IConfiguration configuration, ApplicationKind kind, bool anonymous)
    {
        var isDevelopment = configuration["SalesSupport:EnvironmentCode"] == "DEVELOPMENT";
        services.AddOptions<MailTemplateOptions>();
        services.AddOptions<MailOptions>().Bind(configuration.GetSection("SalesSupport:Mail"))
            // 共通設定ファイルや環境変数にEnabledが含まれていても、メール利用可否はアプリ種別で確定します。
            // 匿名モードはログインOTP・通知・問い合わせを提供しないため、Portalでも送信を無効へ固定します。
            .PostConfigure(options => options.Enabled = kind == ApplicationKind.Portal && !anonymous)
            .Validate(x => !x.Enabled || !string.IsNullOrWhiteSpace(x.Host), "Mail:Hostが必要です。")
            .Validate(x => !x.Enabled || x.Port is > 0 and <= 65535, "Mail:Portが不正です。")
            .Validate(x => !x.Enabled || x.TlsMode is MailTlsMode.StartTls or MailTlsMode.SslOnConnect, "Mail:TlsModeを明示してください。")
            .Validate(x => !x.Enabled || CommonValidation.IsEmail(x.From), "Mail:Fromが不正です。")
            .Validate(x => !x.Enabled || string.IsNullOrEmpty(x.ReplyTo) || CommonValidation.IsEmail(x.ReplyTo), "Mail:ReplyToが不正です。")
            .Validate(x => !x.Enabled || string.IsNullOrEmpty(x.UserName) == string.IsNullOrEmpty(x.Password), "Mail:UserNameとPasswordは一組で設定してください。")
            .Validate(x => !x.Enabled || x.TimeoutSeconds is > 0 and <= 2147483, "Mail:TimeoutSecondsが不正です。")
            .Validate(x => !x.Enabled || x.MaxRecipients is > 0, "Mail:MaxRecipientsが必要です。")
            .Validate(x => !x.Enabled || !isDevelopment || CommonValidation.IsEmail(x.DevelopmentRecipient), "Mail:DevelopmentRecipientが必要です。")
            .ValidateOnStart();
    }

    /// <summary>外部HTTPの既定タイムアウトを検証します。接続先は利用側が登録します。</summary>
    private static void AddHttp(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HttpOptions>().Bind(configuration.GetSection("SalesSupport:Http"))
            .Validate(x => x.TimeoutSeconds > 0, "Http:TimeoutSecondsが不正です。").ValidateOnStart();
    }

    /// <summary>未設定は利用しない意味とし、設定済みなら実在する絶対パスを求めます。</summary>
    private static bool IsUsableRoot(string? root) => string.IsNullOrWhiteSpace(root) || Path.IsPathFullyQualified(root) && Directory.Exists(root);

    /// <summary>一方が他方の配下にある保存領域を拒否します。</summary>
    private static bool IsSeparated(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return true;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)) + Path.DirectorySeparatorChar;
        var right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)) + Path.DirectorySeparatorChar;
        return !left.StartsWith(right, comparison) && !right.StartsWith(left, comparison);
    }
}
