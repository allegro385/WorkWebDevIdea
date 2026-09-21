using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace SalesSupport.Common.Configuration;

/// <summary>保存先とWeb公開・配置領域の重複、およびリンク経由の領域変更を拒否します。</summary>
public sealed class StoragePathValidation(IWebHostEnvironment environment) : IValidateOptions<StorageOptions>
{
    /// <summary>設定済みの領域だけを検証し、物理パスを検証メッセージへ出しません。</summary>
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        foreach (var root in new[] { options.TemporaryRoot, options.PermanentRoot })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) return ValidateOptionsResult.Fail("保存領域が不正です。");
                if (Path.GetFullPath(root) == Path.GetPathRoot(Path.GetFullPath(root))) return ValidateOptionsResult.Fail("ボリュームのルートは保存領域に指定できません。");
                for (var directory = new DirectoryInfo(Path.GetFullPath(root)); directory is not null; directory = directory.Parent)
                    if (directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        return ValidateOptionsResult.Fail("保存領域にリンクは使用できません。");
                foreach (var protectedRoot in new[] { environment.ContentRootPath, environment.WebRootPath, AppContext.BaseDirectory })
                    if (!string.IsNullOrWhiteSpace(protectedRoot) && Overlaps(root, protectedRoot))
                        return ValidateOptionsResult.Fail("保存領域はWeb公開・配置領域の外へ分離してください。");
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return ValidateOptionsResult.Fail("保存領域を確認できません。");
            }
        }
        return ValidateOptionsResult.Success;
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
