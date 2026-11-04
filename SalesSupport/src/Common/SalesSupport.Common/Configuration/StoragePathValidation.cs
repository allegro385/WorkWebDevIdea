using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace SalesSupport.Common.Configuration;

/// <summary>保存先とWeb公開・配置領域の重複、およびリンク経由の領域変更を拒否します。</summary>
public sealed class StoragePathValidation(IWebHostEnvironment environment) : IValidateOptions<StorageOptions>
{
    /// <summary>フォルダーの作成と権限の付与を案内する文です。</summary>
    private const string Remedy = "共通設定ファイルの値を確認し、scripts/Initialize-SalesSupportFolders.ps1でフォルダーの作成と権限の付与を行ってください。";

    /// <summary>設定済みの領域だけを検証し、設定キー・理由・対処を返します。物理パスは検証メッセージへ出しません。</summary>
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var failures = new[] { ("Storage:TemporaryRoot", options.TemporaryRoot), ("Storage:PermanentRoot", options.PermanentRoot) }
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2)).Select(x => Check(x.Item1, x.Item2!)).OfType<string>().ToList();
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>1つの保存領域を検証します。</summary>
    /// <param name="key">検証メッセージへ示す設定キーです。</param>
    /// <param name="root">解決済みの保存領域です。</param>
    /// <returns>問題の理由です。問題がなければNULLです。</returns>
    private string? Check(string key, string root)
    {
        try
        {
            if (!Path.IsPathFullyQualified(root)) return $"{key}を絶対パスへ解決できません。共通設定ファイルの値を確認してください。";
            // Directory.Existsは権限がない場合もfalseを返すため、実際に開いて理由を区別します。
            _ = Directory.EnumerateFileSystemEntries(root).Any();
            if (Path.GetFullPath(root) == Path.GetPathRoot(Path.GetFullPath(root))) return $"{key}にボリュームのルートは指定できません。";
            for (var directory = new DirectoryInfo(Path.GetFullPath(root)); directory is not null; directory = directory.Parent)
                if (directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return $"{key}にリンクは使用できません。";
            foreach (var protectedRoot in new[] { environment.ContentRootPath, environment.WebRootPath, AppContext.BaseDirectory })
                if (!string.IsNullOrWhiteSpace(protectedRoot) && Overlaps(root, protectedRoot))
                    return $"{key}はWeb公開・配置領域の外へ分離してください。";
            return null;
        }
        catch (DirectoryNotFoundException) { return $"{key}のフォルダーがありません。{Remedy}"; }
        catch (UnauthorizedAccessException) { return $"{key}のフォルダーへのアクセス権がありません。{Remedy}"; }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return $"{key}のフォルダーを確認できません。共通設定ファイルの値と配置を確認してください。";
        }
    }

    /// <summary>区切りを含めて両方向の親子関係を判定します。</summary>
    private static bool Overlaps(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)) + Path.DirectorySeparatorChar;
        var right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)) + Path.DirectorySeparatorChar;
        return left.StartsWith(right, comparison) || right.StartsWith(left, comparison);
    }
}
