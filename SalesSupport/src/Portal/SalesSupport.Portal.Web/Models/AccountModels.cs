using System.ComponentModel.DataAnnotations;
using SalesSupport.Portal.Web.Authentication;

namespace SalesSupport.Portal.Web.Models;

/// <summary>ログイン画面の入力です。パスワードは再表示しません。</summary>
public sealed class LoginInput
{
    [Display(Name = "メールアドレス")]
    [Required(ErrorMessage = "メールアドレスを入力してください。")]
    [StringLength(256, ErrorMessage = "メールアドレスは256文字以内で入力してください。")]
    public string? Email { get; set; }

    [Display(Name = "パスワード")]
    [Required(ErrorMessage = "パスワードを入力してください。")]
    public string? Password { get; set; }

    /// <summary>検証済みのサイト内戻り先です。外部URLは受け付けません。</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>ログイン画面の表示情報です。</summary>
public sealed class LoginViewModel
{
    /// <summary>再表示時も維持する入力です。</summary>
    public LoginInput Input { get; init; } = new();
    /// <summary>配置設定で登録された社内システム担当の連絡先です。未設定では表示しません。</summary>
    public string? SupportContact { get; init; }
}

/// <summary>再設定メール請求の入力です。</summary>
public sealed class PasswordRequestInput
{
    [Display(Name = "メールアドレス")]
    [Required(ErrorMessage = "メールアドレスを入力してください。")]
    [StringLength(256, ErrorMessage = "メールアドレスは256文字以内で入力してください。")]
    public string? Email { get; set; }
}

/// <summary>設定リンクから開くパスワード入力と、画面の表示情報です。</summary>
public sealed class PasswordLinkInput
{
    /// <summary>リンクに含まれる対象ユーザーです。</summary>
    public Guid User { get; set; }

    /// <summary>リンクに含まれる設定用トークンです。</summary>
    public string? Token { get; set; }

    [Display(Name = "新しいパスワード")]
    public string? Password { get; set; }

    [Display(Name = "新しいパスワード（確認）")]
    public string? PasswordConfirmation { get; set; }

    /// <summary>表示中の用途です。フォームからは受け取らず、経路から決定します。</summary>
    public PasswordLinkKind Kind { get; set; }

    /// <summary>用途に応じた画面名を返します。</summary>
    public string Title => Kind == PasswordLinkKind.Initial ? "初回パスワード設定" : "パスワード再設定";

    /// <summary>用途に応じた送信先アクション名を返します。</summary>
    public string ActionName => Kind == PasswordLinkKind.Initial ? "PasswordSetup" : "PasswordReset";
}

/// <summary>パスワード変更画面の入力です。</summary>
public sealed class PasswordChangeInput
{
    [Display(Name = "現在のパスワード")]
    [Required(ErrorMessage = "現在のパスワードを入力してください。")]
    public string? CurrentPassword { get; set; }

    [Display(Name = "新しいパスワード")]
    public string? NewPassword { get; set; }

    [Display(Name = "新しいパスワード（確認）")]
    public string? NewPasswordConfirmation { get; set; }
}

/// <summary>入場制限案内画面の表示情報です。</summary>
/// <param name="Message">DB運用で設定した案内文、または共通の既定メッセージです。</param>
public sealed record PrivateNoticeViewModel(string Message);
