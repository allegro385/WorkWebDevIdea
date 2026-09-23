using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DataExport;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Validation;
using SalesSupport.SampleEstimate.Web.Models;

namespace SalesSupport.SampleEstimate.Web.Services;

/// <summary>実行要求です。ファイルはControllerが開いたStreamをそのまま渡します。</summary>
/// <param name="Input">画面の入力項目です。</param>
/// <param name="DetailContent">明細ファイルの内容です。選択がない場合はNULLです。</param>
/// <param name="DetailFileName">アップロードされた元のファイル名です。保存名には使用しません。</param>
public sealed record EstimateRequest(EstimateInput Input, Stream? DetailContent, string? DetailFileName);

/// <summary>実行結果の区分です。入力不正は予期しない障害と区別します。</summary>
public enum EstimateOutcomeKind
{
    /// <summary>計算できました。</summary>
    Calculated,
    /// <summary>入力が条件を満たしていません。</summary>
    InvalidInput
}

/// <summary>実行結果です。入力不正のときは項目エラーだけを返します。</summary>
/// <param name="Kind">結果の区分です。</param>
/// <param name="Result">計算できた場合の結果です。入力不正ではNULLです。</param>
/// <param name="Errors">画面へ返す項目エラーです。</param>
public sealed record EstimateOutcome(EstimateOutcomeKind Kind, EstimateResult? Result, ValidationResult Errors);

/// <summary>入力の検証、計算およびTSV出力を担当します。HTTPと画面遷移はControllerが扱います。</summary>
public interface IEstimateService
{
    /// <summary>区分の選択肢を返します。</summary>
    IReadOnlyList<EstimateCategory> GetCategories();

    /// <summary>明細ファイルの許可条件をCommonから取得し、画面案内の文言を返します。</summary>
    Task<string> GetDetailFileHintAsync(CancellationToken ct = default);

    /// <summary>入力を再検証し、明細ファイルを一時領域で処理して計算します。</summary>
    Task<EstimateOutcome> CalculateAsync(EstimateRequest request, CancellationToken ct = default);

    /// <summary>結果をTSVへ書き出します。内容は保存せず、応答でだけ返します。</summary>
    Task<byte[]> WriteTsvAsync(EstimateResult result, CancellationToken ct = default);

    /// <summary>ダウンロード時のファイル名を返します。</summary>
    string BuildFileName(EstimateResult result);
}

