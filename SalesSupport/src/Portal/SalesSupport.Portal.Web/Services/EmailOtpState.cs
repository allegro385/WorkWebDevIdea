using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesSupport.Portal.Web.Services;

/// <summary>IdentityのUserTokensに保存するOTP要求と送信履歴です。コード自体は保存しません。</summary>
public sealed class EmailOtpState
{
    public const string LoginProvider = "SalesSupport.EmailOtp";
    public const string TokenName = "State.v1";
    public Guid AttemptId { get; set; }
    public Guid TokenId { get; set; }
    public string SecurityStamp { get; set; } = "";
    public string LoginId { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset PendingUntil { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Delivered { get; set; }
    public List<DateTimeOffset> SendTimes { get; set; } = [];

    /// <summary>破損した状態を未登録として扱わず、認証を中止します。</summary>
    public static EmailOtpState Read(string? value) => value is null ? new() :
        JsonSerializer.Deserialize<EmailOtpState>(value) ?? throw new InvalidOperationException("OTPの状態を取得できません。");

    /// <summary>直近15分の送信だけを残し、別のログイン試行でも制限を維持します。</summary>
    public bool CanSend(DateTimeOffset now)
    {
        SendTimes.RemoveAll(sent => sent <= now.AddMinutes(-15));
        return SendTimes.Count < 5 && (SendTimes.Count == 0 || SendTimes.Max() <= now.AddSeconds(-60));
    }

    /// <summary>再送ごとに用途を変更し、以前のコードを検証対象から除外します。</summary>
    [JsonIgnore]
    public string Purpose => $"PortalEmailOtp:{TokenId:N}";

    /// <summary>コードの消費後も送信履歴を残し、再ログインによる上限回避を防ぎます。</summary>
    public void Consume()
    {
        AttemptId = Guid.Empty;
        TokenId = Guid.Empty;
        Delivered = false;
        SecurityStamp = LoginId = Email = "";
    }
}
