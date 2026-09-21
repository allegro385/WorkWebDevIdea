using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>通知設定の保存結果です。</summary>
public enum PreferenceOutcome
{
    /// <summary>本人の設定を更新しました。</summary>
    Succeeded,
    /// <summary>ほかの操作で更新されていたため保存しませんでした。</summary>
    Conflict,
    /// <summary>対象の設定が存在せず、整合性エラーとして扱います。</summary>
    Unavailable
}

/// <summary>保存結果と、成功時の最新の設定値です。</summary>
/// <param name="Outcome">保存の判定結果です。</param>
/// <param name="Preferences">成功時の最新値です。失敗時はnullです。</param>
public sealed record PreferenceSaveResult(PreferenceOutcome Outcome, UserPreferencesDto? Preferences);

/// <summary>本人の通知設定を取得・更新します。</summary>
public interface IPreferenceService
{
    /// <summary>本人の通知設定を返します。未登録の場合はnullを返します。</summary>
    Task<UserPreferencesDto?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>取得時のUpdateCountと照合し、本人の2項目だけを更新します。</summary>
    Task<PreferenceSaveResult> SaveAsync(Guid userId, UserPreferencesDto input, CancellationToken ct = default);
}

/// <summary>存在しない設定を通知有効とみなさず、1行の競合更新として扱います。</summary>
public sealed class PreferenceService(PortalDbContext db, IActivityLogger activity) : IPreferenceService
{
    /// <summary>本人の1行だけを追跡なしで取得します。</summary>
    public async Task<UserPreferencesDto?> GetAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserPreferences.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new UserPreferencesDto(x.SystemNoticeMailEnabled, x.FavoriteToolNoticeMailEnabled, x.UpdateCount))
            .SingleOrDefaultAsync(ct);

    /// <summary>保存の成否を操作ログへ残し、成功時は更新後の値を返します。</summary>
    public async Task<PreferenceSaveResult> SaveAsync(Guid userId, UserPreferencesDto input, CancellationToken ct = default)
    {
        var result = await ApplyAsync(userId, input, ct);
        var succeeded = result.Outcome == PreferenceOutcome.Succeeded;
        await activity.WriteAsync(new ActivityEvent("PREFERENCE_UPDATE", succeeded ? "SUCCESS" : "FAILURE",
            succeeded ? null : result.Outcome == PreferenceOutcome.Conflict ? "CONFLICT" : "INVALID_INPUT",
            "USER", userId.ToString("N"), succeeded ? Changes(input) : null), ct);
        return result;
    }

    /// <summary>提出されたUpdateCountと現在値を照合してから更新します。</summary>
    private async Task<PreferenceSaveResult> ApplyAsync(Guid userId, UserPreferencesDto input, CancellationToken ct)
    {
        var preference = await db.UserPreferences.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (preference is null) return new(PreferenceOutcome.Unavailable, null);
        if (preference.UpdateCount != input.UpdateCount) return new(PreferenceOutcome.Conflict, null);

        preference.SystemNoticeMailEnabled = input.SystemNoticeMailEnabled;
        preference.FavoriteToolNoticeMailEnabled = input.FavoriteToolNoticeMailEnabled;
        try
        {
            await db.SaveChangesAsync(ct);
            // 監査トリガーが更新するUpdateCountを読み直し、次回保存の競合判定へ使用します。
            await db.Entry(preference).ReloadAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return new(PreferenceOutcome.Conflict, null); }
        return new(PreferenceOutcome.Succeeded,
            new UserPreferencesDto(preference.SystemNoticeMailEnabled, preference.FavoriteToolNoticeMailEnabled, preference.UpdateCount));
    }

    /// <summary>操作ログへ残す変更後の値をコード値だけで表します。</summary>
    private static IReadOnlyList<ActivityChange> Changes(UserPreferencesDto input) =>
    [
        new("SystemNoticeMailEnabled", null, input.SystemNoticeMailEnabled ? "1" : "0"),
        new("FavoriteToolNoticeMailEnabled", null, input.FavoriteToolNoticeMailEnabled ? "1" : "0")
    ];
}
