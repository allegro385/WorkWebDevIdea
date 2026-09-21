using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Mail;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>Portalが所有するDB接続、Identity Store、業務サービスおよびMVCを登録します。</summary>
public static class PortalServiceExtensions
{
    /// <summary>Commonの契約を利用してPortalの要求処理に必要なサービスを登録します。</summary>
    public static IServiceCollection AddSalesSupportPortal(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddSalesSupportCommon(configuration, ApplicationKind.Portal);
        var connection = configuration.GetConnectionString("SalesSupport");
        if (string.IsNullOrWhiteSpace(connection)) throw new ConfigurationException("ConnectionStrings:SalesSupport");

        services.AddDbContext<PortalDbContext>(options => options.UseSqlServer(connection));
        // 禁止リストは起動時に一度だけ読み込み、欠落・読込不能を構成エラーとして扱います。
        services.AddSingleton<IPasswordPolicy>(PasswordPolicy.Load(configuration["SalesSupport:Password:ForbiddenListPath"], environment.WebRootPath));
        AddIdentity(services);
        services.Configure<MailTemplateOptions>(PasswordLinkMailTemplates.AddDefaults);
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IPasswordLinkService, PasswordLinkService>();
        services.AddScoped<IInitialAdminProvisioner, InitialAdminProvisioner>();
        services.AddScoped<InitialAdminBootstrapCommand>();
        services.AddSingleton<IInitialAdminConsole, InitialAdminConsole>();
        AddRequestLimits(services, configuration);

        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        return services;
    }

    /// <summary>ロック条件、パスワード条件および用途別TokenProviderを登録します。</summary>
    private static void AddIdentity(IServiceCollection services) => services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
            options.SignIn.RequireConfirmedEmail = true;
            // 文字種の組合せは必須にせず、桁数と使用文字はPortalのパスワードポリシーで検証します。
            options.Password.RequiredLength = PasswordPolicy.MinimumLength;
            options.Password.RequiredUniqueChars = 1;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Tokens.PasswordResetTokenProvider = PasswordLinkTokens.ResetProviderName;
        })
        .AddSignInManager()
        .AddEntityFrameworkStores<PortalDbContext>()
        .AddPasswordValidator<PortalPasswordValidator>()
        .AddTokenProvider<InitialPasswordTokenProvider>(PasswordLinkTokens.InitialProviderName)
        .AddTokenProvider<ResetPasswordTokenProvider>(PasswordLinkTokens.ResetProviderName);

    /// <summary>接続元単位の固定時間窓による要求制限を登録します。待機キューは設けません。</summary>
    private static void AddRequestLimits(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortalRateLimitOptions>().Bind(configuration.GetSection("SalesSupport:RateLimits"))
            .Validate(x => x.LoginPermitLimit > 0, "RateLimits:LoginPermitLimitが不正です。")
            .Validate(x => x.PasswordRequestPermitLimit > 0, "RateLimits:PasswordRequestPermitLimitが不正です。")
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PortalRateLimits.Login, context =>
                Fixed(context, TimeSpan.FromMinutes(1), limits => limits.LoginPermitLimit));
            options.AddPolicy(PortalRateLimits.PasswordRequest, context =>
                Fixed(context, TimeSpan.FromHours(1), limits => limits.PasswordRequestPermitLimit));
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("要求が集中しています。しばらく時間をおいてから、もう一度お試しください。", ct);
            };
        });
    }

    /// <summary>信頼済みプロキシ適用後の接続元ごとに、待機させない固定時間窓を作成します。</summary>
    private static RateLimitPartition<string> Fixed(HttpContext context, TimeSpan window, Func<PortalRateLimitOptions, int> permitLimit)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<PortalRateLimitOptions>>().Value;
        var partition = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit(limits),
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
