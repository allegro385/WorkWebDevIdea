using SalesSupport.Common.Configuration;
using SalesSupport.Portal.Web.Authentication;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>パスワード条件が桁数・使用文字・禁止リストを設計どおりに判定することを確認します。</summary>
public sealed class PasswordPolicyTests
{
    private const string Valid = "Portal-Test#2026";

    /// <summary>条件を満たすパスワードを受け入れることを確認します。</summary>
    [Fact]
    public void ValidateAcceptsAllowedPassword()
    {
        var policy = PasswordPolicy.FromEntries([]);
        Assert.True(policy.Validate("Password", Valid).IsValid);
    }

    /// <summary>14文字未満と64文字超を桁数エラーとして返すことを確認します。</summary>
    [Theory]
    [InlineData("Short#2026a12")]
    [InlineData("A1#aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ValidateRejectsLengthOutsideRange(string password)
    {
        var policy = PasswordPolicy.FromEntries([]);
        var result = policy.Validate("Password", password);
        Assert.Contains(result.Errors, error => error.Code == "LENGTH");
    }

    /// <summary>境界の14文字と64文字を受け入れることを確認します。</summary>
    [Theory]
    [InlineData(14)]
    [InlineData(64)]
    public void ValidateAcceptsBoundaryLength(int length)
    {
        var policy = PasswordPolicy.FromEntries([]);
        Assert.True(policy.Validate("Password", new string('a', length)).IsValid);
    }

    /// <summary>空白・全角文字・制御文字を使用文字エラーとして返すことを確認します。</summary>
    [Theory]
    [InlineData("Portal Test#2026")]
    [InlineData("Portal-Test#2026 ")]
    [InlineData("Ｐortal-Test#2026")]
    [InlineData("Portal-Test#20\t26")]
    public void ValidateRejectsDisallowedCharacters(string password)
    {
        var policy = PasswordPolicy.FromEntries([]);
        var result = policy.Validate("Password", password);
        Assert.Contains(result.Errors, error => error.Code == "CHARACTER");
    }

    /// <summary>前後の空白を除去せず、そのまま判定することを確認します。</summary>
    [Fact]
    public void ValidateDoesNotTrimInput()
    {
        var policy = PasswordPolicy.FromEntries([Valid]);
        // 末尾に空白を付けた値は、禁止リストへ一致させず使用文字エラーとして扱います。
        var result = policy.Validate("Password", Valid + " ");
        Assert.Contains(result.Errors, error => error.Code == "CHARACTER");
        Assert.DoesNotContain(result.Errors, error => error.Code == "FORBIDDEN");
    }

    /// <summary>禁止リストを大文字小文字を区別して完全一致で照合することを確認します。</summary>
    [Fact]
    public void ValidateComparesForbiddenListCaseSensitively()
    {
        var policy = PasswordPolicy.FromEntries([Valid]);
        Assert.Contains(policy.Validate("Password", Valid).Errors, error => error.Code == "FORBIDDEN");
        Assert.True(policy.Validate("Password", Valid.ToUpperInvariant()).IsValid);
    }

    /// <summary>未入力を必須エラーとして返すことを確認します。</summary>
    [Fact]
    public void ValidateRejectsEmptyInput()
    {
        var policy = PasswordPolicy.FromEntries([]);
        var result = policy.Validate("Password", "");
        Assert.Equal("REQUIRED", Assert.Single(result.Errors).Code);
    }

    /// <summary>禁止リストのファイルを読み込み、空行とBOMを除外することを確認します。</summary>
    [Fact]
    public void LoadReadsForbiddenListFromFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forbidden-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "﻿" + Valid + "\n\n" + Valid.ToLowerInvariant() + "\n", System.Text.Encoding.UTF8);
        try
        {
            var policy = PasswordPolicy.Load(path, Path.Combine(Path.GetTempPath(), "wwwroot-none"));
            Assert.Contains(policy.Validate("Password", Valid).Errors, error => error.Code == "FORBIDDEN");
            Assert.Contains(policy.Validate("Password", Valid.ToLowerInvariant()).Errors, error => error.Code == "FORBIDDEN");
        }
        finally { File.Delete(path); }
    }

    /// <summary>未設定・相対パス・欠落したファイルを構成エラーにすることを確認します。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("forbidden.txt")]
    public void LoadRejectsUnusablePath(string? path)
    {
        Assert.Throws<ConfigurationException>(() => PasswordPolicy.Load(path, null));
    }

    /// <summary>実在しない絶対パスを構成エラーにすることを確認します。</summary>
    [Fact]
    public void LoadRejectsMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt");
        Assert.Throws<ConfigurationException>(() => PasswordPolicy.Load(path, null));
    }

    /// <summary>Web公開領域の配下に置いた禁止リストを拒否することを確認します。</summary>
    [Fact]
    public void LoadRejectsFileInsideWebRoot()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"wwwroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        var path = Path.Combine(webRoot, "forbidden.txt");
        File.WriteAllText(path, Valid, System.Text.Encoding.UTF8);
        try { Assert.Throws<ConfigurationException>(() => PasswordPolicy.Load(path, webRoot)); }
        finally { Directory.Delete(webRoot, recursive: true); }
    }
}
