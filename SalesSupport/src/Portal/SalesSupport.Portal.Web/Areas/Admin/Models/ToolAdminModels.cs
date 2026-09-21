using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;

namespace SalesSupport.Portal.Web.Areas.Admin.Models;

/// <summary>担当者・修正担当者の選択肢1件です。</summary>
/// <param name="UserId">対象ユーザーです。</param>
/// <param name="DisplayName">画面へ表示する名前です。</param>
public sealed record UserOption(Guid UserId, string DisplayName);

/// <summary>ツールカテゴリの選択肢1件です。</summary>
/// <param name="CategoryId">カテゴリの識別子です。</param>
/// <param name="CategoryName">画面へ表示する名称です。</param>
public sealed record CategoryOption(int CategoryId, string CategoryName);

/// <summary>ツール選択画面の絞り込み条件です。</summary>
public sealed class ToolFilterInput
{
    [Display(Name = "カテゴリ")]
    public int? CategoryId { get; set; }

    [Display(Name = "担当者")]
    public Guid? OwnerUserId { get; set; }

    [Display(Name = "状態")]
    public string? Status { get; set; }
}

/// <summary>ツール選択画面の1行です。</summary>
/// <param name="ToolId">ツールの識別子です。</param>
/// <param name="SortOrder">表示順です。一覧で並べ替えて一括保存します。</param>
/// <param name="CategoryName">ツールカテゴリの表示名です。</param>
/// <param name="ToolName">ツール名です。</param>
/// <param name="OwnerName">ツール担当者の表示名です。</param>
/// <param name="Status">TOOL_STATUSのコード値です。</param>
/// <param name="Version">現在のバージョンです。未選択・履歴0件では空文字です。</param>
/// <param name="UpdateCount">同時更新の検出に使用する監査列です。</param>
public sealed record ToolSelectionItem(string ToolId, int SortOrder, string CategoryName, string ToolName,
    string OwnerName, string Status, string Version, int UpdateCount);

/// <summary>A001 ツール管理のツール選択画面の表示情報です。</summary>
public sealed class ToolSelectionViewModel
{
    /// <summary>入力された絞り込み条件です。</summary>
    public ToolFilterInput Filter { get; init; } = new();

    /// <summary>非公開を含むすべてのツールから、条件に一致した行です。</summary>
    public IReadOnlyList<ToolSelectionItem> Tools { get; init; } = [];

    /// <summary>カテゴリの選択肢です。</summary>
    public IReadOnlyList<CategoryOption> Categories { get; init; } = [];

    /// <summary>担当者の選択肢です。有効なシステム管理者だけを表示します。</summary>
    public IReadOnlyList<UserOption> Owners { get; init; } = [];

