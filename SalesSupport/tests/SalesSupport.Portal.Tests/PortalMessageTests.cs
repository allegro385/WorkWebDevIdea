using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>保存後リダイレクトをまたぐ画面内メッセージの受け渡しを確認します。</summary>
public sealed class PortalMessageTests
{
    /// <summary>設定した区分と文言をそのまま取り出せることを確認します。</summary>
    [Fact]
    public void TakeReturnsStoredMessage()
    {
        var tempData = CreateTempData();
        PortalMessages.Set(tempData, OperationMessageKind.Conflict, "他の操作で更新されています。");

        var message = PortalMessages.Take(tempData);
        Assert.NotNull(message);
        Assert.Equal(OperationMessageKind.Conflict, message!.Kind);
        Assert.Equal("他の操作で更新されています。", message.Text);
    }

    /// <summary>区切り文字を含む文言でも全文を保持することを確認します。</summary>
    [Fact]
    public void TakeKeepsTextContainingSeparator()
    {
        var tempData = CreateTempData();
        PortalMessages.Set(tempData, OperationMessageKind.Success, "保存|しました。");

        Assert.Equal("保存|しました。", PortalMessages.Take(tempData)?.Text);
    }

    /// <summary>未設定と形式不正ではメッセージを表示しないことを確認します。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unknown|保存しました。")]
    public void TakeReturnsNullForUnusableValue(string? stored)
    {
        var tempData = CreateTempData();
        if (stored is not null) tempData["SalesSupport.Portal.Message"] = stored;

        Assert.Null(PortalMessages.Take(tempData));
    }

    /// <summary>Cookieへ保存しない一時的なTempDataを作成します。</summary>
    private static ITempDataDictionary CreateTempData() =>
        new TempDataDictionary(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new MemoryTempDataProvider());

    /// <summary>テスト内だけで値を保持する一時データの提供です。</summary>
    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

        /// <summary>保持している値を返します。</summary>
        public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context) => values;

        /// <summary>値を保持します。</summary>
        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values)
        {
            this.values.Clear();
            foreach (var pair in values) this.values[pair.Key] = pair.Value;
        }
    }
}
