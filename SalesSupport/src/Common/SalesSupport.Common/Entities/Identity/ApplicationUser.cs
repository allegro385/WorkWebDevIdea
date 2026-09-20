using Microsoft.AspNetCore.Identity;

namespace SalesSupport.Common.Entities.Identity;

/// <summary>PortalがIdentity API経由で更新する共通ユーザーです。</summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public string RoleCode { get; set; } = "USER";
    public bool IsActive { get; set; } = true;
    /// <summary>セッション開始時の実UTC時刻です。</summary>
    public System.DateTime? LastAccessAt { get; set; }
}
