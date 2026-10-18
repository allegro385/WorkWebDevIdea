# SalesSupport 共通設定ファイル

Commonが読み込む共通設定ファイルの雛形です。接続文字列を含むCommonの設定は、Portalと各ツールの`appsettings.json`ではなく配置環境ごとに1ファイルで管理し、Portalと各ツールはCommonのDIサービス（`IConnectionStringProvider`）から接続文字列を受け取ります。本番向けは`salessupport.common.sample.json`、開発向けは`salessupport.common.Development.sample.json`です。いずれも実値は含みません。

開発用ユーザーのTSV雛形は[test-users.sample.tsv](test-users.sample.tsv)です。4列ヘッダーだけを含み、Commonの設定ファイルとしては使用しません。[開発限定の取込コマンド](../src/Portal/SalesSupport.Portal.Web/README.md#tsvで複数の試験ユーザーを追加する)の手順で利用してください。

## 配置

1. 環境に合う雛形をコピーし、`salessupport.common.json`等の名前で**Web公開領域とアプリの配置フォルダーの外**へ置きます。
2. 下表の「導入時に設定」の項目へ実値を記入します。未設定のままでは起動時に構成エラーで停止し、既定値での代替は行いません。
3. Portalと各WebツールのIISアプリケーションへ、環境変数`SalesSupport__CommonConfigPath`でこのファイルへの**相対パス**を設定します（`web.config`の`<environmentVariables>`）。全アプリが同じファイルを指します。
4. ファイルのACLは、対象アプリケーションプールの読取りと運用管理者だけに限定します。Data Protectionの鍵フォルダーと同じ扱いです。

配置手順と確認項目は[導入・運用](../../設計書/05_導入・運用.md#deployment)、キーの一覧と検証条件は[Common詳細設計 第4節](../../設計書/10_Common詳細設計.md)を正とします。

## パスの指定方法

パスはすべて相対パスで指定します。絶対パスは起動時に構成エラーとして拒否します。基準となるフォルダーは次のとおりです。

| 対象 | 基準フォルダー |
| --- | --- |
| 環境変数`SalesSupport__CommonConfigPath` | 各アプリの実行フォルダー（発行成果物の置き場） |
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

この配置では、Portalとツールのどちらも環境変数の値は`..\..\config\salessupport.common.json`で共通です。JSONでは`\`を`\\`と書く必要があるため、雛形では`/`を使用しています（`/`はWindowsでもそのまま使えます）。

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
| `ConnectionStrings:SalesSupport` | 空 | 通常は必須。ツールで認証とツールマスタを両方省略した場合だけ未設定で可 |
| `SalesSupport:Mail:Host` / `From` | 空 | 導入時に設定（メールを使う場合） |
| `SalesSupport:Mail:MaxRecipients` | `0` | 導入時に設定。実SMTPの上限を確認して決めます |
| `SalesSupport:Mail:UserName` / `Password` | 空 | SMTP認証が必要な場合だけ一組で設定 |
| `SalesSupport:Mail:DevelopmentRecipient` | 空 | `EnvironmentCode`が`DEVELOPMENT`のとき必須 |
| `SalesSupport:Mail:Port` / `TlsMode` | `587` / `StartTls` | 一般的な送信用の組合せ。実SMTPに合わせて確認 |
| `Portal:EnvironmentCode` | `PRODUCTION` | 開発環境では`DEVELOPMENT`へ変更 |
| `SalesSupport:Tool:RequireAuthentication` | `true` | Webツールの共有Cookie認証。未設定は`true`。`false`は開発用の仮管理者で動かすデバッグ実行専用 |
| `SalesSupport:Tool:UseToolMaster` | `true` | Webツールのツールマスタ参照・DBログ。未設定は`true`。`false`はデバッグ実行専用 |
| `SalesSupport:Application:Name` | `営業支援ポータル` | 画面・メールの表示名 |
| `SalesSupport:Portal:BaseUrl` | `https://sales-support/` | 実URLに合わせて変更 |
| `SalesSupport:DataProtection:KeyDirectory` | `../keys` | 配置に合わせて変更 |
| `SalesSupport:Storage:TemporaryRoot` / `PermanentRoot` | `../storage/temp` / `../storage/files` | 配置に合わせて変更。空にするとその保存領域を提供しません |
| `SalesSupport:Storage:CleanupTimeoutSeconds` | `5` | 既定値 |
| `SalesSupport:Mail:TimeoutSeconds` | `30` | 既定値 |
| `SalesSupport:Http:TimeoutSeconds` | `30` | 既定値 |
| `SalesSupport:Logging:TimeoutSeconds` | `3` | 既定値 |
| `SalesSupport:Proxy:KnownProxies` | `[]` | 既定値。空では転送ヘッダーを採用しません |

開発向け雛形は`EnvironmentCode=DEVELOPMENT`、PortalのURLを`https://localhost:7065/`、SMTPを`127.0.0.1:2525`にした例です。SMTPサーバーを別途用意しない限りメールは送れません。PortalとDBを使う検証には、空の接続文字列へ使い捨ての開発用DBを指定してください。接続文字列を空のままツールをデバッグ実行する場合は、[認証とツールマスタを両方`false`](#ツールをデバッグ実行する)にします。

## ツールをデバッグ実行する

ツールのデバッグ実行では、共通設定の次の2つを個別に`false`へ変更できます。どちらも雛形と未設定は`true`（本番と同じ動作）です。`false`にするには、Commonの`Portal:EnvironmentCode=DEVELOPMENT`と、ホストの`ASPNETCORE_ENVIRONMENT=Development`の両方が必要です。後者は各ツールの`https`起動プロファイルが設定します。条件を満たさない状態で`false`を指定すると起動を拒否します。Portalはこれらの設定を無視し、常に通常の認証・DB確認を行います。

| キー | `false`にしたときの動作 |
| --- | --- |
| `SalesSupport:Tool:RequireAuthentication` | Portalへログインせず、開発用の仮管理者（LoginId`AdminUser`、UserId`00000000-0000-0000-0000-000000000001`、ロール`ADMIN`）で動かします。共有Cookieは使わず、仮管理者はDBへ登録しません |
| `SalesSupport:Tool:UseToolMaster` | ツールマスタ（ツール行・ロール割当て）を参照せず、設定したToolIdの画面だけを利用できます。DBログを保存せず、明細ファイルなどツール単位のアップロードはCommonが拒否します |

| 認証 | ツールマスタ | 接続文字列 | 主な用途 |
| --- | --- | --- | --- |
| `true` | `true` | 必須 | 結合確認・本番 |
| `false` | `true` | 必須 | ログインなしでツール行・アップロード条件・ログを確認する。ツール行は登録が必要、ロール割当ては不要 |
| `true` | `false` | 必須 | Portalでログインして、ツール未登録のまま画面を確認する |
| `false` | `false` | 不要 | DBなしで入力画面・計算・TSV出力を確認する。業務日付は現在のJST日付 |

- どちらかを`false`にすると、障害の詳細をコンソールにも出力します。DBログの有無とは関係なく出力します。
- 仮管理者はAspNetUsersに存在しないため、サンプルの案件保存・一覧などユーザーに紐づく機能は利用できません。
- 共通設定ファイルへの相対パス、ToolId、アプリ名、鍵・一時保存フォルダー等の設定は、どの組合せでも必要です。

設定変更後はアプリを再起動してください。環境変数で上書きする場合のキーは`SalesSupport__Tool__RequireAuthentication`と`SalesSupport__Tool__UseToolMaster`です。

`SalesSupport:Mail:Enabled`と`SalesSupport:Application:ToolId`は雛形へ含めていません。`Enabled`は設定読み込み後、CommonがPortalで有効・ツールで無効に固定します。各ツールはメールを使用せず、共通設定ファイルや環境変数で`Enabled`を指定しても変更できません。`ToolId`はツールごとに異なるため環境変数で与えます。雛形の`Application:Name`はPortal用です。各ツールでは`SalesSupport__Application__Name`をそのツール名に上書きしてください。これを省くと障害ログの`ApplicationName`がPortal名になります。

共通設定ファイルをWeb公開領域・アプリ配置フォルダーの外へ置くこととアクセス権は、導入時に運用手順で確認します。Commonはファイルの存在と相対パスを検証しますが、配置フォルダーとの包含関係は起動時に検査しません。

## 秘密情報の取扱い

- 接続文字列、SMTPの資格情報を含むため、実値を記入したファイルをリポジトリへ追加しないでください。`salessupport.common*.json`は2つの雛形を除き`.gitignore`で除外しています。名前を変えた実ファイルも追加前に内容を確認してください。
- 実値を作業記録、ログ、画面、メールへ出力しないでください。
- 開発環境でもこの雛形をコピーして使用します。開発用の実値はリポジトリ外へ置き、`SalesSupport__CommonConfigPath`で指定します。
