using System.Net.Mail;
using System.Text.RegularExpressions;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Validation;

/// <summary>業務処理と独立した入力形式を検証します。</summary>
public static partial class CommonValidation
{
    /// <summary>背景色が16進数6桁の形式かを判定します。</summary>
    public static bool IsColor(string? value) => value is not null && ColorPattern().IsMatch(value);

    /// <summary>英数字・下線・ハイフンだけのコード値かを、桁数上限付きで判定します。</summary>
    public static bool IsCode(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        return value is { Length: > 0 } && value.Length <= maxLength && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-');
    }

    /// <summary>DB登録・照合に使用する小文字拡張子の形式かを判定します。</summary>
    public static bool IsExtension(string? value) => value is { Length: >= 2 and <= 20 } && value[0] == '.'
        && value.AsSpan(1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789") < 0;

    /// <summary>任意入力の空欄をNULLへそろえます。必須判定には使用しません。</summary>
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>外部転送や制御文字を許可しない、同一サイト内のパスを検証します。</summary>
    /// <param name="value">検証する値です。ログイン後の戻り先とWebツールの起動先に使います。</param>
    /// <returns>同一サイト内のパスとして扱える場合はtrueです。</returns>
    public static bool IsLocalPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value.Any(char.IsControl) || value.Contains('\\')) return false;
        if (value.StartsWith("//", StringComparison.Ordinal)) return false;
        var decoded = Uri.UnescapeDataString(value);
        return !decoded.Any(char.IsControl) && !decoded.Contains('\\') && !decoded.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>表示名や改行を含まないメールアドレス単体を検証します。</summary>
    public static bool IsEmail(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl)
        && MailAddress.TryCreate(value, out var address) && address.Address == value;

    /// <summary>末尾空白を含むUTF-16単位で長さを検証します。</summary>
    public static FieldError? ValidateText(string field, string? value, int maxLength, bool required)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        if (required && string.IsNullOrWhiteSpace(value)) return new(field, "REQUIRED", "入力してください。");
        return value?.Length > maxLength ? new(field, "MAX_LENGTH", $"{maxLength}文字以内で入力してください。") : null;
    }

    /// <summary>背景色の完全一致パターンを生成します。</summary>
    [GeneratedRegex(@"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
