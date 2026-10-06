using Microsoft.AspNetCore.Identity;
using SalesSupport.Common.Validation;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>失敗位置を入力値を表示せずに伝えるため、TSVの行番号を保持します。</summary>
public sealed record LocalTestUserTsvRow(int LineNumber, LocalTestUserInput User);

/// <summary>TSV全体の検証結果です。不正な入力のときは登録対象を返しません。</summary>
public sealed record LocalTestUserTsvResult(IReadOnlyList<LocalTestUserTsvRow> Rows, string? ErrorMessage);

/// <summary>指定順の4列TSVを検証し、Identityの正規化でファイル内の重複を検出します。</summary>
public sealed class LocalTestUserTsvReader(ILookupNormalizer normalizer)
{
    /// <summary>資格情報を一時的に保持するメモリーと一度の登録件数を制限します。</summary>
    public const int MaxRows = 1000;

    /// <summary>ヘッダーと行区切りを含む入力文字数の上限です。</summary>
    public const int MaxCharacters = 1024 * 1024;

    /// <summary>全行を事前検証し、不備があれば入力値を含まない行番号だけのエラーを返します。</summary>
    public async Task<LocalTestUserTsvResult> ReadAsync(TextReader input, string roleCode, CancellationToken ct = default)
    {
        var rows = new List<LocalTestUserTsvRow>();
        var loginIds = new HashSet<string>(StringComparer.Ordinal);
        var emails = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        var characters = 0L;
        var firstContent = true;
        while (await input.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            characters += line.Length + 1L;
            if (characters > MaxCharacters) return Reject("TSVの入力文字数が上限を超えています。");
            if (lineNumber == 1) line = line.TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            if (firstContent && IsHeader(line))
            {
                firstContent = false;
                continue;
            }
            firstContent = false;
            var columns = line.Split('\t');
            if (columns.Length != 4) return Reject($"{lineNumber}行目：ログインID、メールアドレス、パスワード、表示名の4列が必要です。");

            // ログインIDとパスワードは元の値を保ち、メールと表示名だけ対話入力と同じく前後空白を除きます。
            var loginId = columns[0];
            var email = columns[1].Trim();
            var password = columns[2];
            var displayName = columns[3].Trim();
            if (string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256 || !CommonValidation.IsEmail(email)
                || email.Length > 256 || password.Length < 6 || string.IsNullOrWhiteSpace(displayName)
                || displayName.Length > 100 || columns.Any(column => column.Any(char.IsControl)))
                return Reject($"{lineNumber}行目：入力条件を満たしていません。4項目の形式・文字数を確認してください。");

            var normalizedLoginId = normalizer.NormalizeName(loginId);
            var normalizedEmail = normalizer.NormalizeEmail(email);
            if (string.IsNullOrEmpty(normalizedLoginId) || string.IsNullOrEmpty(normalizedEmail))
                return Reject($"{lineNumber}行目：ログインIDまたはメールアドレスを正規化できません。");
            if (!loginIds.Add(normalizedLoginId) || !emails.Add(normalizedEmail))
                return Reject($"{lineNumber}行目：ファイル内でログインIDまたはメールアドレスが重複しています。");
            if (rows.Count >= MaxRows) return Reject($"TSVの登録件数は{MaxRows}件以内にしてください。");
            rows.Add(new LocalTestUserTsvRow(lineNumber, new LocalTestUserInput(loginId, email, displayName, password, roleCode)));
        }
        return rows.Count == 0 ? Reject("TSVに登録データがありません。") : new(rows, null);
    }

    /// <summary>先頭の任意ヘッダーを指定順の完全一致で識別します。</summary>
    private static bool IsHeader(string line) => line is "ログインID\tメールアドレス\tパスワード\t表示名" or "LoginId\tEmail\tPassword\tDisplayName";

    /// <summary>検証不備があるファイルから一部のユーザーだけが登録されることを防ぎます。</summary>
    private static LocalTestUserTsvResult Reject(string message) => new([], message);
}
