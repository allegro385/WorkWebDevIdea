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
using SalesSupport.Common.Logging;
using SalesSupport.Common.ErrorHandling;

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
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUserAccessor>(provider => provider.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<IAccessEvaluator, AccessEvaluator>();
        services.AddSingleton<IDelimitedTextWriter, DelimitedTextWriter>();
        services.AddScoped<SharedCookieEvents>();
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
}
