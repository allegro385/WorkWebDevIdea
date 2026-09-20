namespace SalesSupport.Common.Contracts;

/// <summary>ホストの種別です。</summary>
public enum ApplicationKind { Portal, Tool }
/// <summary>当該要求で検証済みの利用者です。</summary>
public sealed record CurrentUser(Guid UserId, string DisplayName, string RoleCode);
/// <summary>入力値を含まない項目エラーです。</summary>
public sealed record FieldError(string Field, string Code, string Message);
/// <summary>入力検証の結果です。</summary>
public sealed record ValidationResult(IReadOnlyList<FieldError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
/// <summary>画面へ公開するエラー情報です。</summary>
public sealed record ErrorPresentation(Guid ErrorId, string Message, int StatusCode);
/// <summary>現在ユーザーAPIの応答です。</summary>
public sealed record CurrentUserResponse(Guid UserId, string DisplayName, string RoleCode);
/// <summary>本人の通知設定と競合判定情報です。</summary>
public sealed record UserPreferencesDto(bool SystemNoticeMailEnabled, bool FavoriteToolNoticeMailEnabled, int UpdateCount);
