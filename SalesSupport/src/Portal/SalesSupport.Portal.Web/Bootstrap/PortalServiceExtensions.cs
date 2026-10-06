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
using SalesSupport.Portal.Web.Areas.Admin;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Mail;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>Portalが所有するDB接続、Identity Store、業務サービスおよびMVCを登録します。</summary>
public static class PortalServiceExtensions
{
    /// <summary>Commonの契約を利用してPortalの要求処理に必要なサービスを登録します。</summary>
    public static IServiceCollection AddSalesSupportPortal(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddSalesSupportCommon(ApplicationKind.Portal);
        // 接続文字列はCommonが共通設定ファイルから取得した値を使用し、Portalの設定ファイルからは読み取りません。
        services.AddDbContext<PortalDbContext>((provider, options) => options.UseSqlServer(provider.GetRequiredService<IConnectionStringProvider>().SalesSupportDatabase));
        AddIdentity(services);
        services.Configure<MailTemplateOptions>(NoticeMailTemplates.AddDefaults);
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ILocalTestUserProvisioner, LocalTestUserProvisioner>();
        services.AddScoped<LocalTestUserCommand>();
        services.AddSingleton<ILocalTestUserConsole, LocalTestUserConsole>();
        AddRequestLimits(services, configuration);
        AddManual(services, configuration);
        AddScreenServices(services);

        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        return services;
    }

    /// <summary>利用者画面と管理画面のサービスを登録します。DbContextを共有するサービスはScopedにします。</summary>
    private static void AddScreenServices(IServiceCollection services)
    {
        services.AddScoped<IHomeService, HomeService>();
        services.AddScoped<IToolQueryService, ToolQueryService>();
        services.AddScoped<IToolFileService, ToolFileService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<IHelpService, HelpService>();
        services.AddScoped<IPreferenceService, PreferenceService>();
        services.AddScoped<IInquiryService, InquiryService>();
        services.AddScoped<IInquiryIdAllocator, InquiryIdAllocator>();
        services.AddScoped<INoticeService, NoticeService>();
        services.AddScoped<IToolAdminService, ToolAdminService>();
        services.AddScoped<IToolFileAdminService, ToolFileAdminService>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IInquiryAdminService, InquiryAdminService>();
        services.AddScoped<ToolEditPageBuilder>();
        // 確認IDはプロセス内のメモリーで保持し、再起動で失効させます。
        services.AddSingleton<IConfirmationStore, ConfirmationStore>();
    }

    /// <summary>マニュアルPDFの管理された配置を検証します。任意の物理パスは受け付けません。</summary>
    private static void AddManual(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortalManualOptions>().Bind(configuration.GetSection("SalesSupport:Manual"))
            .Validate(x => IsSafeRelativePath(x.RelativePath), "Manual:RelativePathが不正です。")
            .Validate(x => IsSafeRelativePath(x.FileName) && x.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase), "Manual:FileNameが不正です。")
            .ValidateOnStart();
    }

    /// <summary>絶対パス・親移動・制御文字を含まない相対経路だけを許可します。</summary>
    private static bool IsSafeRelativePath(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl) && !value.Contains(':') && !Path.IsPathRooted(value)
        && value.Replace('\\', '/').Split('/').All(segment => segment is not ("" or "." or ".."));

    /// <summary>ロック条件とIdentity標準のパスワード検証を登録します。</summary>
    private static void AddIdentity(IServiceCollection services) => services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
            options.SignIn.RequireConfirmedEmail = false;
            // ログインIDの業務上の文字規則は外部連携の設計時に決定します。
            options.User.AllowedUserNameCharacters = string.Empty;
            // 以下は開発用ユーザー作成の条件です。連携済みハッシュの照合に新規設定条件を適用しません。
            options.Password.RequiredLength = 6;
            options.Password.RequiredUniqueChars = 1;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddSignInManager()
        .AddEntityFrameworkStores<PortalDbContext>();

    /// <summary>接続元単位の固定時間窓による要求制限を登録します。待機キューは設けません。</summary>
    private static void AddRequestLimits(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortalRateLimitOptions>().Bind(configuration.GetSection("SalesSupport:RateLimits"))
            .Validate(x => x.LoginPermitLimit > 0, "RateLimits:LoginPermitLimitが不正です。")
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PortalRateLimits.Login, context =>
                Fixed(context, TimeSpan.FromMinutes(1), limits => limits.LoginPermitLimit));
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
