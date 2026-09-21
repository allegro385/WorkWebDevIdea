using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <summary>共通データ取得・入力出力を登録します。Identity StoreはPortalが登録します。</summary>
    public static IServiceCollection AddSalesSupportCommon(this IServiceCollection services, IConfiguration configuration, ApplicationKind kind)
    {
        var connection = configuration.GetConnectionString("SalesSupport");
        if (string.IsNullOrWhiteSpace(connection)) throw new ConfigurationException("ConnectionStrings:SalesSupport");
        services.AddOptions<CommonOptions>().Configure(options =>
        {
            options.Kind = kind;
            options.EnvironmentCode = configuration["Portal:EnvironmentCode"] ?? "";
            options.ApplicationName = configuration["SalesSupport:Application:Name"] ?? "";
            options.ToolId = configuration["SalesSupport:Application:ToolId"];
            options.PortalBaseUrl = configuration["SalesSupport:Portal:BaseUrl"] ?? "";
            options.KeyDirectory = configuration["SalesSupport:DataProtection:KeyDirectory"] ?? "";
        }).Validate(x => x.EnvironmentCode is "DEVELOPMENT" or "PRODUCTION", "Portal:EnvironmentCodeが不正です。")
          .Validate(x => !string.IsNullOrWhiteSpace(x.ApplicationName) && x.ApplicationName.Length <= 100, "Application:Nameが不正です。")
          .Validate(x => kind == ApplicationKind.Portal || !string.IsNullOrWhiteSpace(x.ToolId) && x.ToolId.Length <= 20, "Application:ToolIdが必要です。")
          .Validate(x => Uri.TryCreate(x.PortalBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && x.PortalBaseUrl.EndsWith('/'), "Portal:BaseUrlが不正です。")
          .Validate(x => Path.IsPathFullyQualified(x.KeyDirectory) && Directory.Exists(x.KeyDirectory), "DataProtection:KeyDirectoryが必要です。")
          .ValidateOnStart();
        services.AddDbContextFactory<CommonDbContext>(options => options.UseSqlServer(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddDbContextFactory<LogDbContext>(options => options.UseSqlServer(connection));
        services.AddOptions<LoggingOptions>().Bind(configuration.GetSection("SalesSupport:Logging"))
            .Validate(x => x.TimeoutSeconds > 0, "Logging:TimeoutSecondsが不正です。").ValidateOnStart();
        AddStorage(services, configuration);
        AddMail(services, configuration, kind);
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
        services.AddScoped<SharedCookieEvents>();
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        services.AddHttpClient();
        services.AddHttpContextAccessor();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, CommonAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, CommonAuthorizationResultHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("SalesSupportAdmin", policy => policy.RequireAuthenticatedUser().AddRequirements(new CommonAccessRequirement(Admin: true)));
            options.AddPolicy("SalesSupportTool", policy => policy.RequireAuthenticatedUser().AddRequirements(new CommonAccessRequirement(Tool: true)));
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(SharedCookieContract.Scheme)
                .RequireAuthenticatedUser().AddRequirements(new CommonAccessRequirement(Tool: kind == ApplicationKind.Tool)).Build();
        });
        services.AddOptions<Microsoft.AspNetCore.Identity.IdentityOptions>();
        services.AddAuthentication(SharedCookieContract.Scheme).AddCookie(SharedCookieContract.Scheme, options =>
        {
            options.Cookie.Name = SharedCookieContract.CookieName;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.SlidingExpiration = true;
            options.EventsType = typeof(SharedCookieEvents);
        });
        var keyDirectory = configuration["SalesSupport:DataProtection:KeyDirectory"];
        if (string.IsNullOrWhiteSpace(keyDirectory) || !Path.IsPathFullyQualified(keyDirectory) || !Directory.Exists(keyDirectory))
            throw new ConfigurationException("SalesSupport:DataProtection:KeyDirectory");
        var protection = services.AddDataProtection().SetApplicationName("SalesSupport").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("共有鍵の保護にはWindows DPAPIが必要です。");
        protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        return services;
    }

    /// <summary>設定された保存領域だけを検証します。起動処理でフォルダーを作成しません。</summary>
    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<StorageOptions>, StoragePathValidation>();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection("SalesSupport:Storage"))
            .Validate(x => x.CleanupTimeoutSeconds > 0, "Storage:CleanupTimeoutSecondsが不正です。")
            .Validate(x => IsUsableRoot(x.TemporaryRoot), "Storage:TemporaryRootが不正です。")
            .Validate(x => IsUsableRoot(x.PermanentRoot), "Storage:PermanentRootが不正です。")
            .Validate(x => IsSeparated(x.TemporaryRoot, x.PermanentRoot), "Storage:TemporaryRootとPermanentRootを分離してください。")
            .ValidateOnStart();
    }

    /// <summary>メールを使用するホストだけSMTP設定を要求します。</summary>
    private static void AddMail(IServiceCollection services, IConfiguration configuration, ApplicationKind kind)
    {
        var isDevelopment = configuration["Portal:EnvironmentCode"] == "DEVELOPMENT";
        services.AddOptions<MailTemplateOptions>();
        services.AddOptions<MailOptions>().Configure(options => options.Enabled = kind == ApplicationKind.Portal)
            .Bind(configuration.GetSection("SalesSupport:Mail"))
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
