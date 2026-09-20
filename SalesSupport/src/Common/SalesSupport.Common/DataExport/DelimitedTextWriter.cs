using System.Globalization;
using System.Text;

namespace SalesSupport.Common.DataExport;

/// <summary>出力形式を表します。</summary>
public enum DelimitedFormat { Csv, Tsv }
/// <summary>出力列を順序付きで定義します。</summary>
public sealed record ExportDefinition(DelimitedFormat Format, IReadOnlyList<string> Columns);
/// <summary>文字列と型付き値を区別する1行分のデータです。</summary>
public sealed record ExportRow(IReadOnlyList<object?> Values);
/// <summary>所有者のストリームを閉じずに区切りテキストを出力します。</summary>
public interface IDelimitedTextWriter
{
    /// <summary>見出しから順次書き出します。</summary>
    Task WriteAsync(Stream destination, ExportDefinition definition, IAsyncEnumerable<ExportRow> rows, CancellationToken ct = default);
}
/// <summary>数式対策とUTF-8 BOM付きのCSV・TSV出力です。</summary>
public sealed class DelimitedTextWriter : IDelimitedTextWriter
{
    /// <summary>全件を保持せず書き出し、失敗時は呼出元へ例外を返します。</summary>
    public async Task WriteAsync(Stream destination, ExportDefinition definition, IAsyncEnumerable<ExportRow> rows, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(definition.Format) || definition.Columns.Count == 0) throw new ArgumentException("出力定義が不正です。");
        await using var writer = new StreamWriter(destination, new UTF8Encoding(true), 4096, leaveOpen: true) { NewLine = "\r\n" };
        var separator = definition.Format == DelimitedFormat.Csv ? "," : "\t";
        await writer.WriteLineAsync(string.Join(separator, definition.Columns.Select(x => Format(x, definition.Format))).AsMemory(), ct);
        await foreach (var row in rows.WithCancellation(ct))
        {
            if (row.Values.Count != definition.Columns.Count) throw new ArgumentException("出力列数が一致しません。");
            await writer.WriteLineAsync(string.Join(separator, row.Values.Select(x => Format(x, definition.Format))).AsMemory(), ct);
        }
        await writer.FlushAsync(ct);
    }

    /// <summary>許可した型を表記へ変換し、文字列だけに数式対策を適用します。</summary>
    private static string Format(object? value, DelimitedFormat format)
    {
        var text = value switch
        {
            null => "",
            string s => s,
            bool b => b ? "1" : "0",
            Guid id => id.ToString("D"),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            System.DateTime utc => new DateTimeOffset(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("未対応の出力型です。")
        };
        text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (value is string && text.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0])) text = "'" + text;
        return format == DelimitedFormat.Csv && (text.Contains(',') || text.Contains('"')) ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
