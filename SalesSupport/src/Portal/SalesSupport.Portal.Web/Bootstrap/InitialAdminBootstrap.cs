using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>初期管理者として登録する検証済み入力です。</summary>
public sealed record InitialAdminInput(string Email, string DisplayName, string Password);

/// <summary>初期管理者の作成結果です。</summary>
public enum InitialAdminProvisionOutcome
{
    /// <summary>管理者と利用者設定を作成しました。</summary>
    Created,
    /// <summary>有効な管理者が既に存在します。</summary>
    ActiveAdminExists,
    /// <summary>IdentityまたはDBの制約により入力を登録できませんでした。</summary>
    Rejected,
    /// <summary>依存先障害または競合により確定できませんでした。</summary>
    Failed
}

/// <summary>初期管理者を一度だけ作成するDB処理の境界です。</summary>
public interface IInitialAdminProvisioner
{
    /// <summary>有効な管理者が存在するかを、秘密入力を受け取る前に確認します。</summary>
    Task<bool> HasActiveAdminAsync(CancellationToken ct = default);
    /// <summary>最新状態を再確認し、管理者と利用者設定を同一トランザクションで作成します。</summary>
    Task<InitialAdminProvisionOutcome> CreateAsync(InitialAdminInput input, CancellationToken ct = default);
}

/// <summary>UserManagerとPortalDbContextを同じトランザクションで使用して初期管理者を作成します。</summary>
public sealed class InitialAdminProvisioner(PortalDbContext db, UserManager<ApplicationUser> users) : IInitialAdminProvisioner
{
    /// <summary>有効なADMINが一件でもあれば導入コマンドを拒否します。</summary>
    public Task<bool> HasActiveAdminAsync(CancellationToken ct = default) =>
        db.Users.AsNoTracking().AnyAsync(user => user.RoleCode == "ADMIN" && user.IsActive, ct);

    /// <summary>直列化した保存単位で有効ADMINの不存在とユーザー・設定の作成を確定します。</summary>
    public async Task<InitialAdminProvisionOutcome> CreateAsync(InitialAdminInput input, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            if (await db.Users.AnyAsync(user => user.RoleCode == "ADMIN" && user.IsActive, ct))
                return InitialAdminProvisionOutcome.ActiveAdminExists;

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = input.Email,
                Email = input.Email,
                EmailConfirmed = true,
                DisplayName = input.DisplayName,
                RoleCode = "ADMIN",
                IsActive = true,
                LockoutEnabled = true
            };
            var created = await users.CreateAsync(user, input.Password);
            if (!created.Succeeded) return InitialAdminProvisionOutcome.Rejected;

            db.UserPreferences.Add(new UserPreference
            {
                UserId = user.Id,
                SystemNoticeMailEnabled = true,
                FavoriteToolNoticeMailEnabled = true
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return InitialAdminProvisionOutcome.Created;
        }
        catch (DbUpdateException)
        {
            return InitialAdminProvisionOutcome.Failed;
        }
    }
}

/// <summary>対話入力と結果表示を抽象化し、秘密値をログやコマンド引数へ渡しません。</summary>
public interface IInitialAdminConsole
{
    /// <summary>利用者へ秘密を含まない案内を表示します。</summary>
    void WriteLine(string message);
    /// <summary>メールアドレスまたは表示名を一行読み取ります。</summary>
    string? ReadLine();
    /// <summary>画面へ表示せずパスワードを読み取ります。</summary>
    string ReadSecret();
}

/// <summary>標準コンソールで初期管理者の入力を安全に受け取ります。</summary>
public sealed class InitialAdminConsole : IInitialAdminConsole
{
    /// <summary>秘密を含まない案内だけを標準出力へ書き込みます。</summary>
    public void WriteLine(string message) => Console.WriteLine(message);

    /// <summary>通常の一行入力を標準入力から読み取ります。</summary>
    public string? ReadLine() => Console.ReadLine();

    /// <summary>リダイレクト入力を拒否し、IMEと貼り付けに対応した一行入力を画面へ表示せず読み取ります。</summary>
    public string ReadSecret()
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("パスワードは対話コンソールから入力してください。");
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("パスワード入力はWindowsコンソールで実行してください。");

        var inputHandle = NativeMethods.GetStdHandle(NativeMethods.StandardInputHandle);
        if (inputHandle == nint.Zero || inputHandle == new nint(-1)
            || !NativeMethods.GetConsoleMode(inputHandle, out var originalMode))
        {
            throw new InvalidOperationException("コンソールの入力状態を取得できませんでした。");
        }

