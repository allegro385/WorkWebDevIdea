# SalesSupport 共通設定ファイル

Commonが読み込む共通設定ファイルの雛形です。接続文字列を含むCommonの設定は、Portalと各ツールの`appsettings.json`ではなくこのファイル1つで管理し、Portalと各ツールはCommonのDIサービス（`IConnectionStringProvider`）から接続文字列を受け取ります。

## 配置

1. `salessupport.common.sample.json`をサーバーへコピーし、`salessupport.common.json`等の名前で**Web公開領域とアプリの配置フォルダーの外**へ置きます。
2. 空欄・0の項目へ実値を設定します。未設定のままでは起動時に構成エラーで停止し、既定値での代替は行いません。
3. Portalと各WebツールのIISアプリケーションへ、環境変数`SalesSupport__CommonConfigPath`でこのファイルの**絶対パス**を設定します（`web.config`の`<environmentVariables>`）。全アプリが同じファイルを指します。
4. ファイルのACLは、対象アプリケーションプールの読取りと運用管理者だけに限定します。Data Protectionの鍵フォルダーと同じ扱いです。

配置手順と確認項目は[導入・運用](../../設計書/05_導入・運用.md#deployment)、キーの一覧と検証条件は[Common詳細設計 第4節](../../設計書/10_Common詳細設計.md)を正とします。

## 設定の優先順位

共通設定ファイル → 環境変数 の順で適用し、環境変数が優先します。アプリごとに異なる値は共通設定ファイルへ書かず、各アプリの環境変数で与えます。

| 区分 | 与え方 | 例 |
| --- | --- | --- |
| 全アプリ共通 | 共通設定ファイル | `ConnectionStrings:SalesSupport`、`SalesSupport:Mail:*` |
| アプリ固有 | 各アプリの環境変数 | `SalesSupport__Application__ToolId`（Webツールごとに必須） |
| アプリ固有の設定 | 各アプリの`appsettings.json`・環境変数 | Portalの`Portal:SupportContact`、`SalesSupport:RateLimits:*`等 |

設定の変更はアプリの再起動で反映します。Commonは実行中に設定ファイルを再読込しません。共通設定ファイルを変更した場合はPortalと全Webツールを再起動します。

## 秘密情報の取扱い

- 接続文字列、SMTPの資格情報を含むため、実値を記入したファイルをリポジトリへ追加しないでください。`**/salessupport.common.json`は`.gitignore`で除外しています。
- 実値を作業記録、ログ、画面、メールへ出力しないでください。
- 開発環境でもこの雛形をコピーして使用します。開発用の実値はリポジトリ外へ置き、`SalesSupport__CommonConfigPath`で指定します。
