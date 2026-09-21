using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>問い合わせIDの採番だけを扱います。採番用Entityの通常更新は行いません。</summary>
public interface IInquiryIdAllocator
{
    /// <summary>既存の採番プロシージャを外側トランザクションなしで呼び、日別連番を確保します。</summary>
    /// <param name="date">JSTで求めた受付日です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>YYYYMMDDと4桁連番からなる12桁の問い合わせIDです。</returns>
    Task<string> AllocateAsync(DateOnly date, CancellationToken ct = default);
}

/// <summary>採番の直列化はプロシージャ側の行ロックに委ね、確保した番号は戻しません。</summary>
public sealed class InquiryIdAllocator(PortalDbContext db) : IInquiryIdAllocator
{
    /// <summary>採番に失敗した場合は例外を呼出元へ返し、番号を再利用しません。</summary>
    public async Task<string> AllocateAsync(DateOnly date, CancellationToken ct = default)
    {
        var sequenceDate = new SqlParameter("@SequenceDate", SqlDbType.Date) { Value = date.ToDateTime(TimeOnly.MinValue) };
        var inquiryId = new SqlParameter("@InquiryId", SqlDbType.Char, 12) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync("EXEC portal.AllocateInquiryId @SequenceDate, @InquiryId OUTPUT",
            new object[] { sequenceDate, inquiryId }, ct);
        return inquiryId.Value as string ?? throw new InvalidOperationException("問い合わせIDを採番できませんでした。");
    }
}
