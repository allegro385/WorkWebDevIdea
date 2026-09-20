namespace SalesSupport.Common.Entities.Authentication;

/// <summary>要求ごとの認証確認に必要なユーザー列です。</summary>
public sealed class UserAccessRecord
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public bool IsActive { get; set; }
    public string SecurityStamp { get; set; } = "";
}
