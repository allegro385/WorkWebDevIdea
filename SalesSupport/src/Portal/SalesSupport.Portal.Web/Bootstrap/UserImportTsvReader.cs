using Microsoft.AspNetCore.Identity;
using SalesSupport.Common.Validation;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>TSVの1行分の登録内容です。失敗位置を入力値なしで伝えるため行番号を保持します。</summary>
/// <param name="LineNumber">ファイルの行番号です。</param>
/// <param name="LoginId">ログインIDです。</param>
/// <param name="Email">メールアドレスです。</param>
/// <param name="Password">平文のパスワードです。空文字は変更しないことを表します。</param>
/// <param name="DisplayName">表示名です。</param>
/// <param name="RoleCode">ロールです。登録済みかどうかはDBと照合します。</param>
/// <param name="IsActive">有効なユーザーならtrueです。</param>
public sealed record UserImportRow(int LineNumber, string LoginId, string Email, string Password, string DisplayName, string RoleCode, bool IsActive);

/// <summary>TSV全体の検証結果です。不備があるときは登録対象を返しません。</summary>
/// <param name="Rows">登録対象の行です。</param>
/// <param name="Errors">行番号と理由です。入力値は含めません。</param>
public sealed record UserImportTsvResult(IReadOnlyList<UserImportRow> Rows, IReadOnlyList<string> Errors);

/// <summary>見出し付き6列のTSVを検証し、Identityの正規化でファイル内の重複を検出します。</summary>
public sealed class UserImportTsvReader(ILookupNormalizer normalizer)
{
    /// <summary>資格情報を一時的に保持するメモリーと一度の登録件数を制限します。</summary>
    public const int MaxRows = 1000;

    /// <summary>見出しと行区切りを含む入力文字数の上限です。</summary>
    public const int MaxCharacters = 1024 * 1024;

    /// <summary>1行目に求める見出しです。列の取り違えを防ぐため順序も固定します。</summary>
    private const string Header = "LoginId\tEmail\tPassword\tDisplayName\tRoleCode\tIsActive";

    /// <summary>全行を検証し、不備があれば全行分の理由を入力値なしで返します。</summary>
    /// <param name="input">UTF-8で読み取るTSVです。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    /// <returns>登録対象の行、または不備の一覧です。</returns>
    public async Task<UserImportTsvResult> ReadAsync(TextReader input, CancellationToken ct = default)
    {
        List<UserImportRow> rows = [];
        List<string> errors = [];
        var loginIds = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        var characters = 0L;
        var headerFound = false;
        while (await input.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            characters += line.Length + 1L;
            if (characters > MaxCharacters) return Reject("TSVの入力文字数が上限を超えています。");
            if (line.Length == 0) continue;
            if (!headerFound)
            {
                if (line != Header) return Reject("1行目は見出し（LoginId、Email、Password、DisplayName、RoleCode、IsActiveのタブ区切り）にしてください。");
                headerFound = true;
                continue;
            }
            if (rows.Count + errors.Count >= MaxRows) return Reject($"TSVの登録件数は{MaxRows}件以内にしてください。");

            var row = Parse(lineNumber, line, out var reason);
            if (row is not null) reason = FindDuplicate(row, loginIds, emails);
            if (reason is not null) errors.Add($"{lineNumber}行目：{reason}");
            else rows.Add(row!);
        }
        if (!headerFound) return Reject("1行目は見出し（LoginId、Email、Password、DisplayName、RoleCode、IsActiveのタブ区切り）にしてください。");
        if (errors.Count != 0) return new([], errors);
        return rows.Count == 0 ? Reject("TSVに登録データがありません。") : new(rows, []);
    }

    /// <summary>1行の形式を検証します。ログインIDとパスワードは元の値を保ち、それ以外は前後の空白を除きます。</summary>
    /// <param name="lineNumber">ファイルの行番号です。</param>
    /// <param name="line">TSVの1行です。</param>
    /// <param name="reason">不備の理由です。問題がなければNULLです。</param>
    /// <returns>解釈した行です。不備があればNULLです。</returns>
    private static UserImportRow? Parse(int lineNumber, string line, out string? reason)
    {
        var columns = line.Split('\t');
        reason = columns.Length != 6 ? "LoginId、Email、Password、DisplayName、RoleCode、IsActiveの6列が必要です。"
            : columns.Any(column => column.Any(char.IsControl)) ? "制御文字は使用できません。"
            : null;
        if (reason is not null) return null;

        var email = columns[1].Trim();
        var displayName = columns[3].Trim();
        var roleCode = columns[4].Trim();
        var isActive = columns[5].Trim();
        reason = string.IsNullOrWhiteSpace(columns[0]) || columns[0].Length > 256 ? "ログインIDは必須で、256文字以内にしてください。"
            : !CommonValidation.IsEmail(email) || email.Length > 256 ? "メールアドレスの形式が正しくありません。"
            : displayName.Length is 0 or > 100 ? "表示名は必須で、100文字以内にしてください。"
            : roleCode.Length == 0 ? "ロールは必須です。"
            : isActive is not ("1" or "0") ? "IsActiveは1または0にしてください。"
            : null;
        return reason is null ? new(lineNumber, columns[0], email, columns[2], displayName, roleCode, isActive == "1") : null;
    }

    /// <summary>Identityの正規化後の値で、ファイル内の重複を検出します。</summary>
    /// <param name="row">検証済みの行です。</param>
    /// <param name="loginIds">登場済みのログインIDです。</param>
    /// <param name="emails">登場済みのメールアドレスです。</param>
    /// <returns>重複の理由です。問題がなければNULLです。</returns>
    private string? FindDuplicate(UserImportRow row, HashSet<string> loginIds, HashSet<string> emails)
    {
        if (!loginIds.Add(normalizer.NormalizeName(row.LoginId))) return "ファイル内でログインIDが重複しています。";
        return emails.Add(normalizer.NormalizeEmail(row.Email)) ? null : "ファイル内でメールアドレスが重複しています。";
    }

    /// <summary>ファイル全体の不備では登録対象を返しません。</summary>
    /// <param name="message">不備の理由です。</param>
    /// <returns>登録対象のない検証結果です。</returns>
    private static UserImportTsvResult Reject(string message) => new([], [message]);
}