    /// <summary>状態の選択肢です。</summary>
    public IReadOnlyList<CodeOption> Statuses { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>ツール表示順の一括保存で受け取る1行です。</summary>
public sealed class ToolOrderRow
{
    /// <summary>対象ツールです。</summary>
    public string? ToolId { get; set; }

    /// <summary>入力された表示順です。</summary>
    public int SortOrder { get; set; }

    /// <summary>取得時のUpdateCountです。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>ツール表示順の一括保存の入力です。</summary>
public sealed class ToolOrderInput
{
    /// <summary>並べ替え対象の全行です。提出された集合全体で競合を判定します。</summary>
    public List<ToolOrderRow> Rows { get; set; } = [];
}

/// <summary>基本情報区画の入力です。提供種別と表示順は更新対象に含めません。</summary>
public sealed class ToolBasicInput
{
    [Display(Name = "ツール名")]
    [Required(ErrorMessage = "ツール名を入力してください。")]
    [StringLength(200, ErrorMessage = "ツール名は200文字以内で入力してください。")]
    public string? ToolName { get; set; }

    [Display(Name = "カテゴリ")]
    public int CategoryId { get; set; }

    [Display(Name = "担当者")]
    public Guid OwnerUserId { get; set; }

    [Display(Name = "概要")]
    [StringLength(1000, ErrorMessage = "概要は1,000文字以内で入力してください。")]
    public string? ToolSummary { get; set; }

    [Display(Name = "備考")]
    [StringLength(2000, ErrorMessage = "備考は2,000文字以内で入力してください。")]
    public string? Remarks { get; set; }

    [Display(Name = "状態")]
    public string? Status { get; set; }

    /// <summary>取得時のUpdateCountです。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>基本情報区画の表示情報です。</summary>
public sealed class ToolBasicSectionViewModel
{
    /// <summary>再表示時も維持する入力です。</summary>
    public ToolBasicInput Input { get; set; } = new();

    /// <summary>参照表示する提供種別です。</summary>
    public string ToolType { get; init; } = "";

    /// <summary>カテゴリの選択肢です。</summary>
    public IReadOnlyList<CategoryOption> Categories { get; init; } = [];

    /// <summary>担当者の選択肢です。有効なシステム管理者だけを表示します。</summary>
    public IReadOnlyList<UserOption> Owners { get; init; } = [];

    /// <summary>状態の選択肢です。</summary>
    public IReadOnlyList<CodeOption> Statuses { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>バージョン履歴1行の表示情報です。</summary>
/// <param name="ToolHistoryId">履歴の識別子です。</param>
/// <param name="Version">バージョン番号です。</param>
/// <param name="ReleasedAt">リリース日です。</param>
/// <param name="ModifiedByName">修正担当者の表示名です。</param>
/// <param name="ChangeDescription">更新内容です。</param>
/// <param name="IsCurrent">現在のバージョンとして選択されているかどうかです。</param>
/// <param name="CanDelete">削除条件を満たしているかどうかです。</param>
/// <param name="UpdateCount">同時更新の検出に使用する監査列です。</param>
public sealed record ToolVersionRow(int ToolHistoryId, string Version, DateOnly ReleasedAt, string ModifiedByName,
    string ChangeDescription, bool IsCurrent, bool CanDelete, int UpdateCount);

/// <summary>バージョン履歴の登録・編集の入力です。</summary>
public sealed class ToolVersionInput
{
    /// <summary>更新対象の履歴です。新規登録ではnullです。</summary>
    public int? ToolHistoryId { get; set; }

    [Display(Name = "バージョン")]
    [Required(ErrorMessage = "バージョンを入力してください。")]
    public string? Version { get; set; }

    [Display(Name = "リリース日")]
    [Required(ErrorMessage = "リリース日を入力してください。")]
    public DateOnly? ReleasedAt { get; set; }

    [Display(Name = "修正担当者")]
    public Guid ModifiedByUserId { get; set; }

    [Display(Name = "更新内容")]
    [Required(ErrorMessage = "更新内容を入力してください。")]
    [StringLength(2000, ErrorMessage = "更新内容は2,000文字以内で入力してください。")]
    public string? ChangeDescription { get; set; }

    /// <summary>取得時のUpdateCountです。新規登録では0です。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>バージョン更新情報区画の表示情報です。</summary>
public sealed class ToolVersionSectionViewModel
{
    /// <summary>登録済みの全履歴をバージョン番号の降順で並べた一覧です。</summary>
    public IReadOnlyList<ToolVersionRow> Versions { get; init; } = [];

    /// <summary>現在のバージョンとして選択されている履歴です。未選択ではnullです。</summary>
    public int? CurrentHistoryId { get; init; }

    /// <summary>編集欄の入力です。初期状態ではnullとし、編集欄を表示しません。</summary>
    public ToolVersionInput? Editor { get; set; }

    /// <summary>修正担当者の選択肢です。</summary>
    public IReadOnlyList<UserOption> Users { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>現在のバージョン保存で受け取る履歴1件の版です。</summary>
public sealed class ToolVersionStampRow
{
    /// <summary>対象の履歴です。</summary>
    public int ToolHistoryId { get; set; }

    /// <summary>取得時のUpdateCountです。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>現在のバージョン保存の入力です。対象集合の相違も競合として扱います。</summary>
public sealed class ToolCurrentVersionInput
{
    /// <summary>現在のバージョンとして選択した履歴です。</summary>
    [Display(Name = "現在のバージョン")]
    public int ToolHistoryId { get; set; }

    /// <summary>画面に表示していた履歴集合の識別子と版です。</summary>
    public List<ToolVersionStampRow> Rows { get; set; } = [];
}

/// <summary>提供内容区画に表示するファイル1件です。</summary>
/// <param name="FileId">ファイルの識別子です。</param>
/// <param name="DisplayName">登録された表示名です。</param>
/// <param name="OriginalFileName">登録されたファイル名です。</param>
/// <param name="FileSizeBytes">ファイルの容量です。</param>
/// <param name="SortOrder">参考資料の表示順です。</param>
/// <param name="UploaderName">最終登録・差し替えを行った管理者の表示名です。</param>
/// <param name="UploaderEmail">最終登録・差し替えを行った管理者のメールアドレスです。</param>
/// <param name="UploadedAt">最終登録・差し替え日時のJST表示値です。</param>
/// <param name="UpdateCount">同時更新の検出に使用する監査列です。</param>
public sealed record ToolFileRow(int FileId, string DisplayName, string OriginalFileName, long FileSizeBytes, int SortOrder,
    string UploaderName, string UploaderEmail, DateTimeOffset UploadedAt, int UpdateCount);

/// <summary>参考資料の登録・編集の入力です。</summary>
public sealed class ToolReferenceInput
{
    /// <summary>更新対象の参考資料です。新規追加ではnullです。</summary>
    public int? FileId { get; set; }

    [Display(Name = "表示名")]
    [Required(ErrorMessage = "表示名を入力してください。")]
    [StringLength(200, ErrorMessage = "表示名は200文字以内で入力してください。")]
    public string? DisplayName { get; set; }

    /// <summary>取得時のUpdateCountです。新規追加では0です。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>参考資料の並べ替えで受け取る1行です。</summary>
public sealed class ToolFileOrderRow
{
    /// <summary>対象の参考資料です。</summary>
    public int FileId { get; set; }

    /// <summary>入力された表示順です。</summary>
    public int SortOrder { get; set; }

    /// <summary>取得時のUpdateCountです。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>参考資料の並べ替えの入力です。</summary>
public sealed class ToolFileOrderInput
{
    /// <summary>並べ替え対象の全行です。</summary>
    public List<ToolFileOrderRow> Rows { get; set; } = [];
}

/// <summary>提供内容区画の表示情報です。</summary>
public sealed class ToolContentSectionViewModel
{
    /// <summary>参照表示する提供種別です。入力欄の表示を切り替えます。</summary>
    public string ToolType { get; init; } = "";

    [Display(Name = "ページURL")]
    public string? WebAppUrl { get; init; }

    /// <summary>ページURL保存時の競合判定に使用するToolsのUpdateCountです。</summary>
    public int ToolUpdateCount { get; init; }

    /// <summary>登録済みの配布アプリです。未登録ではnullです。</summary>
    public ToolFileRow? AppFile { get; init; }

    /// <summary>登録済みの参考資料を表示順で並べた一覧です。</summary>
    public IReadOnlyList<ToolFileRow> References { get; init; } = [];

    /// <summary>参考資料の編集欄の入力です。初期状態ではnullとし、編集欄を表示しません。</summary>
    public ToolReferenceInput? Editor { get; set; }

    /// <summary>配布アプリのアップロード条件の案内文です。</summary>
    public string AppHint { get; init; } = "";

    /// <summary>参考資料のアップロード条件の案内文です。</summary>
    public string ReferenceHint { get; init; } = "";

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>A001 ツール管理のツール編集画面の表示情報です。</summary>
public sealed class ToolEditViewModel
{
    /// <summary>編集対象のツールです。</summary>
    public string ToolId { get; init; } = "";

    /// <summary>編集対象のツール名です。</summary>
    public string ToolName { get; init; } = "";

    /// <summary>お知らせ区画です。</summary>
    public NoticeSectionViewModel Notices { get; set; } = new();

    /// <summary>バージョン更新情報区画です。</summary>
    public ToolVersionSectionViewModel Versions { get; set; } = new();

    /// <summary>基本情報区画です。</summary>
    public ToolBasicSectionViewModel Basic { get; set; } = new();

    /// <summary>提供内容区画です。</summary>
    public ToolContentSectionViewModel Content { get; set; } = new();
}
