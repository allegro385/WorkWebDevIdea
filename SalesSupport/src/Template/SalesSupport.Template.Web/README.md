# SalesSupport.Template.Web

新しいWebツールの開発を始めるためのテンプレートツールです。方針は[サンプル・テンプレートツール設計方針](../../../../設計書/12_サンプル・テンプレートツール設計方針.md)、共通契約は[Common詳細設計](../../../../設計書/10_Common詳細設計.md)を正とします。

このツール自体は業務用ではありません。複製して題材を差し替えて使います。ツール固有のDBテーブルは持ちません。

## テンプレートに含まれるもの

| 区分 | 内容 |
| --- | --- |
| 起動 | `AddSalesSupportCommon(ApplicationKind.Tool, environment, configuration)`による起動モードの確定、共通設定の検証・DI・認証・認可・ログの組込み（`Bootstrap/ToolServiceExtensions.cs`）。起動プロファイルは`standalone`（既定）と`portal` |
| パイプライン | Commonの`UseSalesSupportTool()`が転送ヘッダー、セキュリティヘッダー、共通エラー応答、HTTPS、静的資産、認証・認可、共通ログアウトを組み込む（`Program.cs`） |
| 利用制御 | 共通の既定認可方針による共有Cookieの現行ユーザー・サイト公開状態・ツール状態の確認。ツール側に独自の認証画面を持ちません |
| 画面 | 共通レイアウト・ヘッダー・パンくず・検証表示を使う入力画面と結果画面（`Views/Tool/TemplateExampleIndex.cshtml`、`TemplateExampleResult.cshtml`） |
| 入力例 | 文字列・数値・日付・ドロップダウン・ラジオボタン・ファイル選択（`TemplateExample/ExampleModels.cs`） |
| 実行 | CSRF付きPOST。ControllerはHTTPと画面遷移、Serviceが検証・計算・出力（`Controllers/ToolController.cs`、`TemplateExample/ExampleService.cs`） |
| ファイル | `TOOL`＋`TOOL_INPUT`のアップロード条件をCommonから取得して検証し、一時領域だけへ保存。成否にかかわらず削除 |
| ログ | 入口で`WEB_OPEN`、実行で`WEB_EXECUTE`（成功・失敗）をCommon経由で一度ずつ記録 |
| 出力 | 画面表示と、同じPOST応答でのTSV直接ダウンロード（Commonの区切りテキスト処理・数式対策） |

含めていないもの：ツール固有のDBテーブルとDbContext、結果の保存・再取得、メール送信、独自のセッション延長処理、入力項目を動的生成する汎用フォーム基盤。

## 差し替える前提の部分

