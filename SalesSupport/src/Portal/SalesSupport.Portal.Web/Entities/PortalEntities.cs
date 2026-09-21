using SalesSupport.Common.Entities;

namespace SalesSupport.Portal.Web.Entities;

/// <summary>ポータル上でツールを分類する表示カテゴリです。</summary>
public sealed class ToolCategory : AuditedEntity
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>ポータルから案内・起動・配布するツールの登録情報です。</summary>
public sealed class Tool : AuditedEntity
{
    public string ToolId { get; set; } = "";
    public int CategoryId { get; set; }
    public string ToolName { get; set; } = "";
    public string? ToolSummary { get; set; }
    public string? Remarks { get; set; }
    public Guid OwnerUserId { get; set; }
    public string ToolType { get; set; } = "";
    public string? WebAppUrl { get; set; }
    public string Status { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>利用者本人が変更できる通知設定です。</summary>
public sealed class UserPreference : AuditedEntity
{
    public Guid UserId { get; set; }
    public bool SystemNoticeMailEnabled { get; set; }
    public bool FavoriteToolNoticeMailEnabled { get; set; }
}

/// <summary>利用者とお気に入りツールの関連を保持します。</summary>
public sealed class UserToolFavorite : AuditedEntity
{
    public Guid UserId { get; set; }
    public string ToolId { get; set; } = "";
}

/// <summary>ツールの公開済みバージョンと変更内容を保持します。</summary>
public sealed class ToolVersionHistory : AuditedEntity
{
    public int ToolHistoryId { get; set; }
    public string ToolId { get; set; } = "";
    public string Version { get; set; } = "";
    public Guid ModifiedByUserId { get; set; }
    public string ChangeDescription { get; set; } = "";
    public DateOnly ReleasedAt { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>ツールに紐付く配布アプリまたは参考資料の保存情報です。</summary>
public sealed class ToolFile : AuditedEntity
{
    public int FileId { get; set; }
    public string ToolId { get; set; } = "";
    public string FileCategory { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Extension { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public Guid UploadedByUserId { get; set; }
    public System.DateTime UploadedAt { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>サイト全体または一つのツールに対するお知らせです。</summary>
public sealed class Notice : AuditedEntity
{
    public int NoticeId { get; set; }
    public string NoticeType { get; set; } = "";
    public string? ToolId { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsPublished { get; set; }
    public System.DateTime? MailSentAt { get; set; }
}

/// <summary>FAQを分類して並べるためのカテゴリです。</summary>
public sealed class FaqCategory : AuditedEntity
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>利用者へ公開する質問と回答の組です。</summary>
public sealed class FaqItem : AuditedEntity
{
    public int FaqId { get; set; }
    public int CategoryId { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsPublished { get; set; }
}

/// <summary>利用者から受け付け、管理者が対応状況を管理する問い合わせです。</summary>
public sealed class Inquiry : AuditedEntity
{
    public string InquiryId { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? ToolId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public string Content { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid? AssigneeUserId { get; set; }
    public string? AdminNote { get; set; }
}
