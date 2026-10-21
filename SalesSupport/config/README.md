# SalesSupport 共通設定ファイル

Commonが読み込む共通設定ファイルの雛形です。接続文字列を含むCommonの設定は、Portalと各ツールの`appsettings.json`ではなく配置環境ごとに1ファイルで管理し、Portalと各ツールはCommonのDIサービス（`IConnectionStringProvider`）から接続文字列を受け取ります。本番向けは`salessupport.common.sample.json`、開発向けは`salessupport.common.Development.sample.json`です。いずれも実値は含みません。

開発用ユーザーのTSV雛形は[test-users.sample.tsv](test-users.sample.tsv)です。4列ヘッダーだけを含み、Commonの設定ファイルとしては使用しません。[開発限定の取込コマンド](../src/Portal/SalesSupport.Portal.Web/README.md#tsvで複数の試験ユーザーを追加する)の手順で利用してください。

## 配置

1. 環境に合う雛形をコピーし、下記の既定の場所へ置きます。ファイルは**Web公開領域とアプリの配置フォルダーの外**です。
2. 下表の「導入時に設定」の項目へ実値を記入します。未設定のままでは起動時に構成エラーで停止し、既定値での代替は行いません。
3. Portalと各Webツールは、各アプリのコンテンツルートから既定の相対パス`../../config/salessupport.common.json`でこのファイルを読みます。標準配置ではパスの指定は不要です。配置が異なる場合だけ、IISアプリケーションの環境変数`SalesSupport__CommonConfigPath`（`web.config`の`<environmentVariables>`）で相対パスを与えます。全アプリが同じファイルを指します。
4. ファイルのACLは、対象アプリケーションプールの読取りと運用管理者だけに限定します。Data Protectionの鍵フォルダーと同じ扱いです。

| 用途 | 既定の場所 | 読み込むアプリ |
| --- | --- | --- |
| IIS配置（開発サーバー・本番） | `<ルート>\config\salessupport.common.json` | `<ルート>\apps\<アプリ名>\`に配置したPortal・Webツール。パス指定不要 |
| ローカルの連携試験 | リポジトリの`SalesSupport\config\salessupport.common.Development.json` | Portalの`https`、各ツールの`portal`起動プロファイル。プロファイルが`SalesSupport__CommonConfigPath=../../../config/salessupport.common.Development.json`を指定済み |

Webツールの[単独開発](#ツールを単独で開発する)ではこのファイルを読みません。親フォルダーを順に探す処理はなく、読込先は一意です。ファイルがない場合や書式不正の場合は、解決後のファイルパスと理由を表示して起動を停止します（設定値は表示しません）。

配置手順と確認項目は[導入・運用](../../設計書/05_導入・運用.md#deployment)、キーの一覧と検証条件は[Common詳細設計 第4節](../../設計書/10_Common詳細設計.md)を正とします。

## パスの指定方法

パスはすべて相対パスで指定します。絶対パスは起動時に構成エラーとして拒否します。基準となるフォルダーは次のとおりです。

| 対象 | 基準フォルダー |
| --- | --- |
| 環境変数`SalesSupport__CommonConfigPath`（既定`../../config/salessupport.common.json`） | 各アプリのコンテンツルート。IISでは配置フォルダー、Visual Studio・`dotnet run`ではプロジェクトフォルダー、`dotnet <アプリ>.dll`では現在のフォルダー |
| このファイル内のフォルダー（`KeyDirectory`、`Storage:*Root`） | このファイルがあるフォルダー |

雛形の値は次の配置を前提にしています。配置が違う場合は相対パスを読み替えてください。

```text
D:\SalesSupport\
├─ apps\portal\      Portalの発行成果物（IISアプリケーション）
├─ apps\T001\        Webツールの発行成果物（IISアプリケーション）
├─ config\           このファイル（salessupport.common.json）
├─ keys\             Data Protectionキー
└─ storage\temp\, storage\files\   一時・永続の保存領域
```

リポジトリでも同じ相対関係になり、ローカルの連携試験では`SalesSupport\keys\`と`SalesSupport\storage\`を使います。いずれも`.gitignore`で除外しています。JSONでは`\`を`\\`と書く必要があるため、雛形では`/`を使用しています（`/`はWindowsでもそのまま使えます）。

## 設定の優先順位

共通設定ファイル → 環境変数 の順で適用し、環境変数が優先します。アプリごとに異なる値は共通設定ファイルへ書かず、各アプリの環境変数で与えます。

| 区分 | 与え方 | 例 |
| --- | --- | --- |
| 全アプリ共通 | 共通設定ファイル | `ConnectionStrings:SalesSupport`、`SalesSupport:Mail:*` |
| アプリ固有 | 各アプリの環境変数 | `SalesSupport__Application__ToolId`（Webツールごとに必須）、`SalesSupport__Application__Name`（ログに表示するアプリ名） |
| アプリ固有の設定 | 各アプリの`appsettings.json`・環境変数 | Portalの`Portal:SupportContact`、`SalesSupport:RateLimits:*`等 |

設定の変更はアプリの再起動で反映します。Commonは実行中に設定ファイルを再読込しません。共通設定ファイルを変更した場合はPortalと全Webツールを再起動します。

## 雛形の値

| キー | 雛形の値 | 区分 |
| --- | --- | --- |
| `ConnectionStrings:SalesSupport` | 空 | 必須 |
| `SalesSupport:Mail:Host` / `From` | 空 | 導入時に設定（メールを使う場合） |
| `SalesSupport:Mail:MaxRecipients` | `0` | 導入時に設定。実SMTPの上限を確認して決めます |
| `SalesSupport:Mail:UserName` / `Password` | 空 | SMTP認証が必要な場合だけ一組で設定 |
| `SalesSupport:Mail:DevelopmentRecipient` | 空 | `EnvironmentCode`が`DEVELOPMENT`のとき必須 |
| `SalesSupport:Mail:Port` / `TlsMode` | `587` / `StartTls` | 一般的な送信用の組合せ。実SMTPに合わせて確認 |
| `Portal:EnvironmentCode` | `PRODUCTION` | 開発環境では`DEVELOPMENT`へ変更 |
| `SalesSupport:Application:Name` | `営業支援ポータル` | 画面・メールの表示名 |
| `SalesSupport:Portal:BaseUrl` | `https://sales-support/` | 実URLに合わせて変更 |
| `SalesSupport:DataProtection:KeyDirectory` | `../keys` | 配置に合わせて変更 |
| `SalesSupport:Storage:TemporaryRoot` / `PermanentRoot` | `../storage/temp` / `../storage/files` | 配置に合わせて変更。空にするとその保存領域を提供しません |
| `SalesSupport:Storage:CleanupTimeoutSeconds` | `5` | 既定値 |
| `SalesSupport:Mail:TimeoutSeconds` | `30` | 既定値 |
| `SalesSupport:Http:TimeoutSeconds` | `30` | 既定値 |
| `SalesSupport:Logging:TimeoutSeconds` | `3` | 既定値 |
| `SalesSupport:Proxy:KnownProxies` | `[]` | 既定値。空では転送ヘッダーを採用しません |

開発向け雛形は`EnvironmentCode=DEVELOPMENT`、PortalのURLを`https://localhost:7065/`、SMTPを`127.0.0.1:2525`にした例です。SMTPサーバーを別途用意しない限りメールは送れません。PortalとDBを使う検証には、空の接続文字列へ使い捨ての開発用DBを指定してください。

## ツールを単独で開発する

Webツールの通常のデバッグ実行（Visual StudioのF5）は「単独開発」です。テンプレートから生成したツールとサンプルは、既定の起動プロファイル`standalone`で起動すると、**Portal・DB・SMTP・この共通設定ファイル・鍵フォルダー・保存フォルダーなしで**入力画面・計算・TSV出力を試せます。

| 起動プロファイル | `SalesSupport:Tool:ExecutionMode` | 用途 |
| --- | --- | --- |
| `standalone`（既定） | `Standalone` | 画面・計算・出力の開発。共通設定ファイルを読まない |
| `portal` | `Portal`（明示） | Portalでログインした実ユーザーでの結合試験。この共通設定ファイルと開発用DBが必要 |

- モードはホスト設定（起動プロファイルの環境変数`SalesSupport__Tool__ExecutionMode`）で選びます。この共通設定ファイルには書きません。未設定は`Portal`です。
- `portal`プロファイルは`Portal`を明示し、親プロセスの環境変数やアプリ設定に残った`Standalone`を上書きします。
- `Standalone`はホストの`ASPNETCORE_ENVIRONMENT=Development`の場合だけ受け付けます。それ以外の環境とPortalでは起動を拒否します。連携モードで共通設定に不備があっても、単独開発へ自動的に切り替えません。
- 単独開発では共有Cookie・ログイン・OTPを使わず、認証ユーザーや仮ユーザーを作りません。自端末（`localhost`）からの対象ツールの画面だけを利用でき、他の端末からの要求は拒否します。サイト・ツールの公開状態とロールは確認しません。
- CSRF検証は有効です。暗号鍵はASP.NET Core標準の保存先（開発者のユーザープロファイル）を使います。
- DBを使いません。業務日付は現在のJST日付で、ログはDBへ保存せず、障害の詳細はコンソールへ出力します。明細ファイルのアップロード、サンプルの案件保存・検索・更新など、DBや認証ユーザーが必要な機能は利用できません。`portal`プロファイルで確認してください。
- 共通ヘッダーにはPortalへのリンクとログアウトを表示せず、「単独開発」と表示します。パンくずの「トップ」「ツール一覧」もリンクにしません。

連携試験に使う開発用ユーザーは、Portalの[開発限定コマンド](../src/Portal/SalesSupport.Portal.Web/README.md#開発試験用のユーザーを追加する)で登録します。

`SalesSupport:Mail:Enabled`と`SalesSupport:Application:ToolId`は雛形へ含めていません。`Enabled`は設定読み込み後、CommonがPortalで有効・ツールで無効に固定します。各ツールはメールを使用せず、共通設定ファイルや環境変数で`Enabled`を指定しても変更できません。`ToolId`はツールごとに異なるため環境変数で与えます。雛形の`Application:Name`はPortal用です。各ツールでは`SalesSupport__Application__Name`をそのツール名に上書きしてください。これを省くと障害ログの`ApplicationName`がPortal名になります。

共通設定ファイルをWeb公開領域・アプリ配置フォルダーの外へ置くこととアクセス権は、導入時に運用手順で確認します。Commonはファイルの存在と相対パスを検証しますが、配置フォルダーとの包含関係は起動時に検査しません。

## 秘密情報の取扱い

- 接続文字列、SMTPの資格情報を含むため、実値を記入したファイルをリポジトリへ追加しないでください。`salessupport.common*.json`は2つの雛形を除き`.gitignore`で除外しています。名前を変えた実ファイルも追加前に内容を確認してください。
- 実値を作業記録、ログ、画面、メールへ出力しないでください。
- 開発環境でもこの雛形をコピーして使用します。ローカルの実値は`.gitignore`で除外した`SalesSupport/config/salessupport.common.Development.json`へ置き、コミットしないでください。
