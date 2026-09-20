using System.Text;
using SalesSupport.Common.DataExport;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Validation;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>入力境界とテキスト出力の安全性を確認します。</summary>
public sealed class ValidationAndExportTests
{
    /// <summary>先頭ゼロ・上限・改行・全角数字を区別します。</summary>
    [Theory]
    [InlineData("0.0.0", true)]
    [InlineData("99.99.99", true)]
    [InlineData("01.2.3", false)]
    [InlineData("1.100.3", false)]
    [InlineData("1.2.3\n", false)]
    [InlineData("１.2.3", false)]
    [InlineData(null, false)]
    public void VersionChecksExactFormat(string? text, bool expected) => Assert.Equal(expected, CommonValidation.IsVersion(text));

    /// <summary>スキーム相対URLとエンコードされた危険な戻り先を拒否します。</summary>
    [Theory]
    [InlineData("/tools/001?a=1", true)]
    [InlineData("https://example.com", false)]
    [InlineData("//example.com", false)]
    [InlineData("/%2fexample.com", false)]
    [InlineData("/%5cexample.com", false)]
    [InlineData("/path%0d%0a", false)]
    public void ReturnUrlIsLocal(string value, bool expected) => Assert.Equal(expected, CommonValidation.IsLocalReturnUrl(value));

    /// <summary>サロゲートペアと末尾空白をUTF-16の桁数へ含めます。</summary>
    [Fact]
    public void TextCountsUtf16AndTrailingSpaces()
    {
        Assert.NotNull(CommonValidation.ValidateText("Body", "😀 ", 2, true));
        Assert.Null(CommonValidation.ValidateText("Body", "😀", 2, true));
        Assert.True(CommonValidation.CompareVersions("1.10.0", "1.9.99") > 0);
    }

    /// <summary>文字列数式だけを無効化し、BOMと改行・引用符を確認します。</summary>
    [Fact]
    public async Task CsvEscapesTextAndPreservesTypedNumbers()
    {
        using var output = new MemoryStream();
        await new DelimitedTextWriter().WriteAsync(output, new(DelimitedFormat.Csv, ["日本語", "数値", "本文"]), Rows(new ExportRow([" =1+1", -2, "a,\"b\"\r\nc\td"])));
        Assert.True(output.CanWrite);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, output.ToArray()[..3]);
        Assert.Equal("日本語,数値,本文\r\n' =1+1,-2,\"a,\"\"b\"\" c d\"\r\n", Encoding.UTF8.GetString(output.ToArray()[3..]));
    }

    /// <summary>ゼロ件でも見出しを出力します。</summary>
    [Fact]
    public async Task EmptyExportIncludesHeader()
    {
        using var output = new MemoryStream();
        await new DelimitedTextWriter().WriteAsync(output, new(DelimitedFormat.Tsv, ["A", "B"]), Rows());
        Assert.Equal("A\tB\r\n", Encoding.UTF8.GetString(output.ToArray()[3..]));
    }

    /// <summary>実際の読み取り容量で上限を判定し、入力を閉じません。</summary>
    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public async Task UploadChecksActualLength(int size, bool expected)
    {
        using var input = new MemoryStream(new byte[size]);
        var policy = new UploadPolicySnapshot(1, UploadPurpose.UserImport, null, 10, [".tsv"]);
        var result = await new UploadValidator().ValidateAsync(input, "C:\\fakepath\\users.TSV", policy);
        Assert.Equal(expected, result.IsValid);
        Assert.True(input.CanRead);
    }

    /// <summary>順次読み取り用のテスト行を提供します。</summary>
    private static async IAsyncEnumerable<ExportRow> Rows(params ExportRow[] rows)
    {
        await Task.CompletedTask;
        foreach (var row in rows) yield return row;
    }
}
