using Microsoft.AspNetCore.RateLimiting;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Portal.Web.Bootstrap;

var importTestUsers = LocalTestUserImportCommand.IsRequested(args);
// 取込の引数をホスト設定へ渡さず、ロール指定が環境設定等を上書きしないようにします。
var builder = WebApplication.CreateBuilder(importTestUsers ? [] : args);
builder.Services.AddSalesSupportPortal(builder.Configuration, builder.Environment);

var app = builder.Build();
if (importTestUsers)
{
    Console.InputEncoding = new System.Text.UTF8Encoding(false, true);
    await using var scope = app.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<LocalTestUserImportCommand>()
        .RunAsync(args, Console.In, Console.IsInputRedirected);
}
if (LocalTestUserCommand.IsRequested(args))
{
    await using var scope = app.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<LocalTestUserCommand>().RunAsync();
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
