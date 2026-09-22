# SalesSupport.Template.Web

新しいWebツールの開発を始めるためのテンプレートツールです。方針は[サンプル・テンプレートツール設計方針](../../../../設計書/12_サンプル・テンプレートツール設計方針.md)、共通契約は[Common詳細設計](../../../../設計書/10_Common詳細設計.md)を正とします。

このツール自体は業務用ではありません。複製して題材を差し替えて使います。ツール固有のDBテーブルは持ちません。

## テンプレートに含まれるもの

| 区分 | 内容 |
| --- | --- |
| 起動 | `AddSalesSupportCommon(ApplicationKind.Tool)`による共通設定の検証・DI・認証・認可・ログの組込み（`Bootstrap/ToolServiceExtensions.cs`） |
| パイプライン | 転送ヘッダー、共通エラー応答、HTTPS、静的資産、認証・認可、共通ログアウト（`Program.cs`） |
| 利用制御 | 共通の既定認可方針による共有Cookieの現行ユーザー・サイト公開状態・ツール状態の確認。ツール側に独自の認証画面を持ちません |
| 画面 | 共通レイアウト・ヘッダー・パンくず・検証表示を使う入力画面と結果画面（`Views/Tool/`） |
| 入力例 | 文字列・数値・日付・ドロップダウン・ラジオボタン・ファイル選択（`Models/EstimateModels.cs`） |
| 実行 | CSRF付きPOST。ControllerはHTTPと画面遷移、Serviceが検証・計算・出力（`Controllers/ToolController.cs`、`Services/EstimateService.cs`） |
| ファイル | `TOOL`＋`TOOL_INPUT`のアップロード条件をCommonから取得して検証し、一時領域だけへ保存。成否にかかわらず削除 |
| ログ | 入口で`WEB_OPEN`、実行で`WEB_EXECUTE`（成功・失敗）をCommon経由で一度ずつ記録 |
| 出力 | 画面表示と、同じPOST応答でのTSV直接ダウンロード（Commonの区切りテキスト処理・数式対策） |

含めていないもの：ツール固有のDBテーブルとDbContext、結果の保存・再取得、メール送信、独自のセッション延長処理、入力項目を動的生成する汎用フォーム基盤。

## 差し替える前提の部分

計算の題材（案件名・数量・単価・区分による概算計算）は、動作と実装例を示すための仮の内容です。[設計方針 第6節](../../../../設計書/12_サンプル・テンプレートツール設計方針.md)の未決定事項であり、恒久仕様ではありません。

| 差し替える | そのまま使う（共通契約） |
| --- | --- |
| `Models/EstimateModels.cs`の入力項目と結果 | `PageShellModel`・共通レイアウト・検証表示の使い方 |
| `Services/EstimateService.cs`の検証条件と計算 | アップロード条件の取得、一時ファイルの保存・削除、区切りテキスト出力 |
| `Views/Tool/`の画面項目と文言 | フォームのCSRF、項目エラーの表示、再実行の案内 |
| 区分の固定選択肢 | コードマスタを使う場合は`ICodeMasterReader`へ差し替え |
| `wwwroot/css/site.css` | Commonの`ss-`クラスとBootstrapの読み込み（レイアウトが行います） |

`Controllers/ToolController.cs`のログ記録（`WEB_OPEN`・`WEB_EXECUTE`）と認可の既定方針は、ツールをまたいで同じ運用にするため残してください。

## 複製して新しいツールを作る

ToolIdを入力して生成スクリプトを実行します。手順と命名規則は[生成スクリプトのREADME](../../../scripts/README.md)を参照してください。

```text
SalesSupport\scripts\new-tool.bat T001
```

スクリプトは次の名称を置き換えます。手作業で複製する場合も同じ範囲を変更します。テンプレート自身のソースは変更しません。

| 置換対象 | テンプレートでの値 |
| --- | --- |
| ソリューション | `SalesSupport/template.slnx` |
| プロジェクトフォルダー | `SalesSupport/src/Template/SalesSupport.Template.Web` |
| プロジェクト名・ルート名前空間 | `SalesSupport.Template.Web` |
| テストプロジェクト | `SalesSupport/tests/SalesSupport.Template.Tests` |
| 起動プロファイルのToolId・アプリ名 | `__TOOL_ID__` / `__TOOL_NAME__` |
| 起動プロファイルのポート | `7075` / `5275` |

`ToolId`は配置設定であり、ソースへ書きません。`Properties/launchSettings.json`の`__TOOL_ID__`・`__TOOL_NAME__`はローカル起動用のプレースホルダーで、生成時に実値へ置き換わります。テンプレートのまま起動すると、対応するツールがDBに存在しないため利用制御で拒否されます。

## 実行に必要な設定

接続文字列・鍵・保存領域を含む共通設定は[共通設定ファイル](../../../config/README.md)で管理します。ツール固有の値は各アプリの環境変数で与えます。

| 環境変数 | 内容 |
| --- | --- |
| `SalesSupport__CommonConfigPath` | 共通設定ファイルへの、アプリの実行フォルダーからの相対パス |
| `SalesSupport__Application__ToolId` | このアプリが対応するツールのID。Portalのツールマスタに登録済みのWEBツールと一致させます |
| `SalesSupport__Application__Name` | 障害ログへ記録するアプリ名 |

共通設定ファイルでは`SalesSupport:Storage:TemporaryRoot`が必要です。明細ファイルのアップロードを使うため、未設定の場合は起動時に構成エラーで停止します。

DB側には、対象ToolIdのツール行（WEBツール・起動URL）と、`TOOL`＋`TOOL_INPUT`のアップロード条件が必要です。登録は[導入・運用](../../../../設計書/05_導入・運用.md)に従い、所定のSQLで手動適用します。アプリは起動時にDBを更新しません。

Data ProtectionキーはWindows DPAPIで保護するため、起動できるのはWindowsだけです。Linuxではビルドと単体テストのみ実行できます。

## 検証

リポジトリルートから実行します。使用するSDKは`SalesSupport/global.json`で10.0.401に固定しています。

```text
dotnet build SalesSupport/template.slnx
dotnet test SalesSupport/template.slnx
dotnet run --project SalesSupport/src/Template/SalesSupport.Template.Web --launch-profile https
```

単体テストはDB・SMTPへ接続しません。共有Cookieでの入場、サイト・ツール状態による利用制御、アップロード条件の取得、ログ記録は実DBとPortalを併用した環境での確認が必要です。ローカル起動には上記の環境変数と共通設定ファイルが必要です。
