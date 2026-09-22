using SalesSupport.Common.Authentication;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Template.Web.Bootstrap;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSalesSupportTool();

var app = builder.Build();

app.UseSalesSupportForwardedHeaders();
app.UseSalesSupportErrors();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
// 既定の認可方針は保護対象の画面にだけ適用し、共通レイアウトが読み込む資産は匿名で配信します。
app.MapStaticAssets().AllowAnonymous();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ヘッダーのログアウトはCommonの共通処理で共有Cookieを破棄し、Portalへ戻します。
app.MapSalesSupportLogout();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Tool}/{action=Index}/{id?}")
    .WithStaticAssets();

await app.RunAsync();
