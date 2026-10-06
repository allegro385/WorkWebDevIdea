using System.ComponentModel.DataAnnotations;

namespace SalesSupport.Portal.Web.Models;

/// <summary>ログイン画面の入力です。パスワードは再表示しません。</summary>
public sealed class LoginInput
{
    [Display(Name = "ログインID")]
    [Required(ErrorMessage = "ログインIDを入力してください。")]
    [StringLength(256, ErrorMessage = "ログインIDは256文字以内で入力してください。")]
    public string? LoginId { get; set; }

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

/// <summary>入場制限案内画面の表示情報です。</summary>
/// <param name="Message">DB運用で設定した案内文、または共通の既定メッセージです。</param>
public sealed record PrivateNoticeViewModel(string Message);
