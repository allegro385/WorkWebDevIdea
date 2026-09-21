using SalesSupport.Common.Mail;

namespace SalesSupport.Portal.Web.Mail;

/// <summary>お知らせメールのテンプレート識別子と暫定の初期文面です。</summary>
/// <remarks>
/// 文面は設計書で確定していないため、配置時に同じ識別子で差し替えられる暫定値です。
/// 本文には公開済みのお知らせだけを差し込み、宛先や秘密情報を記載しません。
/// </remarks>
public static class NoticeMailTemplates
{
    /// <summary>システムのお知らせを送信するテンプレート識別子です。</summary>
    public const string System = "NOTICE_SYSTEM";

    /// <summary>ツールのお知らせを送信するテンプレート識別子です。</summary>
    public const string Tool = "NOTICE_TOOL";

    /// <summary>お知らせ種別に対応するテンプレート識別子を返します。</summary>
    /// <param name="noticeType">SYSTEMまたはTOOLです。</param>
    public static string Key(string noticeType) => noticeType == "TOOL" ? Tool : System;

    /// <summary>未設定のテンプレートだけを暫定文面で補います。登録済みの文面は変更しません。</summary>
    public static void AddDefaults(MailTemplateOptions options)
    {
        options.Templates.TryAdd(System, new MailTemplateDefinition(
            "{システム名}　お知らせ",
            "{システム名}からのお知らせです。\r\n\r\n{お知らせ}\r\n配信設定は、ポータルの個人設定から変更できます。\r\n",
            ["お知らせ"]));
        options.Templates.TryAdd(Tool, new MailTemplateDefinition(
            "{システム名}　{ツール名}のお知らせ",
            "{システム名}に登録されている「{ツール名}」のお知らせです。\r\n\r\n{お知らせ}\r\n配信設定は、ポータルの個人設定から変更できます。\r\n",
            ["ツール名", "お知らせ"]));
    }
}
