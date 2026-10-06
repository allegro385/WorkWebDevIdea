using System.Text;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>開発環境でだけTSVの標準入力から複数の試験ユーザーを追加します。</summary>
public sealed class LocalTestUserImportCommand(IHostEnvironment environment, IOptions<CommonOptions> commonOptions,
    LocalTestUserTsvReader reader, ILocalTestUserProvisioner provisioner, ILocalTestUserConsole console)
{
    /// <summary>本番の連携処理および対話入力の単一ユーザー作成と区別するコマンド名です。</summary>
    public const string Name = "import-test-users";

    /// <summary>不正なオプションでもWebサーバー起動へ進めず、取込コマンドで検証します。</summary>
    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    /// <summary>環境と引数を確認して全行を検証し、一人ずつ保存して最初の失敗で停止します。</summary>
    /// <returns>成功0、保存失敗1、開発環境以外2、引数またはTSV不備3を返します。</returns>
    public async Task<int> RunAsync(string[] args, TextReader input, bool isInputRedirected, CancellationToken ct = default)
    {
        if (!environment.IsDevelopment() || commonOptions.Value.EnvironmentCode != "DEVELOPMENT")
        {
            console.WriteLine("TSVによる試験用ユーザーの追加は開発環境でだけ実行できます。");
            return 2;
        }
        var roleCode = args.Length == 3 && args[1] == "--role" ? args[2] : "A";
        if (!IsRequested(args) || (args.Length != 1 && (args.Length != 3 || args[1] != "--role"))
            || roleCode is not ("A" or "B" or "C" or "D" or "ADMIN"))
        {
            console.WriteLine("使用方法：import-test-users [--role A|B|C|D|ADMIN]。TSVはUTF-8の標準入力で渡してください。");
            return 3;
        }
        if (!isInputRedirected)
        {
            console.WriteLine("TSVファイルをUTF-8の標準入力へパイプしてください。");
            return 3;
        }

        LocalTestUserTsvResult result;
        try { result = await reader.ReadAsync(input, roleCode, ct); }
        catch (Exception exception) when (exception is IOException or DecoderFallbackException)
        {
            console.WriteLine("TSVを読み取れませんでした。UTF-8の文字コードと標準入力を確認してください。");
            return 3;
        }
        if (result.ErrorMessage is { } error)
        {
            console.WriteLine(error);
            return 3;
        }

        var created = 0;
        foreach (var row in result.Rows)
        {
            LocalTestUserOutcome outcome;
            try { outcome = await provisioner.CreateAsync(row.User, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { outcome = LocalTestUserOutcome.Failed; }
            if (outcome != LocalTestUserOutcome.Created)
            {
                console.WriteLine(outcome == LocalTestUserOutcome.Rejected
                    ? $"{row.LineNumber}行目：登録を拒否しました。既存のログインID・メールアドレスやIdentityの入力条件を確認してください。"
                    : $"{row.LineNumber}行目：保存に失敗しました。開発用DBの接続とテーブル状態を確認してください。");
                console.WriteLine($"登録済み{created}件。後続の処理を停止しました。登録状況を確認し、未登録行だけ再実行してください。");
                return 1;
            }
            created++;
        }
        console.WriteLine($"試験用ユーザー{created}件を登録しました（ロール：{roleCode}、通知設定：OFF）。");
        return 0;
    }
}
