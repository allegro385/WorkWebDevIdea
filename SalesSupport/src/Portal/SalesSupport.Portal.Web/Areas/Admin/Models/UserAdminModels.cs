using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Models;

/// <summary>ユーザー管理の検索条件です。空欄は条件なしとして扱います。</summary>
public sealed class UserSearchInput
{
    [Display(Name = "表示名")]
    [StringLength(100)]
    public string? DisplayName { get; set; }

    [Display(Name = "メールアドレス")]
    [StringLength(256)]
    public string? Email { get; set; }

    [Display(Name = "権限")]
    public string? RoleCode { get; set; }

    /// <summary>有効状態の絞り込みです。ACTIVE、INACTIVEまたは未指定です。</summary>
    [Display(Name = "状態")]
    public string? State { get; set; }

    /// <summary>ロック状態の絞り込みです。LOCKED、UNLOCKEDまたは未指定です。</summary>
    [Display(Name = "ロック状態")]
    public string? LockState { get; set; }
}

/// <summary>ユーザー一覧の1行です。内部ユーザーIDは画面へ表示しません。</summary>
/// <param name="UserId">編集画面への遷移に使用する識別子です。</param>
/// <param name="DisplayName">表示名です。</param>
/// <param name="Email">メールアドレスです。</param>
/// <param name="RoleCode">USER_ROLEのコード値です。</param>
/// <param name="IsActive">有効かどうかです。</param>
/// <param name="IsLocked">ロック中かどうかです。</param>
/// <param name="LastAccessAt">最終利用日時のJST表示値です。</param>
public sealed record UserListItem(Guid UserId, string DisplayName, string Email, string RoleCode, bool IsActive,
    bool IsLocked, DateTimeOffset? LastAccessAt);

/// <summary>A002 ユーザー管理の一覧画面の表示情報です。</summary>
public sealed class UserListViewModel
{
    /// <summary>入力された検索条件です。</summary>
    public UserSearchInput Search { get; init; } = new();

    /// <summary>条件に一致したユーザーです。</summary>
    public IReadOnlyList<UserListItem> Users { get; init; } = [];

    /// <summary>権限の選択肢です。</summary>
    public IReadOnlyList<CodeOption> Roles { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>ユーザー編集の入力です。権限は画面から変更しません。</summary>
public sealed class UserEditInput
{
    /// <summary>編集対象のユーザーです。</summary>
    public Guid UserId { get; set; }

    [Display(Name = "表示名")]
    [Required(ErrorMessage = "表示名を入力してください。")]
    [StringLength(100, ErrorMessage = "表示名は100文字以内で入力してください。")]
    public string? DisplayName { get; set; }

    [Display(Name = "メールアドレス")]
    [Required(ErrorMessage = "メールアドレスを入力してください。")]
    [StringLength(256, ErrorMessage = "メールアドレスは256文字以内で入力してください。")]
    public string? Email { get; set; }

    [Display(Name = "状態")]
    public bool IsActive { get; set; }

    /// <summary>取得時のConcurrencyStampです。同時更新の検出に使用します。</summary>
    public string? ConcurrencyStamp { get; set; }
}

/// <summary>A002 ユーザー管理の編集画面の表示情報です。</summary>
public sealed class UserEditViewModel
{
    /// <summary>再表示時も維持する入力です。</summary>
    public UserEditInput Input { get; set; } = new();

    /// <summary>参照表示する権限です。画面からは変更しません。</summary>
    public string RoleCode { get; init; } = "";

    /// <summary>ロック中かどうかです。</summary>
    public bool IsLocked { get; init; }

    /// <summary>ロック終了日時のJST表示値です。</summary>
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>ログイン失敗回数です。</summary>
    public int AccessFailedCount { get; init; }

    /// <summary>初回パスワードを設定済みかどうかです。</summary>
    public bool HasPassword { get; init; }

    /// <summary>最終利用日時のJST表示値です。</summary>
    public DateTimeOffset? LastAccessAt { get; init; }

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>TSV取込の確認画面と結果画面の表示情報です。</summary>
public sealed class UserImportViewModel
{
    /// <summary>検証に成功した取込予定の行です。</summary>
    public IReadOnlyList<UserImportRow> Rows { get; init; } = [];

    /// <summary>行番号付きの検証エラーです。1件でもあれば登録を開始しません。</summary>
    public IReadOnlyList<UserImportError> Errors { get; init; } = [];

    /// <summary>実行時に一度だけ使用する確認IDです。確認できない場合はnullです。</summary>
    public Guid? ConfirmationId { get; init; }

    /// <summary>登録処理の結果です。確認段階では空です。</summary>
    public IReadOnlyList<UserImportResultRow> Results { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}
