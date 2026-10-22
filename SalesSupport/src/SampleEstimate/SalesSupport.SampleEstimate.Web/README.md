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

起動プロファイルは`standalone`（既定、Visual StudioのF5）と`portal`の2つです。`standalone`はPortal・DB・SMTP・共通設定ファイルなしで入力画面・計算・TSV出力を試せ、業務日付はJSTの現在日付を使います。公開状態欄は「単独開発（DB確認なし）」と表示し、DBログは保存しません。明細ファイルはCommonが拒否し、案件の保存・一覧（本人の案件を扱う機能）は画面とサーバーの両方で利用できません。[単独開発の設定手順](../../../config/README.md#ツールを単独で開発する)を参照してください。

DB機能・共有Cookieの開発確認には`portal`プロファイルを使い、Portalでログインした実ユーザーで確認します。開発用ユーザーはPortalの[開発限定コマンド](../../Portal/SalesSupport.Portal.Web/README.md#開発試験用のユーザーを追加する)で登録します。CommonのDB認証・サイト／ツール状態確認を通した後に、サンプルもDBの公開状態とコード表示名を取得します。本番で`Standalone`を指定すると起動を拒否します。

共通設定と秘密値の置き方は[設定手順](../../../config/README.md)に従います。接続文字列はCommonの`IConnectionStringProvider`から取得します。ローカルのToolId・表示名は`Properties/launchSettings.json`に設定済みです。SQLは[サンプル専用手順](../../../sql/sample/README.md)に従い、開発専用DBへ手動適用してください。IISでは独立アプリケーション・プール、環境変数、同一サイト配下のURLを[導入・運用](../../../../設計書/05_導入・運用.md)に従って用意します。

```text
dotnet build SalesSupport/SampleEstimate.slnx
dotnet test SalesSupport/SampleEstimate.slnx
dotnet run --project SalesSupport/src/SampleEstimate/SalesSupport.SampleEstimate.Web --launch-profile standalone
```

テストはDB・SMTP・Portalへ接続しません。`EstimateServiceTests`は計算と入力検証、`SampleEstimateMappingTests`はサンプル専用表のEFマッピング、`ToolControllerLoggingTests`は実行ログの記録単位、`ToolStartupTests`は起動プロファイルと起動時の設定検証を確認します。Commonが組み込む要求パイプラインの確認はテンプレートのソリューションにあり、同じ内容をここへ複製していません。`portal`プロファイルを使い、実DBのSQL制約、Portalとの共有Cookie往復、IIS配置、公開状態、アップロード条件のDB取得は別途結合検証してください。機能追加の手順は[機能追加ガイド](機能追加ガイド.md)を参照してください。
