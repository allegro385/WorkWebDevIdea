# SalesSupport 共通設定ファイル

Commonが読み込む共通設定ファイルの雛形です。接続文字列を含むCommonの設定は、Portalと各ツールの`appsettings.json`ではなくこのファイル1つで管理し、Portalと各ツールはCommonのDIサービス（`IConnectionStringProvider`）から接続文字列を受け取ります。

## 配置

1. `salessupport.common.sample.json`をサーバーへコピーし、`salessupport.common.json`等の名前で**Web公開領域とアプリの配置フォルダーの外**へ置きます。
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
| Portalの`SalesSupport:Password:ForbiddenListPath` | Portalの実行フォルダー |

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
| アプリ固有 | 各アプリの環境変数 | `SalesSupport__Application__ToolId`（Webツールごとに必須） |
| アプリ固有の設定 | 各アプリの`appsettings.json`・環境変数 | Portalの`Portal:SupportContact`、`SalesSupport:RateLimits:*`等 |

設定の変更はアプリの再起動で反映します。Commonは実行中に設定ファイルを再読込しません。共通設定ファイルを変更した場合はPortalと全Webツールを再起動します。

## 雛形の値

| キー | 雛形の値 | 区分 |
| --- | --- | --- |
| `ConnectionStrings:SalesSupport` | 空 | 導入時に設定 |
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

`SalesSupport:Mail:Enabled`と`SalesSupport:Application:ToolId`は雛形へ含めていません。`Enabled`はPortalで有効・ツールで無効をCommonが判定するため、共通設定ファイルへ書くと全アプリへ一律に適用されます。`ToolId`はツールごとに異なるため環境変数で与えます。

## 秘密情報の取扱い

- 接続文字列、SMTPの資格情報を含むため、実値を記入したファイルをリポジトリへ追加しないでください。`**/salessupport.common.json`は`.gitignore`で除外しています。
- 実値を作業記録、ログ、画面、メールへ出力しないでください。
- 開発環境でもこの雛形をコピーして使用します。開発用の実値はリポジトリ外へ置き、`SalesSupport__CommonConfigPath`で指定します。
