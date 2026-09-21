using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesSupport.Common.MasterData;

namespace SalesSupport.Common.UI;

/// <summary>CodeMasterの名称と色で状態バッジを描画します。名称・色を画面側で重複定義しません。</summary>
[HtmlTargetElement("code-badge", TagStructure = TagStructure.WithoutEndTag)]
public sealed class CodeBadgeTagHelper(ICodeMasterReader codes) : TagHelper
{
    /// <summary>CodeMasterのコード区分です。</summary>
    [HtmlAttributeName("code-type")]
    public string CodeType { get; set; } = "";
    /// <summary>CodeMasterのコード値です。</summary>
    [HtmlAttributeName("code-value")]
    public string CodeValue { get; set; } = "";

    /// <summary>名称欠落は「不明」、色不正は標準表示とし、値はHTMLエンコードします。</summary>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var option = await codes.FindAsync(CodeType, CodeValue);
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "badge ss-badge");
        if (option?.ColorCode is { } color && Validation.CommonValidation.IsColor(color))
        {
            output.Attributes.SetAttribute("style", $"background-color:{color};color:{TextColor(color)}");
            output.Attributes.SetAttribute("class", "badge ss-badge ss-badge-colored");
        }
        output.Content.SetContent(string.IsNullOrWhiteSpace(option?.CodeName) ? "不明" : option!.CodeName);
    }

    /// <summary>背景の相対輝度から白または濃色の文字色を選びます。</summary>
    private static string TextColor(string color)
    {
        var luminance = 0.0;
        foreach (var (offset, weight) in new[] { (1, 0.2126), (3, 0.7152), (5, 0.0722) })
        {
            var channel = int.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            luminance += weight * (channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4));
        }
        // 黒と白のうちコントラスト比が高い方を使い、中間の明るさでも読めるようにします。
        return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? "#000000" : "#ffffff";
    }
}
