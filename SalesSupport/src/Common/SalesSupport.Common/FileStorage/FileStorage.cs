using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Logging;

namespace SalesSupport.Common.FileStorage;

/// <summary>保存領域の区分です。一時と永続でルートを分離します。</summary>
public enum FileArea { Temporary, Permanent }

/// <summary>削除要求の結果です。不在と失敗を区別します。</summary>
public enum FileDeleteResult { Deleted, NotFound, Failed }

/// <summary>保存領域からの相対位置だけで対象ファイルを指します。</summary>
public interface IStoredFileReference
{
    /// <summary>対象の保存領域です。</summary>
    FileArea Area { get; }
    /// <summary>保存領域を基準とした相対パスです。Web公開URLではありません。</summary>
    string RelativePath { get; }
}

/// <summary>永続保存済みファイルの情報です。相対パスはDBへ保持します。</summary>
public sealed record StoredFile(string RelativePath, string Extension, long SizeBytes, string OriginalName) : IStoredFileReference
{
    /// <summary>永続領域を示します。</summary>
    public FileArea Area => FileArea.Permanent;
}

/// <summary>一時保存の要求です。用途とツールから保存領域を決定します。</summary>
public sealed record TemporaryFileRequest(UploadPurpose Purpose, string? ToolId, string OriginalName);

/// <summary>永続保存の要求です。FileIdはDBが採番した値を渡します。</summary>
public sealed record PermanentFileRequest(UploadPurpose Purpose, string ToolId, long FileId, string OriginalName);

/// <summary>入力不正による保存拒否です。予期しない障害と区別します。</summary>
public sealed class UploadRejectedException : Exception
{
    /// <summary>画面表示用の項目エラーを保持する例外を作成します。</summary>
    public UploadRejectedException(FieldError error) : base(error.Message) => Error = error;

    /// <summary>入力値を含まない項目エラーです。</summary>
    public FieldError Error { get; }
}

/// <summary>保存領域の内側だけでファイルを読み書きします。認可は呼出元が行います。</summary>
public interface IFileStorage
{
    /// <summary>検証しながら一時領域へ保存し、清掃可能なハンドルを返します。</summary>
    Task<TemporaryFileHandle> SaveTemporaryAsync(TemporaryFileRequest request, Stream source, CancellationToken ct = default);
    /// <summary>検証しながら永続領域へ保存し、DBへ記録する相対パスを返します。</summary>
    Task<StoredFile> SavePermanentAsync(PermanentFileRequest request, Stream source, CancellationToken ct = default);
    /// <summary>読取り用Streamを返します。破棄は呼出元が行います。</summary>
    Task<Stream> OpenReadAsync(IStoredFileReference reference, CancellationToken ct = default);
    /// <summary>対象ファイルを削除し、不在・失敗を区別して返します。</summary>
    Task<FileDeleteResult> DeleteAsync(IStoredFileReference reference, CancellationToken ct = default);
}

/// <summary>業務処理の終了時に一時ファイルを確実に削除するためのハンドルです。</summary>
public sealed class TemporaryFileHandle : IStoredFileReference, IAsyncDisposable
{
    private readonly FileStorage storage;
    private int disposed;

    /// <summary>保存処理だけが生成できるハンドルを作成します。</summary>
    internal TemporaryFileHandle(FileStorage storage, Guid fileId, UploadPurpose purpose, string? toolId, string relativePath, string originalName, string extension, long sizeBytes)
    {
        this.storage = storage;
        FileId = fileId;
        Purpose = purpose;
        ToolId = toolId;
        RelativePath = relativePath;
        OriginalName = originalName;
        Extension = extension;
        SizeBytes = sizeBytes;
    }

    /// <summary>サーバーが発行した識別子です。物理ファイル名にも使用します。</summary>
    public Guid FileId { get; }
    /// <summary>この一時ファイルを所有する用途です。</summary>
    public UploadPurpose Purpose { get; }
    /// <summary>ツール別領域を使用する場合のツールIDです。</summary>
    public string? ToolId { get; }
    /// <summary>一時領域を基準とした相対パスです。</summary>
    public string RelativePath { get; }
    /// <summary>表示・添付名に使用する元のファイル名です。</summary>
    public string OriginalName { get; }
    /// <summary>小文字へ正規化した拡張子です。</summary>
    public string Extension { get; }
    /// <summary>保存時に実測したバイト数です。</summary>
    public long SizeBytes { get; }
    /// <summary>一時領域を示します。</summary>
    public FileArea Area => FileArea.Temporary;

