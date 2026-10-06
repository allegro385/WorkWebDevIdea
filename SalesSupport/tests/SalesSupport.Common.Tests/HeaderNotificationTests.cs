using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.Entities.Authentication;
using SalesSupport.Common.UI;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通ヘッダーが現在のDBロール通知フラグで個人設定の導線を切り替えることを確認します。</summary>
public sealed class HeaderNotificationTests
{
    /// <summary>管理者にもロールフラグを適用し、OFFと未定義ロールでは個人設定を隠します。</summary>
    [Theory]
    [InlineData("ADMIN", true, true)]
    [InlineData("ADMIN", false, false)]
    [InlineData("A", true, true)]
    [InlineData("D", false, false)]
    [InlineData("UNDEFINED", true, false)]
    public async Task HeaderUsesCurrentRoleFlag(string roleCode, bool allowed, bool expected)
    {
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using var seed = new SeedContext(new DbContextOptionsBuilder<SeedContext>().UseInMemoryDatabase(name, root).Options);
        if (roleCode != "UNDEFINED")
        {
            seed.Add(new RoleAccessRecord { RoleCode = roleCode, NoticeMailEnabled = allowed });
            await seed.SaveChangesAsync();
        }
        await using var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(name, root).Options);
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "試験利用者", roleCode));
        var options = Options.Create(new CommonOptions { PortalBaseUrl = "https://example.invalid/", EnvironmentCode = "DEVELOPMENT" });
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var component = new SalesSupportHeaderViewComponent(current, options, new SalesSupportLinks(options, http), db)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext
                {
                    HttpContext = http.HttpContext,
                    ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                        new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
                }
            }
        };
        var result = Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync());
        var model = Assert.IsType<SalesSupportHeaderModel>(result.ViewData!.Model);
        Assert.Equal(expected, model.Menus.SelectMany(x => x.Links).Any(x => x.Label == "個人設定"));
        Assert.Empty(model.AccountLinks);
        if (expected)
        {
            seed.Set<RoleAccessRecord>().Single().NoticeMailEnabled = false;
            await seed.SaveChangesAsync();
            var changed = Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync());
            Assert.DoesNotContain(Assert.IsType<SalesSupportHeaderModel>(changed.ViewData!.Model).Menus.SelectMany(x => x.Links), x => x.Label == "個人設定");
        }
    }

    /// <summary>本番と同じ読取りモデルで使い捨てデータを投入します。</summary>
    private sealed class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
    {
        /// <summary>Commonの標準マッピングを使い、参照ContextとDBを共有します。</summary>
        protected override void OnModelCreating(ModelBuilder model) => CommonMappings.ConfigureReadModels(model);
    }
}
