using SalesSupport.Common.Mail;

namespace SalesSupport.Portal.Web.Authentication;

/// <summary>設定リンクメールのテンプレート識別子と暫定の初期文面です。</summary>
/// <remarks>
/// 文面は設計書で確定していないため、配置時に`SalesSupport:MailTemplates`相当の構成で差し替えられる暫定値です。
/// 解消条件は運用連絡先とメール文面の確定であり、本文へトークン以外の秘密情報を追加しません。
/// </remarks>
public static class PasswordLinkMailTemplates
{
    /// <summary>初回パスワード設定リンクのテンプレート識別子です。</summary>
    public const string Initial = "PASSWORD_INITIAL";
    /// <summary>パスワード再設定リンクのテンプレート識別子です。</summary>
    public const string Reset = "PASSWORD_RESET";

    /// <summary>用途に対応するテンプレート識別子を返します。</summary>
    public static string Key(PasswordLinkKind kind) => kind == PasswordLinkKind.Initial ? Initial : Reset;

    /// <summary>未設定のテンプレートだけを暫定文面で補います。登録済みの文面は変更しません。</summary>
    public static void AddDefaults(MailTemplateOptions options)
    {
        options.Templates.TryAdd(Initial, new MailTemplateDefinition(
            "{システム名}　パスワード設定のご案内",
            "{表示名} 様\r\n\r\n{システム名}のアカウントが登録されました。\r\n次のURLからパスワードを設定してください。\r\n\r\n{設定URL}\r\n\r\n有効期限：{有効期限}\r\n期限を過ぎた場合は、ログイン画面の「パスワードを忘れた方」から改めて請求してください。\r\n\r\nこのメールに心当たりがない場合は破棄してください。\r\n",
            ["表示名", "設定URL", "有効期限"]));
        options.Templates.TryAdd(Reset, new MailTemplateDefinition(
            "{システム名}　パスワード再設定のご案内",
            "{表示名} 様\r\n\r\n{システム名}のパスワード再設定を受け付けました。\r\n次のURLから新しいパスワードを設定してください。\r\n\r\n{設定URL}\r\n\r\n有効期限：{有効期限}\r\n期限を過ぎた場合は、ログイン画面の「パスワードを忘れた方」から改めて請求してください。\r\n\r\nこのメールに心当たりがない場合は破棄してください。\r\n",
            ["表示名", "設定URL", "有効期限"]));
    }
}