    /// <summary>業務処理の成否にかかわらず一時ファイルを削除します。失敗は例外にしません。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await storage.CleanupAsync(this);
    }
}

/// <summary>ルート配下へ限定した保存・読取り・削除を提供します。</summary>
public sealed class FileStorage(IOptions<StorageOptions> options, IUploadPolicyProvider policies, ISystemErrorLogger logger) : IFileStorage
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>用途別・ツール別の一時領域へ保存します。</summary>
    public async Task<TemporaryFileHandle> SaveTemporaryAsync(TemporaryFileRequest request, Stream source, CancellationToken ct = default)
    {
        var policy = await policies.GetAsync(request.Purpose, request.ToolId, ct);
        var (name, extension) = Normalize(request.OriginalName, policy);
        var fileId = Guid.NewGuid();
        var relativePath = Path.Combine(PurposeCode(request.Purpose), Segment(request.ToolId), fileId.ToString("N") + extension);
        var size = await WriteAsync(FileArea.Temporary, relativePath, source, policy.MaxFileSizeBytes, ct);
        return new TemporaryFileHandle(this, fileId, request.Purpose, request.ToolId, relativePath, name, extension, size);
    }

    /// <summary>DBが採番したFileIdの配下へ、GUIDの物理名で保存します。</summary>
    public async Task<StoredFile> SavePermanentAsync(PermanentFileRequest request, Stream source, CancellationToken ct = default)
    {
        if (request.FileId <= 0) throw new ArgumentException("FileIdが不正です。", nameof(request));
        var policy = await policies.GetAsync(request.Purpose, request.ToolId, ct);
        var (name, extension) = Normalize(request.OriginalName, policy);
        var relativePath = Path.Combine("Site", Segment(request.ToolId), "Files", request.FileId.ToString(), Guid.NewGuid().ToString("N") + extension);
        var size = await WriteAsync(FileArea.Permanent, relativePath, source, policy.MaxFileSizeBytes, ct);
        return new StoredFile(relativePath, extension, size, name);
    }

    /// <summary>領域外・リンク経由の読取りを拒否してStreamを開きます。</summary>
    public Task<Stream> OpenReadAsync(IStoredFileReference reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = Resolve(reference);
        Stream stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.Asynchronous
        });
        return Task.FromResult(stream);
    }

    /// <summary>対象が存在しない場合と削除できない場合を区別します。領域外の指定は拒否します。</summary>
    public Task<FileDeleteResult> DeleteAsync(IStoredFileReference reference, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // 領域外・リンク経由の指定は削除失敗ではなく入力不正として呼出元へ返します。
        var path = Resolve(reference);
        try
        {
            if (!File.Exists(path)) return Task.FromResult(FileDeleteResult.NotFound);
            File.Delete(path);
            return Task.FromResult(FileDeleteResult.Deleted);
        }
        catch (Exception) { return Task.FromResult(FileDeleteResult.Failed); }
    }

    /// <summary>ハンドル破棄時の清掃です。失敗は一度だけ記録し、業務結果を変更しません。</summary>
    internal async Task CleanupAsync(TemporaryFileHandle handle)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.CleanupTimeoutSeconds));
            if (await DeleteAsync(handle, timeout.Token) != FileDeleteResult.Failed) return;
            // ログ側の独立した制限時間を使うため、清掃の待機上限は引き継ぎません。
            await logger.WriteAsync(new SystemErrorEvent(Guid.NewGuid(), "FILE_CLEANUP_FAILED"));
        }
        catch (Exception) { /* 清掃の失敗で業務処理を失敗させず、再帰的な記録も行いません。 */ }
    }

    /// <summary>上限超過の時点で書込みを止め、途中ファイルを削除します。</summary>
    private async Task<long> WriteAsync(FileArea area, string relativePath, Stream source, long maxFileSizeBytes, CancellationToken ct)
    {
        var path = Resolve(area, relativePath);
        var directory = Path.GetDirectoryName(path) ?? throw new ConfigurationException("Storage/Path");
        Directory.CreateDirectory(directory);
        EnsureNoLink(RootOf(area), path);
        var buffer = new byte[81920];
        long totalBytes = 0;
        var destination = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous
        });
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                if (read > maxFileSizeBytes - totalBytes) throw new UploadRejectedException(new("File", "MAX_FILE_SIZE", "ファイルの容量が上限を超えています。"));
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                totalBytes += read;
            }
            await destination.FlushAsync(ct);
        }
        catch (Exception)
        {
            await destination.DisposeAsync();
            try { File.Delete(path); } catch (Exception) { /* 途中ファイルの削除失敗は保守対象として残します。 */ }
            throw;
        }
        await destination.DisposeAsync();
        return totalBytes;
    }

    /// <summary>元の名前を検証し、表示用の名前とDB条件に一致する小文字拡張子を返します。</summary>
    private static (string Name, string Extension) Normalize(string originalName, UploadPolicySnapshot policy)
    {
        try
        {
            var name = UploadValidator.NormalizeName(originalName, policy);
            return (name, Path.GetExtension(name).ToLowerInvariant());
        }
        catch (ArgumentException) { throw new UploadRejectedException(new("File", "INVALID_INPUT", "ファイル名またはアップロード条件が不正です。")); }
    }

    /// <summary>ポリシー検索と同じ用途コードを領域名に使用します。</summary>
    private static string PurposeCode(UploadPurpose purpose) => purpose switch
    {
        UploadPurpose.InquiryAttachment => "INQUIRY_ATTACHMENT",
        UploadPurpose.UserImport => "USER_IMPORT",
        UploadPurpose.Reference => "REFERENCE",
        UploadPurpose.App => "APP",
        UploadPurpose.ToolInput => "TOOL_INPUT",
        _ => throw new ArgumentException("アップロード用途が不正です。", nameof(purpose))
    };

    /// <summary>区切り・相対移動を含まないディレクトリ名だけを許可します。</summary>
    private static string Segment(string? toolId)
    {
        if (toolId is null) return "Site";
        if (toolId.Length is 0 or > 20 || !toolId.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-'))
            throw new ArgumentException("ツールIDが保存領域名として不正です。", nameof(toolId));
        return toolId;
    }

    /// <summary>設定済みのルートを返し、未設定の領域利用を構成エラーにします。</summary>
    private string RootOf(FileArea area)
    {
        var root = area == FileArea.Temporary ? options.Value.TemporaryRoot : options.Value.PermanentRoot;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new ConfigurationException(area == FileArea.Temporary ? "Storage:TemporaryRoot" : "Storage:PermanentRoot");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    /// <summary>参照の領域と相対パスから絶対パスを解決します。</summary>
    private string Resolve(IStoredFileReference reference) => Resolve(reference.Area, reference.RelativePath);

    /// <summary>絶対パス・親移動・別ボリュームを拒否し、ルート配下だけを許可します。</summary>
    private string Resolve(FileArea area, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Any(char.IsControl) || relativePath.Contains(':')
            || Path.IsPathRooted(relativePath) || relativePath.Replace('\\', '/').Split('/').Any(x => x is "" or "." or ".."))
            throw new ArgumentException("保存先の相対パスが不正です。", nameof(relativePath));
        var root = RootOf(area);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)) throw new ArgumentException("保存領域の外を指しています。", nameof(relativePath));
        EnsureNoLink(root, full);
        return full;
    }

    /// <summary>ルートから対象までの既存要素にリンク・再解析ポイントがないことを確認します。</summary>
    private static void EnsureNoLink(string root, string fullPath)
    {
        for (var current = fullPath; current is not null && current.Length > root.Length; current = Path.GetDirectoryName(current))
        {
            FileSystemInfo info = File.Exists(current) ? new FileInfo(current) : new DirectoryInfo(current);
            if (!info.Exists) continue;
            if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("保存領域内のリンクは利用できません。", nameof(fullPath));
        }
    }
}
