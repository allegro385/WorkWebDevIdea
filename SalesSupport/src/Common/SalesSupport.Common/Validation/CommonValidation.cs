using System.Net.Mail;
using System.Text.RegularExpressions;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Validation;

/// <summary>業務処理と独立した入力形式を検証します。</summary>
public static partial class CommonValidation
{
    /// <summary>数字3組、各0～99、先頭ゼロ禁止の形式を検証します。</summary>
    public static bool IsVersion(string? value) => value is not null && VersionPattern().IsMatch(value);

    /// <summary>背景色が16進数6桁の形式かを判定します。</summary>
    public static bool IsColor(string? value) => value is not null && ColorPattern().IsMatch(value);

    /// <summary>外部転送や制御文字を許可しない戻り先を検証します。</summary>
    public static bool IsLocalReturnUrl(string? value)
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

    /// <summary>検証済みバージョンを数値の組として比較します。</summary>
    public static int CompareVersions(string left, string right)
    {
        if (!IsVersion(left) || !IsVersion(right)) throw new ArgumentException("バージョン形式が不正です。");
        var first = left.Split('.');
        var second = right.Split('.');
        for (var index = 0; index < first.Length; index++)
        {
            var comparison = int.Parse(first[index]).CompareTo(int.Parse(second[index]));
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    /// <summary>バージョンの完全一致パターンを生成します。</summary>
    [GeneratedRegex(@"\A(?:0|[1-9][0-9]?)\.(?:0|[1-9][0-9]?)\.(?:0|[1-9][0-9]?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    /// <summary>背景色の完全一致パターンを生成します。</summary>
    [GeneratedRegex(@"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
