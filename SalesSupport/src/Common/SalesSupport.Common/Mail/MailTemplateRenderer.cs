using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.Mail;

/// <summary>Commonが整形を担当するテンプレートの識別子です。</summary>
public static class MailTemplateKeys
{
    /// <summary>問い合わせ受付メールです。件名形式は画面・機能設計で確定しています。</summary>
    public const string InquiryReceipt = "INQUIRY_RECEIPT";
}

/// <summary>差込み前の件名・本文と、必須の差込み項目です。</summary>
public sealed record MailTemplateDefinition(string SubjectFormat, string BodyFormat, IReadOnlyList<string> RequiredFields);

/// <summary>整形済みの件名と本文です。</summary>
public sealed record MailContent(string Subject, string Body);

/// <summary>登録済みテンプレートです。未確定の文面は配置時に追加・差替えします。</summary>
public sealed class MailTemplateOptions
{
    /// <summary>テンプレート識別子ごとの定義です。</summary>
    public Dictionary<string, MailTemplateDefinition> Templates { get; } = new(StringComparer.Ordinal)
    {
        [MailTemplateKeys.InquiryReceipt] = new(
            "{システム名}問い合わせ【{カテゴリ}】【No.{問い合わせID}】",
            "{システム名}へのお問い合わせを受け付けました。\r\n\r\n問い合わせID：{問い合わせID}\r\nカテゴリ：{カテゴリ}\r\n対象：{対象}\r\n\r\n内容：\r\n{内容}\r\n",
            ["問い合わせID", "カテゴリ", "対象", "内容"])
    };
}

/// <summary>業務モデルから件名と本文を組み立てます。宛先選定は呼出元の責務です。</summary>
public interface IMailTemplateRenderer
{
    /// <summary>登録済みテンプレートへ差込み項目を適用します。</summary>
    MailContent Render(string templateKey, IReadOnlyDictionary<string, string> model);
}

/// <summary>差込み値の制御文字を除去し、件名へのヘッダー注入を防ぎます。</summary>
public sealed class MailTemplateRenderer(IOptions<MailTemplateOptions> templates, IOptions<CommonOptions> common) : IMailTemplateRenderer
{
    /// <summary>未登録のテンプレートと必須項目の欠落を構成エラーにします。</summary>
    public MailContent Render(string templateKey, IReadOnlyDictionary<string, string> model)
    {
        if (!templates.Value.Templates.TryGetValue(templateKey, out var template)) throw new ConfigurationException("Mail:Templates/" + templateKey);
        if (template.RequiredFields.Any(field => !model.ContainsKey(field))) throw new ArgumentException("差込み項目が不足しています。", nameof(model));
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["システム名"] = common.Value.ApplicationName };
        foreach (var (key, value) in model) values[key] = value ?? "";
        return new(Apply(template.SubjectFormat, values, multiline: false), Apply(template.BodyFormat, values, multiline: true));
    }

    /// <summary>未解決の差込み記号を残さず、値の制御文字を用途に応じて整形します。</summary>
    private static string Apply(string format, IReadOnlyDictionary<string, string> values, bool multiline)
    {
        var builder = new System.Text.StringBuilder(format.Length);
        for (var index = 0; index < format.Length;)
        {
            var open = format.IndexOf('{', index);
            if (open < 0) { builder.Append(format, index, format.Length - index); break; }
            var close = format.IndexOf('}', open + 1);
            if (close < 0) throw new ConfigurationException("Mail:Templates");
            builder.Append(format, index, open - index);
            var key = format[(open + 1)..close];
            if (!values.TryGetValue(key, out var value)) throw new ConfigurationException("Mail:Templates/" + key);
            builder.Append(Sanitize(value, multiline));
            index = close + 1;
        }
        return builder.ToString();
    }

    /// <summary>本文では改行だけを残し、件名では改行も空白へ置き換えます。</summary>
    private static string Sanitize(string value, bool multiline)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '\r' or '\n') builder.Append(multiline ? character : ' ');
            else if (character == '\t') builder.Append(multiline ? character : ' ');
            else if (!char.IsControl(character)) builder.Append(character);
        }
        return builder.ToString();
    }
}