/// <summary>サンプル見積の入力検証と副作用のない計算・TSV出力を担当します。案件保存は別Serviceです。</summary>
public sealed class EstimateService(IFileStorage storage, IUploadPolicyProvider policies, IDelimitedTextWriter writer,
    IBusinessDateProvider businessDate, IOptions<CommonOptions> options) : IEstimateService
{
    /// <summary>明細ファイルの項目名です。項目エラーを入力欄へ対応付けます。</summary>
    public const string DetailFileField = "DetailFile";

    /// <summary>適用日として受け付ける業務日付からの日数です。範囲検証の例として使用します。</summary>
    private const int AppliedOnRangeDays = 365;

    private static readonly EstimateCategory[] Categories =
    [
        new("STANDARD", "通常", 0m),
        new("CAMPAIGN", "キャンペーン", 0.10m)
    ];

    /// <summary>固定の選択肢を返します。コードマスタで管理する場合は`ICodeMasterReader`へ差し替えます。</summary>
    public IReadOnlyList<EstimateCategory> GetCategories() => Categories;

    /// <summary>DBに登録された条件だけを案内し、条件を取得できない場合は例外を呼出元へ返します。</summary>
    public async Task<string> GetDetailFileHintAsync(CancellationToken ct = default)
    {
        var policy = await policies.GetAsync(UploadPurpose.ToolInput, options.Value.ToolId, ct);
        var megabytes = policy.MaxFileSizeBytes / 1024m / 1024m;
        return $"{string.Join("、", policy.Extensions)}のファイルを{megabytes:0.#}MBまで選択できます。処理後に一時ファイルは削除されます。";
    }

    /// <summary>入力不正を予定された結果として返し、ファイルは正常・異常のどちらでも削除します。</summary>
    public async Task<EstimateOutcome> CalculateAsync(EstimateRequest request, CancellationToken ct = default)
    {
        var input = request.Input;
        List<FieldError> errors = [];
        if (CommonValidation.ValidateText(nameof(EstimateInput.ProjectName), input.ProjectName, 60, required: true) is { } nameError)
            errors.Add(nameError);
        if (input.Quantity is not (>= 1 and <= 9999))
            errors.Add(new(nameof(EstimateInput.Quantity), "OUT_OF_RANGE", "数量は1以上9,999以下で入力してください。"));
        if (input.UnitPrice is not (>= 0 and <= 9_999_999))
            errors.Add(new(nameof(EstimateInput.UnitPrice), "OUT_OF_RANGE", "単価は0以上9,999,999以下で入力してください。"));
        var category = Categories.FirstOrDefault(x => x.Code == input.CategoryCode);
        if (category is null) errors.Add(new(nameof(EstimateInput.CategoryCode), "INVALID_INPUT", "区分を選択してください。"));
        if (!Enum.IsDefined(input.Output)) errors.Add(new(nameof(EstimateInput.Output), "INVALID_INPUT", "出力方法を選択してください。"));
        // 適用日は業務日付を基準に前後1年までとし、開発用の業務日付設定にも追従させます。
        var today = await businessDate.GetTodayAsync(ct);
        if (input.AppliedOn is not { } appliedOn || appliedOn < today.AddDays(-AppliedOnRangeDays) || appliedOn > today.AddDays(AppliedOnRangeDays))
            errors.Add(new(nameof(EstimateInput.AppliedOn), "OUT_OF_RANGE", $"適用日は{today.AddDays(-AppliedOnRangeDays):yyyy/MM/dd}から{today.AddDays(AppliedOnRangeDays):yyyy/MM/dd}までで入力してください。"));

        int? detailRowCount = null;
        TemporaryFileHandle? detail = null;
        try
        {
            if (request.DetailContent is not null)
            {
                try
                {
                    // 保存時にCommonがTOOL＋TOOL_INPUTの条件で拡張子と実測容量を検証します。
                    detail = await storage.SaveTemporaryAsync(
                        new TemporaryFileRequest(UploadPurpose.ToolInput, options.Value.ToolId, request.DetailFileName ?? ""), request.DetailContent, ct);
                }
                catch (UploadRejectedException exception) { errors.Add(exception.Error with { Field = DetailFileField }); }
            }
            if (errors.Count != 0) return new(EstimateOutcomeKind.InvalidInput, null, new(errors));
            if (detail is not null) detailRowCount = await CountRowsAsync(detail, ct);

            var subtotal = (decimal)input.Quantity!.Value * input.UnitPrice!.Value;
            // 割引額は円未満を切り捨て、合計との差が出ないよう差し引きで求めます。
            var discount = decimal.Floor(subtotal * category!.DiscountRate);
            var result = new EstimateResult(input.ProjectName!.Trim(), input.AppliedOn!.Value, category.Name,
                input.Quantity!.Value, input.UnitPrice!.Value, subtotal, discount, subtotal - discount, detailRowCount);
            return new(EstimateOutcomeKind.Calculated, result, new([]));
        }
        finally
        {
            // 一時ファイルは結果にかかわらず削除します。破棄の失敗は業務結果を変えません。
            if (detail is not null) await detail.DisposeAsync();
        }
    }

    /// <summary>Commonの区切りテキスト出力を使い、数式解釈への対策と文字コードを統一します。</summary>
    public async Task<byte[]> WriteTsvAsync(EstimateResult result, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        var definition = new ExportDefinition(DelimitedFormat.Tsv, ["項目", "値"]);
        await writer.WriteAsync(buffer, definition, ToRowsAsync(result, ct), ct);
        return buffer.ToArray();
    }

    /// <summary>実行日時ではなく適用日で識別できるASCIIのファイル名を返します。</summary>
    public string BuildFileName(EstimateResult result) =>
        "estimate-" + result.AppliedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".tsv";

    /// <summary>一時ファイルを読み直して行数だけを数えます。内容は保持・記録しません。</summary>
    /// <param name="detail">一時領域に保存した明細ファイルです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>空行を除いた行数です。</returns>
    private async Task<int> CountRowsAsync(TemporaryFileHandle detail, CancellationToken ct)
    {
        await using var content = await storage.OpenReadAsync(detail, ct);
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
            if (!string.IsNullOrWhiteSpace(line)) rows++;
        return rows;
    }

    /// <summary>少数の結果行を区切りテキスト出力の非同期列挙へ渡します。</summary>
    /// <param name="result">出力する計算結果です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>見出しに続けて書き出す行です。</returns>
    private static async IAsyncEnumerable<ExportRow> ToRowsAsync(EstimateResult result, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ExportRow(["案件名", result.ProjectName]);
        yield return new ExportRow(["適用日", result.AppliedOn]);
        yield return new ExportRow(["区分", result.CategoryName]);
        yield return new ExportRow(["数量", result.Quantity]);
        yield return new ExportRow(["単価", result.UnitPrice]);
        yield return new ExportRow(["小計", result.Subtotal]);
        yield return new ExportRow(["割引額", result.DiscountAmount]);
        yield return new ExportRow(["合計", result.Total]);
        yield return new ExportRow(["明細行数", result.DetailRowCount]);
        // 行はメモリー上にあり待機しませんが、出力側の契約にあわせて非同期列挙で返します。
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
    }
}
