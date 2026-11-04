using Microsoft.AspNetCore.Identity;
using SalesSupport.Portal.Web.Bootstrap;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>ユーザー登録TSVの形式検証が全行を対象にし、入力値を表示しないことを確認します。</summary>
public sealed class UserImportTsvReaderTests
{
    private const string Header = "LoginId\tEmail\tPassword\tDisplayName\tRoleCode\tIsActive";
    private const string Secret = "secret-value-1";

    /// <summary>6列を読み取り、メール・表示名の前後の空白を除いて、ログインIDとパスワードは元の値を保ちます。</summary>
    [Fact]
    public async Task ReadsRowsAndKeepsLoginIdAndPassword()
    {
        var result = await ReadAsync($"{Header}\n staff1 \t a@example.invalid \t {Secret} \t 利用者 \tA\t0");

        Assert.Empty(result.Errors);
        var row = Assert.Single(result.Rows);
        Assert.Equal(new UserImportRow(2, " staff1 ", "a@example.invalid", $" {Secret} ", "利用者", "A", false), row);
    }

    /// <summary>完全な空行は無視し、行番号はファイルの行を指します。</summary>
    [Fact]
    public async Task IgnoresBlankLinesAndKeepsLineNumbers()
    {
        var result = await ReadAsync($"{Header}\n\n{Row(1)}\n\n{Row(2)}\n");

        Assert.Equal([3, 5], result.Rows.Select(x => x.LineNumber));
    }

    /// <summary>見出しがないファイルは列の取り違えを防ぐため拒否します。</summary>
    [Fact]
    public async Task RequiresHeader()
    {
        var result = await ReadAsync(Row(1));

        Assert.Empty(result.Rows);
        Assert.Contains("見出し", Assert.Single(result.Errors));
    }

    /// <summary>不備はすべての行について行番号と理由を返し、登録対象を返しません。入力値は表示しません。</summary>
    [Fact]
    public async Task ReportsEveryInvalidRowWithoutValues()
    {
        var input = string.Join('\n', Header,
            Row(1),
            $"\tb@example.invalid\t{Secret}\t名前\tA\t1",
            $"staff3\tnot-mail\t{Secret}\t名前\tA\t1",
            $"staff4\td@example.invalid\t{Secret}\t\tA\t1",
            $"staff5\te@example.invalid\t{Secret}\t名前\t\t1",
            $"staff6\tf@example.invalid\t{Secret}\t名前\tA\tyes",
            "staff7\tmissing-columns");

        var result = await ReadAsync(input);

        Assert.Empty(result.Rows);
        Assert.Equal(6, result.Errors.Count);
        Assert.StartsWith("3行目", result.Errors[0]);
        Assert.StartsWith("8行目", result.Errors[^1]);
        Assert.DoesNotContain(result.Errors, x => x.Contains(Secret, StringComparison.Ordinal) || x.Contains("not-mail", StringComparison.Ordinal));
    }

    /// <summary>ログインIDとメールのファイル内重複はIdentityの正規化後の値で検出します。</summary>
    [Fact]
    public async Task DetectsDuplicatesAfterNormalization()
    {
        var input = string.Join('\n', Header, Row(1), $"STAFF1\tother@example.invalid\t{Secret}\t名前\tA\t1", $"staff3\tSTAFF1@EXAMPLE.INVALID\t{Secret}\t名前\tA\t1");

        var result = await ReadAsync(input);

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("ログインID", result.Errors[0]);
        Assert.Contains("メールアドレス", result.Errors[1]);
    }

    /// <summary>制御文字を含む値を拒否します。</summary>
    [Fact]
    public async Task RejectsControlCharacters()
    {
        var result = await ReadAsync($"{Header}\nstaff\u0001\ta@example.invalid\t{Secret}\t名前\tA\t1");

        Assert.Contains("制御文字", Assert.Single(result.Errors));
    }

    /// <summary>登録件数の上限を超えるファイルは拒否します。</summary>
    [Fact]
    public async Task RejectsTooManyRows()
    {
        var rows = Enumerable.Range(1, UserImportTsvReader.MaxRows + 1).Select(Row);

        var result = await ReadAsync(Header + "\n" + string.Join('\n', rows));

        Assert.Empty(result.Rows);
        Assert.Contains("件以内", Assert.Single(result.Errors));
    }

    /// <summary>見出しだけのファイルは登録データなしとして拒否します。</summary>
    [Fact]
    public async Task RejectsFileWithoutRows()
    {
        Assert.Contains("登録データ", Assert.Single((await ReadAsync(Header)).Errors));
    }

    /// <summary>文字列のTSVを検証します。</summary>
    /// <param name="input">TSVの全文です。</param>
    /// <returns>検証結果です。</returns>
    private static Task<UserImportTsvResult> ReadAsync(string input) =>
        new UserImportTsvReader(new UpperInvariantLookupNormalizer()).ReadAsync(new StringReader(input));

    /// <summary>有効な1行を作成します。</summary>
    /// <param name="index">ログインIDとメールを区別する番号です。</param>
    /// <returns>TSVの1行です。</returns>
    private static string Row(int index) => $"staff{index}\tstaff{index}@example.invalid\t{Secret}\t利用者{index}\tA\t1";
}
