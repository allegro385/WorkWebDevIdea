namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>対話入力と結果表示を抽象化し、秘密値をログやコマンド引数へ渡しません。</summary>
public interface ILocalTestUserConsole
{
    /// <summary>利用者へ秘密を含まない案内を表示します。</summary>
    void WriteLine(string message);
    /// <summary>ログインID・メールアドレス・表示名・ロールを一行読み取ります。</summary>
    string? ReadLine();
    /// <summary>画面へ表示せずパスワードを読み取ります。</summary>
    string ReadSecret();
}

/// <summary>標準コンソールで試験用ユーザーの入力を安全に受け取ります。</summary>
public sealed class LocalTestUserConsole : ILocalTestUserConsole
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
