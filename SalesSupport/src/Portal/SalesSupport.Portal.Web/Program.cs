using SalesSupport.Common.Authentication;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Portal.Web.Bootstrap;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSalesSupportPortal(builder.Configuration);

var app = builder.Build();
app.UseSalesSupportForwardedHeaders();
app.UseSalesSupportErrors();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapSalesSupportLogout();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

/// <summary>結合テストからPortalホストを参照するためのエントリポイントです。</summary>
public partial class Program;
