using Microsoft.AspNetCore.Identity;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Entities.Identity;

namespace SalesSupport.Portal.Web.Authentication;

/// <summary>初回設定・再設定・変更で共通に適用するパスワード条件です。</summary>
public interface IPasswordPolicy
{
    /// <summary>入力を加工せずに桁数・使用文字・禁止リストを検証します。</summary>
    ValidationResult Validate(string field, string? password);
}

/// <summary>起動時に読み込んだ禁止リストで、前後の空白も加工せずに判定します。</summary>
public sealed class PasswordPolicy : IPasswordPolicy
{
    /// <summary>画面・メールへ表示するパスワードの最小文字数です。</summary>
    public const int MinimumLength = 14;
    /// <summary>画面・メールへ表示するパスワードの最大文字数です。</summary>
    public const int MaximumLength = 64;

    private readonly HashSet<string> forbidden;

    /// <summary>読み込み済みの禁止リストを保持します。生成は<see cref="Load"/>から行います。</summary>
    private PasswordPolicy(HashSet<string> forbidden) => this.forbidden = forbidden;

    /// <summary>禁止リストを起動時に1度だけ読み込みます。欠落・読込不能は構成エラーにします。</summary>
    /// <param name="path">Web公開領域外に配置した1行1件のUTF-8テキストの、アプリの実行フォルダーからの相対パスです。</param>
    /// <param name="webRootPath">静的公開に使用するディレクトリです。配下のファイルは受け付けません。</param>
    public static PasswordPolicy Load(string? path, string? webRootPath)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Any(char.IsControl)) throw new ConfigurationException("SalesSupport:Password:ForbiddenListPath");
        var file = Path.GetFullPath(path, AppContext.BaseDirectory);
        if (IsInside(file, webRootPath)) throw new ConfigurationException("SalesSupport:Password:ForbiddenListPath");
        string[] lines;
        try { lines = File.ReadAllLines(file, System.Text.Encoding.UTF8); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ConfigurationException("SalesSupport:Password:ForbiddenListPath");
        }
        // 大文字小文字を区別し、リスト側の改行・空行だけを取り除きます。
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var entry = line.Trim('﻿', '\r', '\n');
            if (entry.Length > 0) entries.Add(entry);
        }
        return new PasswordPolicy(entries);
    }

    /// <summary>テスト・導入処理から明示した禁止リストで生成します。</summary>
    public static PasswordPolicy FromEntries(IEnumerable<string> entries) => new(new HashSet<string>(entries, StringComparer.Ordinal));

    /// <summary>満たしていない条件をすべて返し、入力値そのものは結果へ含めません。</summary>
    public ValidationResult Validate(string field, string? password)
    {
        List<FieldError> errors = [];
        if (string.IsNullOrEmpty(password)) return new([new(field, "REQUIRED", "入力してください。")]);
        if (password.Length is < MinimumLength or > MaximumLength)
            errors.Add(new(field, "LENGTH", $"{MinimumLength}文字以上{MaximumLength}文字以下で入力してください。"));
        if (!password.All(IsAllowedCharacter))
            errors.Add(new(field, "CHARACTER", "半角の英数字と記号だけで入力してください。空白と全角文字は使用できません。"));
        if (forbidden.Contains(password))
            errors.Add(new(field, "FORBIDDEN", "よく使われるパスワードのため使用できません。別のパスワードを入力してください。"));
        return new(errors);
    }

    /// <summary>ASCIIのU+0021〜U+007Eだけを許可します。空白・制御文字・全角文字は許可しません。</summary>
    private static bool IsAllowedCharacter(char value) => value is >= '!' and <= '~';

    /// <summary>指定パスが公開ディレクトリの配下にあるかを判定します。</summary>
    private static bool IsInside(string path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, comparison);
    }
}

/// <summary>Identity APIの経路でも同じパスワード条件を適用します。</summary>
public sealed class PortalPasswordValidator(IPasswordPolicy policy) : IPasswordValidator<ApplicationUser>
{
    /// <summary>画面検証を通らない経路からの登録・再設定を拒否します。</summary>
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var result = policy.Validate("Password", password);
        if (result.IsValid) return Task.FromResult(IdentityResult.Success);
        var errors = result.Errors.Select(error => new IdentityError { Code = error.Code, Description = error.Message }).ToArray();
        return Task.FromResult(IdentityResult.Failed(errors));
    }
}
