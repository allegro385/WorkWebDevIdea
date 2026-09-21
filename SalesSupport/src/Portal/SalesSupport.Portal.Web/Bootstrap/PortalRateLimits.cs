namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>接続元単位で適用する要求制限のポリシー名です。</summary>
public static class PortalRateLimits
{
    /// <summary>ログイン要求の制限ポリシー名です。</summary>
    public const string Login = "SalesSupport.Login";
    /// <summary>再設定メール請求の制限ポリシー名です。</summary>
    public const string PasswordRequest = "SalesSupport.PasswordRequest";
}

/// <summary>共有接続元からの通常利用にあわせて調整する初期値です。</summary>
public sealed class PortalRateLimitOptions
{
    /// <summary>1分あたりに許可するログイン要求数です。</summary>
    public int LoginPermitLimit { get; set; } = 30;
    /// <summary>1時間あたりに許可する再設定メール請求数です。</summary>
    public int PasswordRequestPermitLimit { get; set; } = 30;
}
