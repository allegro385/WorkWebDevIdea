using System.Globalization;

namespace SalesSupport.Common.Logging;

/// <summary>自由入力ではなく許可した値だけをログの補足情報に採用します。</summary>
public static class SafeLogDetails
{
    private static readonly HashSet<string> Stages = new(["検証", "宛先", "添付", "組立", "接続", "送信", "清掃"], StringComparer.Ordinal);
    private static readonly HashSet<string> Reasons = new(["INVALID_INPUT", "NO_RECIPIENTS", "SEND_FAILED", "SEND_UNKNOWN", "CANCELLED", "DEPENDENCY_UNAVAILABLE"], StringComparer.Ordinal);

    /// <summary>行番号・件数と固定の段階・理由以外を捨てます。切詰めによる部分保存はしません。</summary>
    public static string Format(IReadOnlyDictionary<string, string>? details)
    {
        if (details is null) return "";
        var parts = new List<string>();
        foreach (var key in new[] { "処理段階", "行番号", "件数", "理由" })
        {
            if (!details.TryGetValue(key, out var value)) continue;
            var allowed = key switch
            {
                "処理段階" => Stages.Contains(value),
                "理由" => Reasons.Contains(value),
                _ => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 0
            };
            if (allowed) parts.Add($"[{key}：{value}]");
        }
        return string.Concat(parts);
    }
}
