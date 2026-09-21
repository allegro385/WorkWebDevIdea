# SalesSupport.Portal.Web

営業支援ポータル本体です。仕様は[Portal詳細設計](../../../../設計書/11_Portal詳細設計.md)、共通契約は[Common詳細設計](../../../../設計書/10_Common詳細設計.md)を正とします。本書は実装済みの範囲と実行に必要な設定だけを記載します。

## 実装済みの範囲

| 区分 | 状態 |
| --- | --- |
| PortalDbContext・業務Entity・既存DDLマッピング | 実装済み |
| Common登録、Identity Store、認証・認可パイプライン | 実装済み |
| P001 ログイン（IP制限・アカウントロック・共有Cookie発行） | 実装済み |
| P009 初回設定・再設定・請求（用途別トークン、発行番号の原子的消費） | 実装済み |
| P010 パスワード変更（SecurityStamp更新・リンク失効・Cookie破棄） | 実装済み |
| P011 入場制限案内 | 実装済み |
| P002 トップの本文（お知らせ・メニューカード） | 未実装 |
| ツール一覧・詳細・お気に入り・個人設定・問い合わせ・管理画面・TSV出力 | 未実装 |
| 最初の管理者を作成する導入用コマンドモード | 未実装 |

ログインには初回パスワード設定済みかつ有効なユーザーが必要です。未実装の導入処理が入るまで、検証環境では管理されたSQLとIdentity API経由の手順でユーザーを用意してください。DDLやダミーデータへ直接パスワードハッシュを投入しないでください。

## 実行に必要な設定

`appsettings.json`へ機密値を書かず、配置環境の設定（環境変数・ユーザーシークレット等）で与えます。いずれかが欠けると起動時に構成エラーで停止します。既定値での代替は行いません。

| キー | 内容 |
| --- | --- |
| `ConnectionStrings:SalesSupport` | SQL Serverの接続文字列 |
| `Portal:EnvironmentCode` | `DEVELOPMENT` または `PRODUCTION` |
| `Portal:SupportContact` | ログイン画面へ表示する社内システム担当の連絡先。未設定なら表示しません |
| `SalesSupport:Application:Name` | 画面・メールで使用するシステム名 |
| `SalesSupport:Portal:BaseUrl` | httpsで末尾スラッシュ付きのPortal公開URL |
| `SalesSupport:DataProtection:KeyDirectory` | 各アプリから参照できる実在の鍵共有フォルダー |
| `SalesSupport:Password:ForbiddenListPath` | 禁止パスワードのUTF-8テキスト（1行1件）の絶対パス。Web公開領域の配下は拒否します |
| `SalesSupport:Storage:TemporaryRoot` / `PermanentRoot` | 一時・永続のファイル保存領域。Webルートと配置先の外に置きます |
| `SalesSupport:Mail:*` | SMTPのHost・Port・TlsMode・From・MaxRecipients等。`DEVELOPMENT`では`DevelopmentRecipient`も必須です |
| `SalesSupport:Proxy:KnownProxies` | 転送ヘッダーを信頼するプロキシのIP。未設定では転送ヘッダーを採用しません |
| `SalesSupport:RateLimits:LoginPermitLimit` | ログイン要求の上限（既定30回/1分） |
| `SalesSupport:RateLimits:PasswordRequestPermitLimit` | 再設定請求の上限（既定30回/1時間） |

禁止パスワードのリストは起動時に一度だけ読み込みます。更新後はアプリを再起動してください。欠落・読込不能は構成エラーとして扱い、照合を省略しません。

Data Protectionの鍵はWindows DPAPIで保護するため、起動できるのはWindowsだけです。Linuxではビルドと単体テストのみ実行できます。

## 検証

リポジトリルートから実行します。

```text
dotnet build SalesSupport/SalesSupport.Portal.slnx
dotnet test SalesSupport/SalesSupport.Portal.slnx
dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web --launch-profile https
```

単体テストはDB・SMTPへ接続しません。行ロック、条件付き一意制約、Identityのトランザクション、共有Cookieの複数アプリ往復、実SMTPは使い捨てのSQL Server DBと実環境での検証が必要です。

## 暫定・残件

- 設定リンクメールの件名・本文は設計書で未確定のため、`PasswordLinkMailTemplates`の暫定文面を使用します。`MailTemplateOptions`へ同じ識別子を登録すると差し替えられます。文面確定時に解消します。
- `Portal:SupportContact`の文面、禁止パスワードの初期リスト、SMTP、鍵共有パスは導入前に確定が必要な外部情報です（[Portal詳細設計 第14節](../../../../設計書/11_Portal詳細設計.md)）。
