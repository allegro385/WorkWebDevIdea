using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Services;

/// <summary>取込対象の1行です。</summary>
/// <param name="LineNumber">ヘッダーを含む元TSVの行番号です。</param>
/// <param name="Email">メールアドレスです。ログインIDとUserNameに使用します。</param>
/// <param name="DisplayName">表示名です。</param>
public sealed record UserImportRow(int LineNumber, string Email, string DisplayName);

/// <summary>行番号付きの検証エラーです。</summary>
/// <param name="LineNumber">対象の行番号です。ファイル全体のエラーでは0です。</param>
/// <param name="Message">画面へ表示する日本語の内容です。</param>
public sealed record UserImportError(int LineNumber, string Message);

/// <summary>登録結果の1行です。メール送信の成否は含めません。</summary>
/// <param name="LineNumber">対象の行番号です。</param>
/// <param name="Email">対象のメールアドレスです。</param>
/// <param name="Status">登録済み、登録失敗または未処理です。</param>
public sealed record UserImportResultRow(int LineNumber, string Email, string Status);

/// <summary>検証結果と、登録を実行するための確認IDです。</summary>
/// <param name="Rows">登録予定の行です。</param>
/// <param name="Errors">検証エラーです。1件でもあれば登録を開始しません。</param>
/// <param name="ConfirmationId">確認IDです。エラーがある場合や保持できない場合はnullです。</param>
public sealed record UserImportPreview(IReadOnlyList<UserImportRow> Rows, IReadOnlyList<UserImportError> Errors, Guid? ConfirmationId);

/// <summary>新規ユーザーのTSV一括登録を扱います。単独登録の画面・APIは設けません。</summary>
public interface IUserImportService
{
    /// <summary>TSVを検証し、エラーがなければ確認IDを発行します。登録は行いません。</summary>
    Task<UserImportPreview> ValidateAsync(Guid operatorUserId, Stream content, string fileName, CancellationToken ct = default);

    /// <summary>確認IDを一度だけ使用し、全行を再検証してから1行ずつ登録します。</summary>
    Task<IReadOnlyList<UserImportResultRow>> ExecuteAsync(Guid operatorUserId, Guid confirmationId, CancellationToken ct = default);
}

