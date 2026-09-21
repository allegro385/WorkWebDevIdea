using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>Portalが所有するDB接続、Identity StoreおよびMVCを登録します。</summary>
public static class PortalServiceExtensions
{
    /// <summary>Commonの契約を利用してPortalの要求処理に必要なサービスを登録します。</summary>
    public static IServiceCollection AddSalesSupportPortal(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSalesSupportCommon(configuration, ApplicationKind.Portal);
        var connection = configuration.GetConnectionString("SalesSupport");
        if (string.IsNullOrWhiteSpace(connection)) throw new ConfigurationException("ConnectionStrings:SalesSupport");

        services.AddDbContext<PortalDbContext>(options => options.UseSqlServer(connection));
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
            options.SignIn.RequireConfirmedEmail = true;
        }).AddSignInManager().AddEntityFrameworkStores<PortalDbContext>().AddDefaultTokenProviders();

        services.AddControllersWithViews(options => options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()));
        return services;
    }
}
