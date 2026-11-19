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

/// <summary>共通ヘッダーが現在のDBロール通知フラグと利用者の種類で、本人向けの導線とログインを切り替えることを確認します。</summary>
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

    /// <summary>単独開発では利用者がいなくてもDBを読まず、Portalへのリンク・メニュー・ログアウトを出さずに単独開発と示します。</summary>
    [Fact]
    public async Task StandaloneHeaderHasNoPortalLinks()
    {
        await using var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "T001", ExecutionMode = ToolExecutionMode.Standalone
        });
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var component = new SalesSupportHeaderViewComponent(new CurrentUserAccessor(), options, new SalesSupportLinks(options, http), db)
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
        var model = Assert.IsType<SalesSupportHeaderModel>(Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync()).ViewData!.Model);
        Assert.True(model.IsStandalone);
        Assert.True(model.IsDevelopment);
        Assert.Null(model.DisplayName);
        Assert.Null(model.PortalTopUrl);
        Assert.Null(model.LogoutUrl);
        Assert.Empty(model.Menus);
        Assert.Empty(model.AccountLinks);
    }

    /// <summary>匿名モードの共通ユーザーには、全ツールとマニュアルだけのメニューと、表示中の画面へ戻るログインを表示します。</summary>
    [Fact]
    public async Task AnonymousHeaderOffersLoginToCommonUser()
    {
        await using var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode));
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.PathBase = "/tools/T001/app";
        context.Request.Path = "/records";
        context.Request.QueryString = new QueryString("?page=2");

        var model = await InvokeAnonymousAsync(current, context, db);

        Assert.Null(model.DisplayName);
        Assert.Equal("https://example.invalid/account/login?returnUrl=%2Ftools%2FT001%2Fapp%2Frecords%3Fpage%3D2", model.LoginUrl);
        Assert.Equal(["全ツール", "利用マニュアル・FAQ"], model.Menus.SelectMany(x => x.Links).Select(x => x.Label));
    }

    /// <summary>更新要求の結果画面では、GETで開けない経路を戻り先にせずログインへ案内します。</summary>
    [Fact]
    public async Task AnonymousHeaderOmitsReturnUrlForPost()
    {
        await using var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode));
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/execute";

        var model = await InvokeAnonymousAsync(current, context, db);

        Assert.Equal("https://example.invalid/account/login", model.LoginUrl);
    }

    /// <summary>匿名モードでもログインした利用者には、メールOTPのモードと同じ本人向けメニューとログアウトを表示します。</summary>
    [Fact]
    public async Task AnonymousHeaderShowsSignedInUserMenus()
    {
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using var seed = new SeedContext(new DbContextOptionsBuilder<SeedContext>().UseInMemoryDatabase(name, root).Options);
        seed.Add(new RoleAccessRecord { RoleCode = "A", NoticeMailEnabled = true });
        await seed.SaveChangesAsync();
        await using var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(name, root).Options);
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "試験太郎", "A"));

        var model = await InvokeAnonymousAsync(current, new DefaultHttpContext(), db);

        Assert.Equal("試験太郎", model.DisplayName);
        Assert.Null(model.LoginUrl);
        Assert.NotNull(model.LogoutUrl);
        Assert.Contains(model.Menus.SelectMany(x => x.Links), x => x.Label == "お気に入りツール");
        Assert.Contains(model.Menus.SelectMany(x => x.Links), x => x.Label == "問い合わせ・ご意見");
    }

    /// <summary>匿名モードの共通ヘッダーを指定の要求で描画し、表示内容を返します。</summary>
    /// <param name="current">検証済み利用者です。</param>
    /// <param name="context">表示中の要求です。</param>
    /// <param name="db">ロールの通知許可を読む共通DBです。</param>
    /// <returns>共通ヘッダーの表示内容です。</returns>
    private static async Task<SalesSupportHeaderModel> InvokeAnonymousAsync(CurrentUserAccessor current, HttpContext context, CommonDbContext db)
    {
        var options = Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Portal, PortalBaseUrl = "https://example.invalid/", EnvironmentCode = "PRODUCTION",
            AuthenticationMode = SiteAuthenticationMode.Anonymous
        });
        var http = new HttpContextAccessor { HttpContext = context };
        var component = new SalesSupportHeaderViewComponent(current, options, new SalesSupportLinks(options, http), db)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext
                {
                    HttpContext = context,
                    ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                        new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
                }
            }
        };
        return Assert.IsType<SalesSupportHeaderModel>(Assert.IsType<ViewViewComponentResult>(await component.InvokeAsync()).ViewData!.Model);
    }

    /// <summary>本番と同じ読取りモデルで使い捨てデータを投入します。</summary>
    private sealed class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
    {
        /// <summary>Commonの標準マッピングを使い、参照ContextとDBを共有します。</summary>
        protected override void OnModelCreating(ModelBuilder model) => CommonMappings.ConfigureReadModels(model);
    }
}