        try
        {
            if (!NativeMethods.SetConsoleMode(inputHandle, originalMode & ~NativeMethods.EnableEchoInput))
                throw new InvalidOperationException("コンソールの入力表示を無効にできませんでした。");

            var value = Console.ReadLine() ?? throw new InvalidOperationException("パスワードを読み取れませんでした。");
            Console.WriteLine();
            return value;
        }
        finally
        {
            NativeMethods.SetConsoleMode(inputHandle, originalMode);
        }
    }

    /// <summary>Windowsコンソールの入力表示を制御するネイティブAPIを提供します。</summary>
    private static class NativeMethods
    {
        internal const int StandardInputHandle = -10;
        internal const uint EnableEchoInput = 0x0004;

        /// <summary>標準入力のWindowsハンドルを取得します。</summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GetStdHandle(int standardHandle);

        /// <summary>Windowsコンソールの現在の入力モードを取得します。</summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool GetConsoleMode(nint consoleHandle, out uint mode);

        /// <summary>Windowsコンソールの入力モードを設定します。</summary>
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool SetConsoleMode(nint consoleHandle, uint mode);
    }
}

/// <summary>初期管理者作成の明示コマンドを対話形式で実行します。</summary>
public sealed class InitialAdminBootstrapCommand(IInitialAdminProvisioner provisioner, IPasswordPolicy passwords, IInitialAdminConsole console)
{
    /// <summary>Web起動と区別する導入用コマンド名です。</summary>
    public const string Name = "bootstrap-admin";

    /// <summary>引数が初期管理者作成だけを明示しているか判定します。</summary>
    public static bool IsRequested(string[] args) => args.Length == 1 && args[0] == Name;

    /// <summary>有効ADMINの不存在と入力条件を確認してから作成処理を一度だけ呼び出します。</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        bool hasActiveAdmin;
        try { hasActiveAdmin = await provisioner.HasActiveAdminAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            console.WriteLine("初期管理者の作成条件を確認できませんでした。DB接続とテーブル状態を確認してください。");
            return 1;
        }
        if (hasActiveAdmin)
        {
            console.WriteLine("有効な管理者が既に存在するため、初期管理者は作成できません。");
            return 2;
        }

        console.WriteLine("管理者のメールアドレスを入力してください。");
        var email = console.ReadLine();
        console.WriteLine("管理者の表示名を入力してください。");
        var displayName = console.ReadLine();
        string? password = null;
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                console.WriteLine("パスワードを入力してください（IME・貼り付けが使えます。入力内容は表示されません）。");
                var candidate = console.ReadSecret();
                console.WriteLine("確認のため、同じパスワードをもう一度入力してください。");
                var confirmation = console.ReadSecret();
                if (candidate == confirmation)
                {
                    password = candidate;
                    break;
                }
                console.WriteLine(attempt < 3
                    ? "パスワードと確認入力が一致しません。もう一度入力してください。"
                    : "パスワードと確認入力が一致しません。");
            }
        }
        catch (InvalidOperationException)
        {
            console.WriteLine("パスワードは対話コンソールから入力してください。");
            return 3;
        }

        var normalizedEmail = email?.Trim();
        var normalizedDisplayName = displayName?.Trim();
        if (!CommonValidation.IsEmail(normalizedEmail) || normalizedEmail!.Length > 256)
        {
            console.WriteLine("メールアドレスの形式が正しくありません。");
            return 3;
        }
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length > 100)
        {
            console.WriteLine("表示名は1文字以上100文字以内で入力してください。");
            return 3;
        }
        if (password is null) return 3;
        if (!passwords.Validate("Password", password).IsValid)
        {
            console.WriteLine("パスワードが設定条件を満たしていません。");
            return 3;
        }

        InitialAdminProvisionOutcome outcome;
        try { outcome = await provisioner.CreateAsync(new InitialAdminInput(normalizedEmail, normalizedDisplayName, password), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { outcome = InitialAdminProvisionOutcome.Failed; }
        console.WriteLine(outcome switch
        {
            InitialAdminProvisionOutcome.Created => "初期管理者を作成しました。",
            InitialAdminProvisionOutcome.ActiveAdminExists => "有効な管理者が既に存在するため、初期管理者は作成できません。",
            InitialAdminProvisionOutcome.Rejected => "入力内容を登録できませんでした。メールアドレスの重複などを確認してください。",
            _ => "初期管理者を作成できませんでした。DB接続とテーブル状態を確認してください。"
        });
        return outcome == InitialAdminProvisionOutcome.Created ? 0 : outcome == InitialAdminProvisionOutcome.ActiveAdminExists ? 2 : 1;
    }
}
