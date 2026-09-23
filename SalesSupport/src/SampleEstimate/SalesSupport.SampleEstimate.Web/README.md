# 見積試算・案件記録サンプル

ToolIdは`SAMPLE-ESTIMATE`、PortalからのURLは`/tools/SAMPLE-ESTIMATE/app`です。テンプレート生成スクリプトで作った独立Webアプリを題材に、Commonの認証・UI・一時ファイル・ログと、ツール専用EF Coreの使い分けを示します。実業務データは入力しないでください。

## 画面と保存範囲

入力画面で案件名、数量、単価、適用日、区分、任意の`.tsv`明細を指定します。「画面表示」「TSV直接ダウンロード」「要約を案件として保存」から選びます。通常の割引は0%、キャンペーンは10%で円未満切捨てです。ファイルの非空行数のみ計算結果へ載せます。元ファイルと出力TSVは保存しません。

保存した本人の案件は`/records`で適用日・区分検索し、該当全件の件数と合計を表示します。一覧は最新100件です。詳細と編集は本人の案件だけを扱います。編集時は再計算して保存し、DB監査列の`UpdateCount`が変わっていれば上書きせず開き直しを促します。編集にはファイルを再送しないため、更新後の明細行数はNULLに戻します。同じ保存フォームの再送信は送信IDの一意制約と結果GETへのリダイレクトで重複を抑えます。

| 層 | 読む場所 |
| --- | --- |
| HTTP・画面遷移 | `Controllers/ToolController.cs`、`Controllers/RecordsController.cs` |
| 入力・計算・検証 | `Models/EstimateModels.cs`、`Services/EstimateService.cs` |
| 検索・保存・更新 | `Data/SampleEstimateDbContext.cs`、`Services/EstimateRecordService.cs` |
| 画面 | `Views/Tool/`、`Views/Records/` |
| 共通契約 | `Bootstrap/ToolServiceExtensions.cs`、`Services/ToolStatusService.cs`、Commonの認証・UI・CodeMaster・FileStorage・Logging |

## 準備と検証

共通設定と秘密値の置き方は[設定手順](../../../config/README.md)に従います。接続文字列はCommonの`IConnectionStringProvider`から取得します。ローカルのToolId・表示名は`Properties/launchSettings.json`に設定済みです。SQLは[サンプル専用手順](../../../sql/sample/README.md)に従い、開発専用DBへ手動適用してください。IISでは独立アプリケーション・プール、環境変数、同一サイト配下のURLを[導入・運用](../../../../設計書/05_導入・運用.md)に従って用意します。

```text
dotnet build SalesSupport/SampleEstimate.slnx
dotnet test SalesSupport/SampleEstimate.slnx
dotnet run --project SalesSupport/src/SampleEstimate/SalesSupport.SampleEstimate.Web --launch-profile https
```

単体テストはDB・SMTPへ接続しません。実DBのSQL制約、Portalとの共有Cookie往復、IIS配置、公開状態、アップロード条件のDB取得は別途結合検証してください。機能追加の手順は[機能追加ガイド](機能追加ガイド.md)を参照してください。
