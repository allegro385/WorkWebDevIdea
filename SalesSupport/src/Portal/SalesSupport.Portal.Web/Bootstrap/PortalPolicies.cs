namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>Commonが登録した認可ポリシー名です。管理Controllerへ明示的に指定します。</summary>
public static class PortalPolicies
{
    /// <summary>サイト入場に加えてADMINを要求するポリシーです。</summary>
    public const string Admin = "SalesSupportAdmin";
}
