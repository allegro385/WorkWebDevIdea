using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.DataExport;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Controllers;

/// <summary>A003 ログ管理です。ログ種別と抽出内容を選択してTSVを出力します。</summary>
/// <remarks>画面内の明細表示、検索条件、月別集計、グラフ表示は初期対象外です。</remarks>
[Area("Admin")]
[Route("admin/logs")]
[Authorize(Policy = PortalPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LogsController(ILogExportService exports, IDelimitedTextWriter writer) : Controller
{
    /// <summary>出力条件の選択画面を表示します。</summary>
    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData.SetPageShell(new PageShellModel
        {
            PageTitle = "ログ管理",
            Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb("ログ管理")]
        });
        return View();
    }

    /// <summary>選択したログをTSVで直接ダウンロードします。サーバーへ恒久保存しません。</summary>
    /// <param name="kind">出力するログ種別です。</param>
    /// <param name="extraction">ツール利用ログの抽出内容です。ほかの種別では明細だけを出力します。</param>
    /// <param name="ct">要求のキャンセルトークンです。クライアント切断で読取りを止めます。</param>
    [HttpGet("export")]
    public async Task<IActionResult> Export(LogKind kind, LogExtraction extraction, CancellationToken ct)
    {
        // 読取り開始前に入力を確認します。未定義の選択は出力しません。
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(extraction)) return BadRequest();
        if (kind != LogKind.ToolUsage) extraction = LogExtraction.Detail;

        var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(exports.BuildFileName(kind, extraction));
        Response.ContentType = "text/tab-separated-values; charset=utf-8";
        Response.Headers.ContentDisposition = disposition.ToString();

        await writer.WriteAsync(Response.Body, exports.GetDefinition(kind, extraction), exports.ReadAsync(kind, extraction, ct), ct);
        return new EmptyResult();
    }
}
