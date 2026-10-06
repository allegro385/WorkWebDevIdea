using Microsoft.AspNetCore.Identity;
using SalesSupport.Portal.Web.Bootstrap;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>TSVの列順・全行検証・秘密入力の保持と、Identity正規化による重複検出を確認します。</summary>
public sealed class LocalTestUserTsvReaderTests
{
    private static readonly string TestPassword = Guid.NewGuid().ToString("N");

    /// <summary>任意ヘッダーとBOMの有無によらず4列を指定順に読み、元の行番号を保持します。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("ログインID\tメールアドレス\tパスワード\t表示名\r\n")]
    [InlineData("\uFEFFLoginId\tEmail\tPassword\tDisplayName\r\n")]
    public async Task OptionalHeaderAndBomPreserveColumnOrder(string header)
    {
        var result = await ReadAsync(header + Row() + "\r\n\r\n" + Row("staff002", "second@example.invalid"));
        Assert.Null(result.ErrorMessage);
        Assert.Equal(2, result.Rows.Count);
        var user = result.Rows[0].User;
        Assert.Equal("staff001", user.LoginId);
        Assert.Equal("staff@example.invalid", user.Email);
        Assert.Equal(TestPassword, user.Password);
        Assert.Equal("試験利用者", user.DisplayName);
        Assert.Equal("C", user.RoleCode);
        Assert.Equal(header.Length == 0 ? 3 : 4, result.Rows[1].LineNumber);
    }

    /// <summary>認証値の前後空白は変更せず、メール・表示名は対話コマンドと同じ整形にします。</summary>
    [Fact]
    public async Task LoginIdAndPasswordAreNotTrimmed()
    {
        var password = $" {TestPassword} ";
        var result = await ReadAsync(Row(" staff001 ", " staff@example.invalid ", password, " 試験利用者 "));
        var user = Assert.Single(result.Rows).User;
        Assert.Equal(" staff001 ", user.LoginId);
        Assert.Equal(password, user.Password);
        Assert.Equal("staff@example.invalid", user.Email);
        Assert.Equal("試験利用者", user.DisplayName);
    }

    /// <summary>途中に列不足がある場合も、事前検証が成功した先行行を登録対象へ返しません。</summary>
    [Theory]
    [InlineData("broken\tmail@example.invalid\t")]
    [InlineData("broken\tmail@example.invalid\t\tname\textra")]
    public async Task InvalidColumnCountRejectsWholeFile(string invalidRow)
    {
        var result = await ReadAsync(Row() + "\n" + invalidRow);
        Assert.Empty(result.Rows);
        Assert.StartsWith("2行目：", result.ErrorMessage);
        Assert.DoesNotContain(TestPassword, result.ErrorMessage);
        Assert.DoesNotContain("broken", result.ErrorMessage);
    }

    /// <summary>入力形式・長さ・制御文字の不備をパスワードや元の行を表示せずに拒否します。</summary>
    [Fact]
    public async Task InvalidFieldsRejectWholeFileWithoutValues()
    {
        string[] invalidRows = [Row(loginId: " "), Row(loginId: new string('x', 257)), Row(email: "invalid-mail"),
            Row(password: ""), Row(password: "short"), Row(displayName: " "), Row(displayName: new string('名', 101)),
            Row(password: TestPassword + "\0"), Row(loginId: "staff\0")];
        foreach (var invalidRow in invalidRows)
        {
            var result = await ReadAsync(Row() + "\n" + invalidRow);
            Assert.Empty(result.Rows);
            Assert.StartsWith("2行目：", result.ErrorMessage);
            Assert.DoesNotContain(TestPassword, result.ErrorMessage);
        }
    }

    /// <summary>ログインIDとメールの大小文字違いも、Identityと同じ正規化で重複と判定します。</summary>
    [Theory]
    [InlineData("STAFF001", "second@example.invalid")]
    [InlineData("staff002", "STAFF@EXAMPLE.INVALID")]
    public async Task IdentityNormalizedDuplicatesRejectWholeFile(string loginId, string email)
    {
        var result = await ReadAsync(Row() + "\n" + Row(loginId, email));
        Assert.Empty(result.Rows);
        Assert.Contains("重複", result.ErrorMessage);
    }

    /// <summary>空ファイルとヘッダーだけのファイルを成功扱いにしません。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("ログインID\tメールアドレス\tパスワード\t表示名\n")]
    public async Task EmptyInputIsRejected(string input)
    {
        var result = await ReadAsync(input);
        Assert.Empty(result.Rows);
        Assert.Contains("登録データがありません", result.ErrorMessage);
    }

    /// <summary>過剰な件数・入力文字数でも一部だけの登録対象を返しません。</summary>
    [Fact]
    public async Task InputLimitsRejectWholeFile()
    {
        var oversizedRows = string.Join('\n', Enumerable.Range(0, LocalTestUserTsvReader.MaxRows + 1)
            .Select(index => Row($"staff{index}", $"staff{index}@example.invalid")));
        Assert.Empty((await ReadAsync(oversizedRows)).Rows);
        var oversizedInput = Row() + "\n" + new string('x', LocalTestUserTsvReader.MaxCharacters);
        Assert.Empty((await ReadAsync(oversizedInput)).Rows);
    }

    /// <summary>実際のIdentity標準正規化を使ってメモリー上のTSVを読みます。</summary>
    private static Task<LocalTestUserTsvResult> ReadAsync(string text) =>
        new LocalTestUserTsvReader(new UpperInvariantLookupNormalizer()).ReadAsync(new StringReader(text), "C");

    /// <summary>列を間違えたマッピングを検出できるよう、各項目を独立した値で生成します。</summary>
    private static string Row(string loginId = "staff001", string email = "staff@example.invalid", string? password = null,
        string displayName = "試験利用者") => $"{loginId}\t{email}\t{password ?? TestPassword}\t{displayName}";
}
