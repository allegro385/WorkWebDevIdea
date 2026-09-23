using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Authentication;
using SalesSupport.SampleEstimate.Web.Data;
using SalesSupport.SampleEstimate.Web.Models;

namespace SalesSupport.SampleEstimate.Web.Services;

/// <summary>案件一覧に表示する1件の要約です。</summary>
public sealed record EstimateRecordSummary(Guid RecordId, string ProjectName, DateOnly AppliedOn, string CategoryCode, decimal Total, DateTime UpdatedAt);

/// <summary>検索結果と、同じ条件に該当する全件の集計です。</summary>
public sealed record EstimateRecordList(IReadOnlyList<EstimateRecordSummary> Items, int Count, decimal Total);

/// <summary>更新時の結果です。他人の案件と存在しない案件を区別して漏らしません。</summary>
public enum RecordUpdateResult { Updated, NotFound, Conflict }

/// <summary>サンプル専用テーブルへの検索・登録・更新を提供します。</summary>
public interface IEstimateRecordService
{
    /// <summary>本人の案件を適用日・区分で絞り、合計を集計します。</summary>
    Task<EstimateRecordList> ListAsync(DateOnly? from, DateOnly? to, string? categoryCode, CancellationToken ct = default);
    /// <summary>本人の案件1件を読みます。</summary>
    Task<EstimateRecord?> GetAsync(Guid recordId, CancellationToken ct = default);
    /// <summary>計算済み要約を1件登録します。同じ送信IDは既存の本人の案件を返します。</summary>
    Task<Guid> CreateAsync(Guid submissionId, EstimateInput input, EstimateResult result, CancellationToken ct = default);
    /// <summary>取得時の更新回数が一致したときだけ本人の案件を更新します。</summary>
    Task<RecordUpdateResult> UpdateAsync(Guid recordId, int updateCount, EstimateInput input, EstimateResult result, CancellationToken ct = default);
}

/// <summary>EF Coreを直接使うサンプルの業務サービスです。ユーザーごとに検索範囲を限定します。</summary>
public sealed class EstimateRecordService(SampleEstimateDbContext db, ICurrentUserAccessor current) : IEstimateRecordService
{
    /// <summary>認証済みユーザーのIDです。認可の設定漏れがあっても他人のデータへ倒しません。</summary>
    private Guid UserId => current.User?.UserId ?? throw new UnauthorizedAccessException("認証済みユーザーが必要です。");

    /// <summary>絞り込み後の全件を集計し、一覧は最新100件まで表示します。</summary>
    public async Task<EstimateRecordList> ListAsync(DateOnly? from, DateOnly? to, string? categoryCode, CancellationToken ct = default)
    {
        var userId = UserId;
        var query = db.EstimateRecords.AsNoTracking().Where(x => x.OwnerUserId == userId);
        if (from is { } start) query = query.Where(x => x.AppliedOn >= start);
        if (to is { } end) query = query.Where(x => x.AppliedOn <= end);
        if (!string.IsNullOrEmpty(categoryCode)) query = query.Where(x => x.CategoryCode == categoryCode);
        var totals = await query.GroupBy(_ => 1).Select(g => new { Count = g.Count(), Total = g.Sum(x => x.Total) }).SingleOrDefaultAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.RecordId).Take(100)
            .Select(x => new EstimateRecordSummary(x.RecordId, x.ProjectName, x.AppliedOn, x.CategoryCode, x.Total, x.UpdatedAt))
            .ToListAsync(ct);
        return new(items, totals?.Count ?? 0, totals?.Total ?? 0);
    }

    /// <summary>本人が登録した案件だけを返します。</summary>
    public Task<EstimateRecord?> GetAsync(Guid recordId, CancellationToken ct = default)
    {
        var userId = UserId;
        return db.EstimateRecords.AsNoTracking().SingleOrDefaultAsync(x => x.RecordId == recordId && x.OwnerUserId == userId, ct);
    }

    /// <summary>一意の送信IDで重複登録を抑止し、ファイルではなく結果の数値だけを記録します。</summary>
    public async Task<Guid> CreateAsync(Guid submissionId, EstimateInput input, EstimateResult result, CancellationToken ct = default)
    {
        if (submissionId == Guid.Empty) throw new ArgumentException("送信IDが必要です。", nameof(submissionId));
        var userId = UserId;
        var existing = await db.EstimateRecords.AsNoTracking().Where(x => x.SubmissionId == submissionId && x.OwnerUserId == userId)
            .Select(x => (Guid?)x.RecordId).SingleOrDefaultAsync(ct);
        if (existing is { } id) return id;
        var record = new EstimateRecord
        {
            RecordId = Guid.NewGuid(), SubmissionId = submissionId, OwnerUserId = userId,
            ProjectName = result.ProjectName, AppliedOn = result.AppliedOn, CategoryCode = input.CategoryCode!,
            Quantity = result.Quantity, UnitPrice = result.UnitPrice, Subtotal = result.Subtotal,
            DiscountAmount = result.DiscountAmount, Total = result.Total, DetailRowCount = result.DetailRowCount
        };
        db.EstimateRecords.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // 同時POSTによる一意制約違反だけを既存レコードとして扱い、ほかのDB障害は隠さない。
            db.ChangeTracker.Clear();
            var duplicate = await db.EstimateRecords.AsNoTracking()
                .Where(x => x.SubmissionId == submissionId && x.OwnerUserId == userId)
                .Select(x => (Guid?)x.RecordId).SingleOrDefaultAsync(ct);
            if (duplicate is { } duplicateId) return duplicateId;
            throw;
        }
        return record.RecordId;
    }

    /// <summary>取得時の更新回数を元の値としてEFへ渡し、競合時は上書きしません。</summary>
    public async Task<RecordUpdateResult> UpdateAsync(Guid recordId, int updateCount, EstimateInput input, EstimateResult result, CancellationToken ct = default)
    {
        var userId = UserId;
        var record = await db.EstimateRecords.SingleOrDefaultAsync(x => x.RecordId == recordId && x.OwnerUserId == userId, ct);
        if (record is null) return RecordUpdateResult.NotFound;
        if (updateCount < 0) return RecordUpdateResult.Conflict;
        db.Entry(record).Property(x => x.UpdateCount).OriginalValue = updateCount;
        record.ProjectName = result.ProjectName;
        record.AppliedOn = result.AppliedOn;
        record.CategoryCode = input.CategoryCode!;
        record.Quantity = result.Quantity;
        record.UnitPrice = result.UnitPrice;
        record.Subtotal = result.Subtotal;
        record.DiscountAmount = result.DiscountAmount;
        record.Total = result.Total;
        record.DetailRowCount = null; // 編集時にファイルを再送しないため、古い行数は引き継ぎません。
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return RecordUpdateResult.Conflict; }
        return RecordUpdateResult.Updated;
    }
}
