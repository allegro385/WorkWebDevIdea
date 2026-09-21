using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin;

/// <summary>ツール編集画面の4区画を組み立てます。区画別の保存がどのControllerから呼ばれても同じ画面を返します。</summary>
public sealed class ToolEditPageBuilder(IToolAdminService tools, IToolFileAdminService files, INoticeService notices)
{
    /// <summary>ツール編集画面の表示情報を作成します。指定した区画だけ入力とメッセージを差し替えます。</summary>
    /// <param name="toolId">編集対象のツールです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <param name="basic">基本情報区画の入力です。未指定では登録済みの値を表示します。</param>
    /// <param name="basicMessage">基本情報区画に表示する処理結果です。</param>
    /// <param name="versionEditor">バージョン編集欄の入力です。未指定では編集欄を表示しません。</param>
    /// <param name="versionMessage">バージョン区画に表示する処理結果です。</param>
    /// <param name="referenceEditor">参考資料編集欄の入力です。未指定では編集欄を表示しません。</param>
    /// <param name="contentMessage">提供内容区画に表示する処理結果です。</param>
    /// <param name="noticeEditor">お知らせ編集欄の入力です。未指定では編集欄を表示しません。</param>
    /// <param name="noticeMessage">お知らせ区画に表示する処理結果です。</param>
    /// <returns>対象ツールが存在しない場合はnullを返します。</returns>
    public async Task<ToolEditViewModel?> BuildAsync(string toolId, CancellationToken ct,
        ToolBasicInput? basic = null, OperationMessage? basicMessage = null,
        ToolVersionInput? versionEditor = null, OperationMessage? versionMessage = null,
        ToolReferenceInput? referenceEditor = null, OperationMessage? contentMessage = null,
        NoticeEditInput? noticeEditor = null, OperationMessage? noticeMessage = null)
    {
        var context = await tools.GetEditContextAsync(toolId, versionEditor, ct);
        var content = await files.GetContentAsync(toolId, referenceEditor, ct);
        if (context is null || content is null) return null;

        if (basic is not null) context.Basic.Input = basic;
        context.Basic.Message = basicMessage;
        context.Versions.Message = versionMessage;
        content.Message = contentMessage;

        return new ToolEditViewModel
        {
            ToolId = toolId,
            ToolName = context.ToolName,
            Basic = context.Basic,
            Versions = context.Versions,
            Content = content,
            Notices = new NoticeSectionViewModel
            {
                NoticeType = "TOOL",
                ToolId = toolId,
                Notices = await notices.GetListAsync("TOOL", toolId, ct),
                Editor = noticeEditor,
                Message = noticeMessage
            }
        };
    }
}
