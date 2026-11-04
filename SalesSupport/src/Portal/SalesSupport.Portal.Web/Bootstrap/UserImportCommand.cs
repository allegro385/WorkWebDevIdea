using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>TSVファイルからユーザーを一括登録する管理コマンドです。開発・本番のどちらでも使用します。</summary>
public sealed class UserImportCommand(UserImportTsvReader reader, UserImportService service, IActivityLogger activity,
    IConnectionStringProvider connections, IOptions<CommonOptions> commonOptions)
{
    /// <summary>Web起動と区別するコマンド名です。</summary>
    public const string Name = "import-users";

    /// <summary>不正なオプションでもWebサーバーの起動へ進めず、このコマンドで検証します。</summary>
    /// <param name="args">起動時の引数です。</param>
    /// <returns>このコマンドの実行要求ならtrueです。</returns>
    public static bool IsRequested(string[] args) => args.Length > 0 && args[0] == Name;

    /// <summary>接続先を表示し、全行を検証してから確認のうえ全件を登録します。</summary>
    /// <param name="args">起動時の引数です。</param>
    /// <param name="input">本番の確認を読み取る入力です。</param>
    /// <param name="output">結果の表示先です。入力値と例外の内容は表示しません。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    /// <returns>成功0、保存失敗1、確認で中止2、引数・入力不備3を返します。</returns>
    public async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken ct = default)
    {
        var options = args.Skip(1).Where(x => x.StartsWith("--", StringComparison.Ordinal)).ToList();
        var files = args.Skip(1).Except(options).ToList();
        if (files.Count != 1 || options.Except(["--update", "--dry-run"]).Any())
        {
            output.WriteLine("使用方法：import-users <TSVファイル> [--update] [--dry-run]");
            return 3;
        }
        var dryRun = options.Contains("--dry-run");
        var environmentCode = commonOptions.Value.EnvironmentCode;
        var target = new SqlConnectionStringBuilder(connections.SalesSupportDatabase);
        output.WriteLine($"接続先：DB {target.InitialCatalog}（サーバー {target.DataSource}）、環境区分：{environmentCode}");

        var read = await ReadFileAsync(files[0], output, ct);
        if (read is null) return 3;
        if (read.Errors.Count != 0)
        {
            foreach (var error in read.Errors) output.WriteLine(error);
            output.WriteLine($"追加0件、更新0件、スキップ0件、エラー{read.Errors.Count}件。登録していません。");
            return 3;
        }

        UserImportPlan plan;
        int? failedLine;
        try
        {
            plan = await service.PlanAsync(read.Rows, options.Contains("--update"), ct);
            foreach (var error in plan.Errors) output.WriteLine(error);
            output.WriteLine($"追加{plan.Added}件、更新{plan.Updated}件、スキップ{plan.Skipped}件、エラー{plan.Errors.Count}件");
            if (plan.Errors.Count != 0)
            {
                output.WriteLine("登録していません。");
                return 3;
            }
            if (dryRun) return 0;
            if (plan.Changes.Count == 0)
            {
                output.WriteLine("登録する内容はありません。");
                return 0;
            }
            if (environmentCode == "PRODUCTION")
            {
                output.WriteLine("本番環境へ登録します。続行する場合は y を入力してください。");
                if (await input.ReadLineAsync(ct) != "y")
                {
                    output.WriteLine("中止しました。登録していません。");
                    return 2;
                }
            }
            failedLine = await service.ApplyAsync(plan, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            output.WriteLine("処理に失敗しました。登録していません。DBの接続とテーブルの状態を確認してください。");
            return 1;
        }

        await RecordAsync(files[0], plan, failedLine, ct);
        if (failedLine is { } line)
        {
            output.WriteLine($"{line}行目を保存できませんでした。全件を取り消しました。入力内容とDBの状態を確認してください。");
            return 1;
        }
        output.WriteLine("登録しました。平文のパスワードを含むTSVは、確認後に削除してください。");
        return 0;
    }

    /// <summary>UTF-8のファイルを読み取ります。BOMの有無は問いません。</summary>
    /// <param name="path">TSVファイルのパスです。</param>
    /// <param name="output">読取り失敗の表示先です。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    /// <returns>検証結果です。読み取れないときはNULLです。</returns>
    private async Task<UserImportTsvResult?> ReadFileAsync(string path, TextWriter output, CancellationToken ct)
    {
        try
        {
            using var file = new StreamReader(path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            return await reader.ReadAsync(file, ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            output.WriteLine("TSVを読み取れませんでした。ファイルのパスとUTF-8の文字コードを確認してください。");
            return null;
        }
    }

    /// <summary>実行者・ファイル名・件数を操作ログへ記録します。ログ失敗で登録結果は変更しません。</summary>
    /// <param name="path">TSVファイルのパスです。フォルダーは記録しません。</param>
    /// <param name="plan">適用した登録計画です。</param>
    /// <param name="failedLine">失敗した行番号です。成功ならNULLです。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    private async Task RecordAsync(string path, UserImportPlan plan, int? failedLine, CancellationToken ct)
    {
        // 失敗時は全件を取り消すため、保存件数は0として記録します。
        var saved = failedLine is null;
        await activity.WriteAsync(new ActivityEvent("USER_IMPORT", saved ? "SUCCESS" : "FAILURE", saved ? null : "INVALID_INPUT", Changes:
        [
            new("Operator", null, Environment.UserName),
            new("FileName", null, Path.GetFileName(path)),
            new("Added", null, (saved ? plan.Added : 0).ToString(CultureInfo.InvariantCulture)),
            new("Updated", null, (saved ? plan.Updated : 0).ToString(CultureInfo.InvariantCulture)),
            new("Skipped", null, plan.Skipped.ToString(CultureInfo.InvariantCulture))
        ]), ct);
    }
}
