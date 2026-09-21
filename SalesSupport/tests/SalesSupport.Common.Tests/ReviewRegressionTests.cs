using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.HttpClients;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>PRレビューで検出した安全性と仕様境界の回帰を確認します。</summary>
public sealed class ReviewRegressionTests
{
    /// <summary>保存ルート自身がジャンクション・リンクの場合も領域外へ書き込みません。</summary>
    [Fact]
    public async Task StorageRejectsLinkedRoot()
    {
        var root = Directory.CreateTempSubdirectory("ss-review-link-").FullName;
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("-NoProfile");
                start.ArgumentList.Add("-NonInteractive");
                start.ArgumentList.Add("-Command");
                start.ArgumentList.Add($"New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{target.Replace("'", "''")}' -ErrorAction Stop | Out-Null");
                using var process = System.Diagnostics.Process.Start(start)!;
                await process.WaitForExitAsync();
                Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(link, target);
            var storage = new FileStorage.FileStorage(Options.Create(new StorageOptions { PermanentRoot = link }), new RejectPolicyProvider(), new StubLogger());
            var reference = new StoredFile("test.pdf", ".pdf", 0, "test.pdf");
            await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(reference));
            await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(reference));
        }
        finally
        {
            // 先にリンクだけを外し、このテストが作った空ディレクトリだけを削除します。
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(target);
            Directory.Delete(root);
        }
    }

    /// <summary>HTTP応答ヘッダー到着後も本文全体にタイムアウトを適用します。</summary>
    [Fact]
    public async Task HttpTimeoutIncludesResponseBody()
    {
        using var client = new HttpClient(new StubHttpHandler()) { BaseAddress = new Uri("https://example.test/api/"), Timeout = TimeSpan.FromMilliseconds(100) };
        var result = await new JsonHttpClient(new StubClientFactory(client)).GetAsync<object>("test", "slow");
        Assert.Equal(HttpCallStatus.Timeout, result.Status);
    }

    /// <summary>外部URL・逆斜線・エンコードされた親移動を送信前に拒否します。</summary>
    [Theory]
    [InlineData("https://other.test/")]
    [InlineData("\\\\other.test/path")]
    [InlineData("%2e%2e/secret")]
    [InlineData("%252e%252e/secret")]
    [InlineData("%2f%2fother.test/path")]
    public async Task HttpRejectsUnsafeRelativePath(string path)
    {
        using var client = new HttpClient(new StubHttpHandler()) { BaseAddress = new Uri("https://example.test/api/") };
        await Assert.ThrowsAsync<ArgumentException>(() => new JsonHttpClient(new StubClientFactory(client)).GetAsync<object>("test", path));
    }

    /// <summary>開発環境でも宛先0件は置換先へ送信しません。</summary>
    [Fact]
    public async Task DevelopmentMailWithNoRecipientsDoesNotSend()
    {
        var result = await CreateMailSender(2).SendAsync(new([], [], [], "件名", "本文"));
        Assert.Equal("NO_RECIPIENTS", result.FailureReason);
        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
    }

    /// <summary>開発宛先への置換前に元の宛先数を検証します。</summary>
    [Fact]
    public async Task DevelopmentMailChecksOriginalRecipientLimit()
    {
        var result = await CreateMailSender(1).SendAsync(new(["a@example.test", "b@example.test"], [], [], "件名", "本文"));
        Assert.Equal("INVALID_INPUT", result.FailureReason);
    }

    /// <summary>開発環境でも不正な実宛先を見逃しません。</summary>
    [Fact]
    public async Task DevelopmentMailRejectsMalformedOriginalRecipient()
    {
        var result = await CreateMailSender(1).SendAsync(new(["invalid\r\nvalue"], [], [], "件名", "本文"));
        Assert.Equal("INVALID_INPUT", result.FailureReason);
    }

    /// <summary>許可キーへメール・物理パス・任意の秘密値を入れても記録しません。</summary>
    [Fact]
    public void LogDetailsOnlyAcceptFixedOrNumericValues()
    {
        var result = SafeLogDetails.Format(new Dictionary<string, string>
        {
            ["対象"] = "person@example.test", ["処理段階"] = "C:\\private\\data", ["理由"] = "AbCdEfGhSecretValue",
            ["行番号"] = "12", ["件数"] = "3"
        });
        Assert.Equal("[行番号：12][件数：3]", result);
        Assert.Equal("[処理段階：送信][理由：SEND_UNKNOWN]", SafeLogDetails.Format(new Dictionary<string, string> { ["処理段階"] = "送信", ["理由"] = "SEND_UNKNOWN" }));
    }

    /// <summary>中間色のバッジはコントラストが高い黒文字を選びます。</summary>
    [Fact]
    public async Task BadgeChoosesReadableForeground()
    {
        var helper = new CodeBadgeTagHelper(new StubCodes()) { CodeType = "TOOL_STATUS", CodeValue = "PUBLIC" };
        var context = new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "badge");
        var output = new TagHelperOutput("code-badge", new TagHelperAttributeList(), (useCached, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        await helper.ProcessAsync(context, output);
        Assert.Contains("color:#000000", output.Attributes["style"].Value.ToString());
    }

    /// <summary>配置先の内側を保存領域に指定すると起動検証を失敗させます。</summary>
    [Fact]
    public void StorageRejectsDeploymentDirectory()
    {
        var environment = new StubEnvironment { ContentRootPath = AppContext.BaseDirectory };
        var result = new StoragePathValidation(environment).Validate(null, new StorageOptions { TemporaryRoot = AppContext.BaseDirectory });
        Assert.True(result.Failed);
    }

    /// <summary>一時用途を永続領域へ誤保存することを拒否します。</summary>
    [Fact]
    public async Task TemporaryPurposeCannotBeSavedPermanently()
    {
        var storage = new FileStorage.FileStorage(Options.Create(new StorageOptions()), new RejectPolicyProvider(), new StubLogger());
        using var input = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SavePermanentAsync(new(UploadPurpose.ToolInput, "TOOL001", "input.tsv"), input));
    }

    /// <summary>SMTPへ接続しない事前検証用の送信器を組み立てます。</summary>
    private static MailSender CreateMailSender(int maxRecipients) => new(
        Options.Create(new MailOptions { Enabled = true, From = "from@example.test", DevelopmentRecipient = "dev@example.test", MaxRecipients = maxRecipients, TlsMode = MailTlsMode.StartTls }),
        Options.Create(new CommonOptions { EnvironmentCode = "DEVELOPMENT" }), new RejectStorage(), new StubErrors());

    /// <summary>本文が停止する応答を返します。ネットワークは使用しません。</summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        /// <summary>ヘッダーだけを即時に返し、本文をキャンセル待ちにします。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) });
    }

    /// <summary>指定したテストクライアントだけを返します。</summary>
    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        /// <summary>通信しないHTTPハンドラーを使います。</summary>
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>受信停止を再現する読取り専用ストリームです。</summary>
    private sealed class WaitingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        /// <summary>テストの永久停止を避け、キャンセルが伝わらなければ2秒後に失敗します。</summary>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            throw new InvalidOperationException("本文読み取りにタイムアウトが伝わっていません。");
        }
        /// <summary>同期読取りを拒否します。</summary>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>書込みを拒否します。</summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        /// <summary>シークを拒否します。</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        /// <summary>長さ変更を拒否します。</summary>
        public override void SetLength(long value) => throw new NotSupportedException();
        /// <summary>書込みを保持しません。</summary>
        public override void Flush() { }
    }

    /// <summary>事前検証で失敗すべきテストに保存処理が呼ばれることを検出します。</summary>
    private sealed class RejectStorage : IFileStorage
    {
        /// <summary>一時保存はテスト対象外です。</summary>
        public Task<TemporaryFileHandle> SaveTemporaryAsync(TemporaryFileRequest request, Stream source, CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>永続保存はテスト対象外です。</summary>
        public Task<StoredFile> SavePermanentAsync(PermanentFileRequest request, Stream source, CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>添付読取りまで進んだら失敗させます。</summary>
        public Task<Stream> OpenReadAsync(IStoredFileReference reference, CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>削除はテスト対象外です。</summary>
        public Task<FileDeleteResult> DeleteAsync(IStoredFileReference reference, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    /// <summary>DBを使わずエラー識別子を返します。</summary>
    private sealed class StubErrors : IErrorHandler
    {
        /// <summary>SMTP前のエラー結果を検証するため固定表示を返します。</summary>
        public Task<ErrorPresentation> HandleAsync(ErrorRequest request, CancellationToken ct = default) => Task.FromResult(new ErrorPresentation(Guid.NewGuid(), "エラー", 503));
    }

    /// <summary>用途拒否の前にDBへ進まないことを確認します。</summary>
    private sealed class RejectPolicyProvider : IUploadPolicyProvider
    {
        /// <summary>呼ばれた場合はテストを失敗させます。</summary>
        public Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    /// <summary>外部保存を行わない障害ログです。</summary>
    private sealed class StubLogger : ISystemErrorLogger
    {
        /// <summary>ログ処理を成功として完了します。</summary>
        public Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Written);
    }

    /// <summary>固定色のコードを供給します。</summary>
    private sealed class StubCodes : ICodeMasterReader
    {
        /// <summary>テスト用の表示コードを返します。</summary>
        public Task<CodeOption?> FindAsync(string codeType, string codeValue, CancellationToken ct = default) => Task.FromResult<CodeOption?>(new("PUBLIC", "公開", 1, "#808080"));
        /// <summary>一覧取得は使用しません。</summary>
        public Task<IReadOnlyList<CodeOption>> GetOptionsAsync(string codeType, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>保存領域検証用のWebホスト情報です。</summary>
    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
