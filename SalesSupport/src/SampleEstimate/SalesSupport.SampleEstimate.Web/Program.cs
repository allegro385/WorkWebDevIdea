using SalesSupport.Common.DependencyInjection;
using SalesSupport.SampleEstimate.Web.Bootstrap;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSalesSupportTool(builder.Environment, builder.Configuration);

var app = builder.Build();

// 転送ヘッダー、セキュリティヘッダー、共通エラー応答、HTTPS、静的資産、認証・認可、共通ログアウトと
// 既定のルートはCommonが順序ごと組み込みます。ツール側へ個別のミドルウェア呼出しを展開しません。
app.UseSalesSupportTool();

await app.RunAsync();
