using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.FileStorage;

/// <summary>入力ストリームを消費して容量と名前を検証します。所有権は呼出元に残ります。</summary>
public interface IUploadValidator
{
    /// <summary>内容を読み進め、容量超過時点で停止します。</summary>
    Task<ValidationResult> ValidateAsync(Stream stream, string originalName, UploadPolicySnapshot policy, CancellationToken ct = default);
}
/// <summary>保存処理と事前検証で同じファイル条件を使います。</summary>
public sealed class UploadValidator : IUploadValidator
{
    /// <summary>DB登録用の小文字拡張子を検証します。</summary>
    public static bool IsExtension(string? extension) => extension is { Length: >= 2 and <= 20 } && extension[0] == '.'
        && extension.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789") < 0;

    /// <summary>元のパスを除去し、最終拡張子とDB条件を照合します。</summary>
    public static string NormalizeName(string originalName, UploadPolicySnapshot policy)
    {
        if (policy.MaxFileSizeBytes <= 0 || policy.Extensions.Count == 0 || policy.Extensions.Any(x => !IsExtension(x)))
            throw new ArgumentException("アップロード条件が不正です。");
        var name = Path.GetFileName(originalName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("ファイル名が不正です。");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!IsExtension(extension) || !policy.Extensions.Contains(extension, StringComparer.Ordinal))
            throw new ArgumentException("許可されていない拡張子です。");
        return name;
    }

    /// <summary>名前のエラーを返し、読み取り失敗やキャンセルは呼出元へ伝えます。</summary>
    public async Task<ValidationResult> ValidateAsync(Stream stream, string originalName, UploadPolicySnapshot policy, CancellationToken ct = default)
    {
        try { NormalizeName(originalName, policy); }
        catch (ArgumentException) { return new([new("File", "INVALID_INPUT", "ファイル名またはアップロード条件が不正です。")]); }
        var buffer = new byte[81920];
        long totalBytes = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (read > policy.MaxFileSizeBytes - totalBytes) return new([new("File", "MAX_FILE_SIZE", "ファイルの容量が上限を超えています。")]);
            totalBytes += read;
        }
        return new([]);
    }
}
