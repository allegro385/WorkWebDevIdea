using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>配布アプリと参考資料を認可済みの経路だけで配信します。</summary>
[Route("tools/{toolId}/files")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolFilesController(IToolFileService files, IAccessEvaluator access) : Controller
{
    /// <summary>ツール状態を再判定し、常に添付として返します。物理パスは応答へ含めません。</summary>
    /// <param name="toolId">対象ツールです。</param>
    /// <param name="fileId">対象ツールに所属するファイルです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    [HttpGet("{fileId:int}")]
    public async Task<IActionResult> Download(string toolId, int fileId, CancellationToken ct)
    {
        var decision = await access.EvaluateAsync(new AccessRequest(AccessPurpose.ToolDownload, toolId), ct);
        if (!decision.Allowed) return StatusCode(decision.StatusCode);

        var download = await files.OpenAsync(toolId, fileId, ct);
        if (download is null) return NotFound();
        return File(download.Content, "application/octet-stream", download.FileName);
    }
}
