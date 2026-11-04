using SalesSupport.Common.DependencyInjection;
using SalesSupport.Portal.Web.Bootstrap;

var importUsers = UserImportCommand.IsRequested(args);
// 取込の引数をホスト設定へ渡さず、ファイル名やオプションが環境設定等を上書きしないようにします。
var builder = WebApplication.CreateBuilder(importUsers ? [] : args);
builder.Services.AddSalesSupportPortal(builder.Environment, builder.Configuration);

var app = builder.Build();
if (importUsers)
{
    await using var scope = app.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<UserImportCommand>().RunAsync(args, Console.In, Console.Out);
}
// 転送ヘッダー、セキュリティヘッダー、共通エラー応答、HTTPS、静的資産、認証・接続元単位の制限・認可、
// 共通ログアウトと既定のルートはCommonが順序ごと組み込みます。Portal側へ個別のミドルウェア呼出しを展開しません。
app.UseSalesSupportPortal();

await app.RunAsync();
return 0;

/// <summary>結合テストからPortalホストを参照するためのエントリポイントです。</summary>
public partial class Program;