計算の題材（案件名・数量・単価・区分による概算計算）は、動作と実装例を示すための仮の内容です。題材は`TemplateExample`へ隔離してあり、**生成したツールでは削除する前提**です。残したまま機能を足すとビルドで検出されます。条件は[題材の隔離と残存の検出](../../../../設計書/12_サンプル・テンプレートツール設計方針.md#題材の隔離と残存の検出)を正とします。

| 区分 | 対象 |
| --- | --- |
| 題材（削除する） | `TemplateExample/ExampleModels.cs`、`TemplateExample/ExampleService.cs`、`Views/Tool/TemplateExampleIndex.cshtml`、`Views/Tool/TemplateExampleResult.cshtml`、`wwwroot/css/site.css`の題材向け指定 |
| 骨組み（そのまま使う） | `Program.cs`、`Bootstrap/ToolServiceExtensions.cs`、`Controllers/ToolController.cs`、`Views/_ViewImports.cshtml`、`Views/_ViewStart.cshtml`、`.csproj`、`Properties/launchSettings.json` |

生成したツールでの差し替え手順です。[開発サイクル](../../../../AGENTS.md#開発方針作業サイクル)どおり資料更新→テスト追加→最小実装の順で進めます。

1. `TemplateExample/`と`Views/Tool/TemplateExample*.cshtml`を削除する。
2. 自分の題材の入力モデル・Service・Viewを`Models/`・`Services/`・`Views/Tool/`へ作る。
3. `Bootstrap/ToolServiceExtensions.cs`のServiceの登録を自分のServiceへ差し替える。一時アップロードを使わないツールでは`RequireTemporaryStorage()`も外す。
4. `Controllers/ToolController.cs`の本体と、View名を保持する定数`ExampleIndexView`を自分の題材へ書き替える。題材を削除するとコンパイルが通らないため、この手順を飛ばせません。

`Controllers/ToolController.cs`のログ記録（`WEB_OPEN`・`WEB_EXECUTE`）、`IToolPageShell`の利用、CSRFとモデル検証の作法、認可の既定方針は、ツールをまたいで同じ運用にするため残してください。`WEB_OPEN`は入口アクションへCommonの`WebOpenLoggingFilter`を`TypeFilter`で適用して記録します。区分の選択肢をコードマスタで管理する場合は`ICodeMasterReader`へ差し替えます。

### 題材が残っているときのビルド

`.csproj`の`CheckTemplateExample`ターゲットが`TemplateExample/**/*`と`Views/Tool/TemplateExample*.cshtml`を検出します。

| 構成 | 題材が残っている場合 |
| --- | --- |
| Debug（`standalone`のF5を含む） | 警告。生成直後から動かして確認できます |
| Release（`dotnet publish -c Release`） | エラー。[手動リリース](../../../../設計書/05_導入・運用.md#release)の発行手順で必ず止まります |
| Release（`dotnet publish -c Release --no-build`） | エラー。ビルド済みの出力を再発行する経路も塞ぎます |

検査はコンパイルの前に動くため、Releaseで止まったときに題材を含むDLLは作られません。

このテンプレート自身は題材を持つことが目的のため、`.csproj`の`TemplateExampleAllowed`を`true`にしてターゲットを無効にしています。生成スクリプトがこの値を`false`へ置き換え、生成したツールでだけ検出が有効になります。

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
| 起動プロファイル（`standalone`・`portal`）のToolId・アプリ名 | `__TOOL_ID__` / `__TOOL_NAME__` |
| 起動プロファイルのポート | `7075` / `5275` |
| 題材の残存検出を有効にする`.csproj`のプロパティ | `TemplateExampleAllowed`（テンプレートは`true`、生成後は`false`） |

`ToolId`は配置設定であり、ソースへ書きません。`Properties/launchSettings.json`の`__TOOL_ID__`・`__TOOL_NAME__`はローカル起動用のプレースホルダーで、生成時に実値へ置き換わります。`portal`プロファイルでテンプレートのまま起動すると、対応するツールがDBに存在しないため利用制御で拒否されます。

## 実行に必要な設定

起動モードは2つあり、Visual Studioの起動プロファイルで選びます。生成直後のF5は`standalone`です。詳細は[共通設定の単独開発手順](../../../config/README.md#ツールを単独で開発する)を参照してください。

| 起動プロファイル | 用途 | 必要なもの |
| --- | --- | --- |
| `standalone`（既定） | 画面・計算・TSV出力の開発 | なし（Portal・DB・SMTP・共通設定ファイル・鍵／保存フォルダーは不要） |
| `portal` | Portalでログインした実ユーザーでの結合試験 | Portalの起動、`SalesSupport/config/salessupport.common.Development.json`、開発用DB、DB上のツール行・ロール割当て・アップロード条件 |

単独開発では明細ファイルのアップロードをCommonが拒否し、画面の入力欄も無効になります。業務日付は現在のJST日付で、ログはDBへ保存しません。CSRF検証は有効です。

ツール固有の値は各アプリの環境変数で与えます。ローカルでは起動プロファイルに設定済みです。

| 環境変数 | 内容 |
| --- | --- |
| `SalesSupport__Application__ToolId` | このアプリが対応するツールのID。Portalのツールマスタに登録済みのWEBツールと一致させます |
| `SalesSupport__Application__Name` | 障害ログへ記録するアプリ名 |
| `SalesSupport__Tool__ExecutionMode` | `standalone`は`Standalone`、`portal`は`Portal`を明示。本番では設定しません |
| `SalesSupport__CommonConfigPath` | 連携モードの共通設定ファイルへの、コンテンツルートからの相対パス。標準配置（IIS）では不要で、`portal`プロファイルはローカル用の値を設定済み |

連携モードの共通設定ファイルでは`SalesSupport:Storage:TemporaryRoot`が必要です。明細ファイルのアップロードを使うため、未設定の場合は起動時に構成エラーで停止します。DB側には、対象ToolIdのツール行（WEBツール・起動URL）、ロール割当てと、`TOOL`＋`TOOL_INPUT`のアップロード条件が必要です。登録は[導入・運用](../../../../設計書/05_導入・運用.md)に従い、所定のSQLで手動適用します。アプリは起動時にDBを更新しません。本番で`Standalone`を指定すると起動を拒否します。

Data ProtectionキーはWindows DPAPIで保護するため、起動できるのはWindowsだけです。Linuxではビルドと単体テストのみ実行できます。連携モードで起動する結合テストはWindows以外ではスキップします。

## 検証

リポジトリルートから実行します。使用するSDKは`SalesSupport/global.json`で10.0.401に固定しています。

```text
dotnet build SalesSupport/template.slnx
dotnet test SalesSupport/template.slnx
dotnet run --project SalesSupport/src/Template/SalesSupport.Template.Web --launch-profile standalone
```

テストはDB・SMTP・Portalへ接続しません。`ToolPipelineTests`は実際の`Program.cs`をテストサーバーで起動し、連携モードで未認証の画面要求がPortalのログインへ誘導されること、単独開発でCSRFトークンのないPOSTが400になることを確認します。共有Cookieでの入場、サイト・ツール状態による利用制御、アップロード条件の取得、ログ記録は`portal`プロファイルで実DBとPortalを併用した環境での確認が必要です。
