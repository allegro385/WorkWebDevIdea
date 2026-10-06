namespace SalesSupport.Common.Entities.Authentication;

/// <summary>要求時にロール定義の存在を確認する読取り行です。</summary>
public sealed class RoleAccessRecord
{
    public string RoleCode { get; set; } = "";
    /// <summary>個人設定へのメニューを表示できるロールです。</summary>
    public bool NoticeMailEnabled { get; set; }
}

/// <summary>要求時にツールと一般ロールの割当てを確認する読取り行です。</summary>
public sealed class ToolRoleAccessRecord
{
    public string ToolId { get; set; } = "";
    public string RoleCode { get; set; } = "";
}
