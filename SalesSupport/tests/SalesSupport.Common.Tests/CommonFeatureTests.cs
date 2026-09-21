using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.UI;
using SalesSupport.Common.Validation;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>保存領域の境界、メール文面の整形、リンク生成の安全性を確認します。</summary>
public sealed class CommonFeatureTests : IDisposable
{
    private readonly string temporaryRoot = Directory.CreateTempSubdirectory("ss-temp-").FullName;
    private readonly string permanentRoot = Directory.CreateTempSubdirectory("ss-perm-").FullName;
    private readonly StubErrorLogger logger = new();

    /// <summary>検証用の一時ディレクトリを削除します。</summary>
    public void Dispose()
    {
        foreach (var root in new[] { temporaryRoot, permanentRoot })
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>記号・空文字・桁超過をコード値として拒否します。</summary>
    [Theory]
    [InlineData("TOOL_001", true)]
    [InlineData("tool-001", true)]
    [InlineData("TOOL 001", false)]
    [InlineData("TOOL.001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CodeAllowsOnlyAsciiCodeCharacters(string? value, bool expected) => Assert.Equal(expected, CommonValidation.IsCode(value, 20));

    /// <summary>大文字・点なし・長すぎる拡張子を拒否します。</summary>
    [Theory]
    [InlineData(".tsv", true)]
    [InlineData(".TSV", false)]
    [InlineData("tsv", false)]
    [InlineData(".", false)]
    [InlineData(null, false)]
    public void ExtensionRequiresLowerCaseWithDot(string? value, bool expected) => Assert.Equal(expected, CommonValidation.IsExtension(value));

    /// <summary>一時ファイルは破棄で削除し、保存名に元のファイル名を使用しません。</summary>
    [Fact]
    public async Task TemporaryFileIsRemovedOnDispose()
    {
        var storage = CreateStorage(maxFileSizeBytes: 100, ".tsv");
        string path;
        await using (var handle = await storage.SaveTemporaryAsync(new(UploadPurpose.UserImport, null, "C:\\work\\users.TSV"), new MemoryStream(new byte[10])))
        {
            path = Path.Combine(temporaryRoot, handle.RelativePath);
            Assert.True(File.Exists(path));
            Assert.Equal(10, handle.SizeBytes);
            Assert.Equal(".tsv", handle.Extension);
            Assert.Equal("users.TSV", handle.OriginalName);
            Assert.DoesNotContain("users", handle.RelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.Combine("USER_IMPORT", "Site"), handle.RelativePath, StringComparison.Ordinal);
        }
        Assert.False(File.Exists(path));
    }

    /// <summary>上限超過は保存を拒否し、途中まで書き込んだファイルを残しません。</summary>
    [Fact]
    public async Task OversizeUploadIsRejectedAndPartialFileRemoved()
    {
        var storage = CreateStorage(maxFileSizeBytes: 8, ".tsv");
        var error = await Assert.ThrowsAsync<UploadRejectedException>(() =>
            storage.SaveTemporaryAsync(new(UploadPurpose.UserImport, null, "users.tsv"), new MemoryStream(new byte[9])));
        Assert.Equal("MAX_FILE_SIZE", error.Error.Code);
        Assert.Empty(Directory.GetFiles(temporaryRoot, "*", SearchOption.AllDirectories));
    }

    /// <summary>ポリシーにない拡張子は保存前に拒否します。</summary>
    [Fact]
    public async Task DisallowedExtensionIsRejectedBeforeWriting()
    {
        var storage = CreateStorage(maxFileSizeBytes: 100, ".tsv");
        var error = await Assert.ThrowsAsync<UploadRejectedException>(() =>
            storage.SaveTemporaryAsync(new(UploadPurpose.UserImport, null, "users.exe"), new MemoryStream(new byte[1])));
        Assert.Equal("INVALID_INPUT", error.Error.Code);
        Assert.Empty(Directory.GetFiles(temporaryRoot, "*", SearchOption.AllDirectories));
    }

    /// <summary>永続ファイルはDB採番前にGUID名で保存できます。</summary>
    [Fact]
    public async Task PermanentFileUsesFileIdDirectoryAndGeneratedName()
    {
        var storage = CreateStorage(maxFileSizeBytes: 100, ".pdf");
        var stored = await storage.SavePermanentAsync(new(UploadPurpose.Reference, "TOOL001", "手順書.PDF"), new MemoryStream(new byte[5]));
        Assert.Equal(Path.Combine("Site", "TOOL001", "Files"), Path.GetDirectoryName(stored.RelativePath));
        Assert.Equal(".pdf", stored.Extension);
        Assert.Equal("手順書.PDF", stored.OriginalName);
        Assert.Equal(5, stored.SizeBytes);
        Assert.True(File.Exists(Path.Combine(permanentRoot, stored.RelativePath)));
        Assert.DoesNotContain("手順書", stored.RelativePath, StringComparison.Ordinal);
    }

    /// <summary>領域外・絶対パス・親移動の参照を拒否します。</summary>
    [Theory]
    [InlineData("../outside.tsv")]
    [InlineData("a/../../outside.tsv")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public async Task ReferenceOutsideRootIsRejected(string relativePath)
    {
        var storage = CreateStorage(maxFileSizeBytes: 100, ".tsv");
        var reference = new StoredFile(relativePath, ".tsv", 1, "x.tsv");
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(reference));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(reference));
    }

    /// <summary>保存領域内のリンク経由で領域外へ出る参照を拒否します。</summary>
    [Fact]
    public async Task ReferenceThroughLinkIsRejected()
    {
        var link = Path.Combine(permanentRoot, "LINKED");
        // リンク作成に権限が必要な環境では検証を省略します。作成できた場合だけ拒否を確認します。
        try { Directory.CreateSymbolicLink(link, Path.GetTempPath()); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
        var storage = CreateStorage(maxFileSizeBytes: 100, ".tsv");
        var reference = new StoredFile(Path.Combine("LINKED", "x.tsv"), ".tsv", 1, "x.tsv");
        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(reference));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(reference));
        Directory.Delete(link);
    }

    /// <summary>非seekの入力でも保存中に上限を判定し、途中ファイルを残しません。</summary>
    [Fact]
    public async Task ForwardOnlyStreamIsValidatedWhileWriting()
    {
        var storage = CreateStorage(maxFileSizeBytes: 8, ".tsv");
        await Assert.ThrowsAsync<UploadRejectedException>(() =>
            storage.SaveTemporaryAsync(new(UploadPurpose.UserImport, null, "users.tsv"), new ForwardOnlyStream(new byte[9])));
        Assert.Empty(Directory.GetFiles(temporaryRoot, "*", SearchOption.AllDirectories));
    }

    /// <summary>存在しないファイルの削除は失敗と区別します。</summary>
    [Fact]
    public async Task DeleteReportsNotFoundForMissingFile()
    {
        var storage = CreateStorage(maxFileSizeBytes: 100, ".pdf");
        var result = await storage.DeleteAsync(new StoredFile(Path.Combine("Site", "TOOL001", "Files", "1", "missing.pdf"), ".pdf", 1, "missing.pdf"));
        Assert.Equal(FileDeleteResult.NotFound, result);
        Assert.Equal(0, logger.Count);
    }

    /// <summary>件名へ確定形式を適用し、改行によるヘッダー注入を防ぎます。</summary>
    [Fact]
    public void TemplateFillsSubjectAndBlocksHeaderInjection()
    {
        var content = CreateRenderer().Render(MailTemplateKeys.InquiryReceipt, new Dictionary<string, string>
        {
            ["問い合わせID"] = "20260921001", ["カテゴリ"] = "質問\r\n偽装", ["対象"] = "ポータルサイト", ["内容"] = "1行目\r\n2行目"
        });
        Assert.Equal("営業支援ポータルサイト問い合わせ【質問  偽装】【No.20260921001】", content.Subject);
        Assert.DoesNotContain('\n', content.Subject);
        Assert.Contains("1行目\r\n2行目", content.Body);
        Assert.Contains("問い合わせID：20260921001", content.Body);
    }

    /// <summary>未登録テンプレートと必須項目の欠落を区別して拒否します。</summary>
    [Fact]
    public void TemplateRejectsUnknownKeyAndMissingField()
    {
        var renderer = CreateRenderer();
        Assert.Throws<ConfigurationException>(() => renderer.Render("UNKNOWN", new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => renderer.Render(MailTemplateKeys.InquiryReceipt, new Dictionary<string, string> { ["カテゴリ"] = "質問" }));
    }

    /// <summary>配置先サブパスを前置し、任意URLの生成を拒否します。</summary>
    [Fact]
    public void LinksApplyPathBaseAndRejectAbsolutePaths()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Request.PathBase = "/tool001";
        var links = new SalesSupportLinks(Options.Create(new CommonOptions { PortalBaseUrl = "https://portal.example.com/" }), accessor);
        Assert.Equal("/tool001/account/logout", links.Local("account/logout"));
        Assert.Equal("https://portal.example.com/tools", links.Portal("tools"));
        Assert.Equal("https://portal.example.com/", links.Portal());
        Assert.Throws<ArgumentException>(() => links.Local("/absolute"));
        Assert.Throws<ArgumentException>(() => links.Local("https://example.com"));
        Assert.Throws<ArgumentException>(() => links.Local("../escape"));
    }

    /// <summary>一時・永続の両領域を設定した保存処理を作成します。</summary>
    private FileStorage.FileStorage CreateStorage(long maxFileSizeBytes, string extension)
    {
        var options = Options.Create(new StorageOptions { TemporaryRoot = temporaryRoot, PermanentRoot = permanentRoot, CleanupTimeoutSeconds = 5 });
        return new FileStorage.FileStorage(options, new StubPolicyProvider(maxFileSizeBytes, extension), logger);
    }

    /// <summary>アプリ名だけを設定したテンプレート整形処理を作成します。</summary>
    private static MailTemplateRenderer CreateRenderer() =>
        new(Options.Create(new MailTemplateOptions()), Options.Create(new CommonOptions { ApplicationName = "営業支援ポータルサイト" }));

    /// <summary>DBを参照せず固定のアップロード条件を返します。</summary>
    private sealed class StubPolicyProvider(long maxFileSizeBytes, string extension) : IUploadPolicyProvider
    {
        /// <summary>用途とツールにかかわらず同じ条件を返します。</summary>
        public Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default) =>
            Task.FromResult(new UploadPolicySnapshot(1, purpose, toolId, maxFileSizeBytes, [extension]));
    }

    /// <summary>巻き戻しも長さ取得もできない入力の代替です。</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private int position;

        /// <summary>読取りだけを許可します。</summary>
        public override bool CanRead => true;
        /// <summary>シークできないことを示します。</summary>
        public override bool CanSeek => false;
        /// <summary>書込みできないことを示します。</summary>
        public override bool CanWrite => false;
        /// <summary>事前の容量判定を防ぐため長さを公開しません。</summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>位置の取得・設定を許可しません。</summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>何も保持していないため実処理はありません。</summary>
        public override void Flush() { }

        /// <summary>1回の呼出しで少量ずつ返し、分割読取りを再現します。</summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(Math.Min(count, 4), data.Length - position);
            Array.Copy(data, position, buffer, offset, read);
            position += read;
            return read;
        }

        /// <summary>シークを拒否します。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <summary>長さ変更を拒否します。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <summary>書込みを拒否します。</summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>記録回数だけを数える障害ログの代替です。</summary>
    private sealed class StubErrorLogger : ISystemErrorLogger
    {
        /// <summary>記録要求の回数です。</summary>
        public int Count { get; private set; }

        /// <summary>常に成功を返し、回数だけを数えます。</summary>
        public Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(LogWriteResult.Written);
        }
    }
}
