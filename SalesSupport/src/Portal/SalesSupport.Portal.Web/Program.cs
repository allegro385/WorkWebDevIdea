using Microsoft.AspNetCore.RateLimiting;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Portal.Web.Bootstrap;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSalesSupportPortal(builder.Configuration, builder.Environment);

var app = builder.Build();
if (InitialAdminBootstrapCommand.IsRequested(args))
{
    await using var scope = app.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<InitialAdminBootstrapCommand>().RunAsync();
}

app.UseSalesSupportForwardedHeaders();
app.UseSalesSupportErrors();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
// 既定の認可方針は保護対象の画面にだけ適用し、ログイン画面と入場制限案内が読み込む共通資産は匿名で配信します。
app.MapStaticAssets().AllowAnonymous();
app.UseRouting();
app.UseAuthentication();
// 接続元単位の制限は認証の後、認可の前に適用し、超過分を待機させません。
app.UseRateLimiter();
app.UseAuthorization();

app.MapSalesSupportLogout();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


await app.RunAsync();
return 0;

/// <summary>結合テストからPortalホストを参照するためのエントリポイントです。</summary>
public partial class Program;