/// <summary>ユーザーと利用者設定を1行ごとのトランザクションで確定し、確定済みの行は戻しません。</summary>
public sealed class UserImportService(PortalDbContext db, UserManager<ApplicationUser> users, IFileStorage storage,
    IConfirmationStore confirmations, IPasswordLinkService links, IActivityLogger activity) : IUserImportService
{
    /// <summary>ヘッダーを除く取込可能なデータ行数の上限です。</summary>
    public const int MaxRows = 100;

    /// <summary>取込に使用する列見出しです。パスワード・権限の列は受け付けません。</summary>
    private static readonly string[] Header = ["Email", "DisplayName"];

    /// <summary>容量・拡張子はCommon、ヘッダー・UTF-8・行数はPortalで検証します。</summary>
    public async Task<UserImportPreview> ValidateAsync(Guid operatorUserId, Stream content, string fileName, CancellationToken ct = default)
    {
        TemporaryFileHandle? handle = null;
        try
        {
            try { handle = await storage.SaveTemporaryAsync(new TemporaryFileRequest(UploadPurpose.UserImport, null, fileName), content, ct); }
            catch (UploadRejectedException exception) { return new([], [new UserImportError(0, exception.Error.Message)], null); }

            var (rows, errors) = await ReadAsync(handle, ct);
            if (errors.Count == 0) errors = await FindDuplicatesAsync(rows, ct);
            if (errors.Count != 0) return new([], errors, null);

            var confirmationId = confirmations.Issue(operatorUserId, new UserImportTicket(rows));
            return confirmationId is null
                ? new([], [new UserImportError(0, "確認内容を保持できませんでした。時間をおいて、もう一度実行してください。")], null)
                : new UserImportPreview(rows, [], confirmationId);
        }
        finally
        {
            if (handle is not null) await handle.DisposeAsync();
        }
    }

    /// <summary>UTF-8のTSVとして読み取り、ヘッダー・列数・必須・桁数を検証します。</summary>
    private async Task<(List<UserImportRow> Rows, List<UserImportError> Errors)> ReadAsync(TemporaryFileHandle handle, CancellationToken ct)
    {
        List<UserImportRow> rows = [];
        List<UserImportError> errors = [];
        await using var stream = await storage.OpenReadAsync(handle, ct);
        // 不正なUTF-8は置換せず、取込全体のエラーとして扱います。
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        try
        {
            var header = await reader.ReadLineAsync(ct);
            if (header is null || !IsHeader(header)) return (rows, [new UserImportError(1, "1行目はEmailとDisplayNameの2列の見出しにしてください。")]);

            var lineNumber = 1;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                lineNumber++;
                if (line.Length == 0) continue;
                if (rows.Count >= MaxRows)
                {
                    errors.Add(new UserImportError(lineNumber, $"データ行は{MaxRows}件までです。件数を分けて取り込んでください。"));
                    break;
                }
                var columns = line.Split('\t');
                if (columns.Length != 2)
                {
                    errors.Add(new UserImportError(lineNumber, "EmailとDisplayNameの2列で入力してください。"));
                    continue;
                }
                var email = columns[0].Trim();
                var displayName = columns[1].Trim();
                if (!CommonValidation.IsEmail(email) || email.Length > 256)
                    errors.Add(new UserImportError(lineNumber, "メールアドレスの形式が正しくありません。"));
                else if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100)
                    errors.Add(new UserImportError(lineNumber, "表示名は1文字以上100文字以内で入力してください。"));
                else
                    rows.Add(new UserImportRow(lineNumber, email, displayName));
            }
        }
        catch (DecoderFallbackException)
        {
            return ([], [new UserImportError(0, "UTF-8として読み取れない文字が含まれています。")]);
        }
        if (rows.Count == 0 && errors.Count == 0) errors.Add(new UserImportError(0, "登録するデータ行がありません。"));
        return (rows, errors);
    }

    /// <summary>ファイル内とDBの正規化メールアドレスの重複を検出します。</summary>
    private async Task<List<UserImportError>> FindDuplicatesAsync(IReadOnlyList<UserImportRow> rows, CancellationToken ct)
    {
        List<UserImportError> errors = [];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        List<string> normalizedList = [];
        foreach (var row in rows)
        {
            var normalized = users.NormalizeEmail(row.Email) ?? row.Email.ToUpperInvariant();
            normalizedList.Add(normalized);
            if (!seen.TryAdd(normalized, row.LineNumber))
                errors.Add(new UserImportError(row.LineNumber, $"{seen[normalized]}行目と同じメールアドレスです。"));
        }
        var existing = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail != null && normalizedList.Contains(x.NormalizedEmail))
            .Select(x => x.NormalizedEmail!).ToListAsync(ct);
        foreach (var row in rows.Where(row => existing.Contains(users.NormalizeEmail(row.Email) ?? row.Email.ToUpperInvariant(), StringComparer.Ordinal)))
            errors.Add(new UserImportError(row.LineNumber, "登録済みのメールアドレスです。"));
        return errors.OrderBy(x => x.LineNumber).ToList();
    }

    /// <summary>登録は1行ごとに確定し、失敗した行以降は未処理として結果に残します。</summary>
    public async Task<IReadOnlyList<UserImportResultRow>> ExecuteAsync(Guid operatorUserId, Guid confirmationId, CancellationToken ct = default)
    {
        if (confirmations.Consume<UserImportTicket>(confirmationId, operatorUserId) is not { } ticket) return [];

        var duplicates = await FindDuplicatesAsync(ticket.Rows, ct);
        if (duplicates.Count != 0)
            return ticket.Rows.Select(row => new UserImportResultRow(row.LineNumber, row.Email, "未処理")).ToList();

        List<UserImportResultRow> results = [];
        List<Guid> created = [];
        var stopped = false;
        foreach (var row in ticket.Rows)
        {
            if (stopped)
            {
                results.Add(new UserImportResultRow(row.LineNumber, row.Email, "未処理"));
                continue;
            }
            var userId = await CreateAsync(row, ct);
            await activity.WriteAsync(new ActivityEvent("USER_CREATE", userId is null ? "FAILURE" : "SUCCESS",
                userId is null ? "INVALID_INPUT" : null, "USER", userId?.ToString("N")), ct);
            if (userId is null)
            {
                // DBの失敗では以後の登録を停止します。確定済みの行は戻しません。
                stopped = true;
                results.Add(new UserImportResultRow(row.LineNumber, row.Email, "登録失敗"));
                continue;
            }
            created.Add(userId.Value);
            results.Add(new UserImportResultRow(row.LineNumber, row.Email, "登録済み"));
        }

        // 登録の確定後に初回パスワード設定リンクを発行します。送信失敗を理由に登録をやり直しません。
        foreach (var userId in created) await links.IssueForUserAsync(userId, ct);
        return results;
    }

    /// <summary>ユーザーと利用者設定を同一トランザクションで作成します。</summary>
    private async Task<Guid?> CreateAsync(UserImportRow row, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = row.Email,
                Email = row.Email,
                EmailConfirmed = false,
                DisplayName = row.DisplayName,
                RoleCode = "USER",
                IsActive = true,
                LockoutEnabled = true
            };
            // パスワードは管理者が設定せず、初回設定リンクで利用者が登録します。
            if (!(await users.CreateAsync(user)).Succeeded) return null;

            db.UserPreferences.Add(new UserPreference
            {
                UserId = user.Id,
                SystemNoticeMailEnabled = true,
                FavoriteToolNoticeMailEnabled = true
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return user.Id;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    /// <summary>見出し行がEmailとDisplayNameの2列かどうかを判定します。</summary>
    private static bool IsHeader(string line)
    {
        var columns = line.Split('\t');
        return columns.Length == Header.Length
            && columns.Select((column, index) => string.Equals(column.Trim(), Header[index], StringComparison.OrdinalIgnoreCase)).All(x => x);
    }

    /// <summary>確認時に保持する取込内容です。</summary>
    /// <param name="Rows">検証済みの登録予定行です。</param>
    private sealed record UserImportTicket(IReadOnlyList<UserImportRow> Rows);
}
