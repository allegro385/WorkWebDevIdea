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
| P002 トップ（公開システムお知らせ・メニューカード） | 実装済み |
| P003・P008 ツール一覧／お気に入り（星印の登録・解除、限定公開の表示制御） | 実装済み |
| P004 ツール詳細（お知らせ・現在版以下の履歴・提供内容）、Web起動、ファイル取得 | 実装済み |
| P005 利用マニュアル・FAQ、P006 個人設定、P007 問い合わせ受付 | 実装済み |
| 共通API（`/api/users/me`、`/api/users/me/preferences`） | 実装済み |
| A001 ツール管理（選択・表示順・基本情報・バージョン・提供内容） | 実装済み |
| A002 ユーザー管理（検索・編集・ロック解除・設定リンク再発行・TSV一括登録） | 実装済み |
| A003 ログ管理（3ログの明細とツール別利用者数のTSV出力） | 実装済み |
| A005 問い合わせ管理（検索・一括保存・詳細更新） | 実装済み |
| A006 サイト管理（システムお知らせの保存・確認付き手動送信） | 実装済み |
| 最初の管理者を作成する導入用コマンドモード | 実装済み |

画面を追加した範囲はビルド・単体テスト・ブラウザー操作のいずれも未実施です（[検証](#検証)を参照）。

ログインには初回パスワード設定済みかつ有効なユーザーが必要です。最初の管理者は本書の導入用コマンドで作成し、DDLやダミーデータへ直接パスワードハッシュを投入しないでください。

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
| `SalesSupport:Manual:RelativePath` | 利用マニュアルPDFの配置。永続保存領域からの相対パスで、既定は`Manual/sales-support-portal-manual.pdf` |
| `SalesSupport:Manual:FileName` | マニュアル取得時のファイル名。既定は`sales-support-portal-manual.pdf` |

マニュアルは`/help/manual`から認可済みの経路で配信します。絶対パス・親ディレクトリーを含む指定は起動時に構成エラーとして拒否します。配置がない場合は取得だけが404となり、FAQの表示は継続します。

禁止パスワードのリストは起動時に一度だけ読み込みます。更新後はアプリを再起動してください。欠落・読込不能は構成エラーとして扱い、照合を省略しません。

Data Protectionの鍵はWindows DPAPIで保護するため、起動できるのはWindowsだけです。Linuxではビルドと単体テストのみ実行できます。

## 最初の管理者を作成する

Web公開ルートではなく、Portalを停止した状態でリポジトリルートから導入用コマンドを実行します。

```text
dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web -- bootstrap-admin
```

メールアドレス、表示名、パスワードと確認入力を順に求めます。パスワード入力ではIME、コピー＆ペースト、Backspaceを利用でき、入力内容は画面に表示されません。不一致時は最大3回まで再入力できます。パスワードをコマンド引数やログには記録しません。有効な`ADMIN`が既に存在する場合は何も作成せず終了します。作成時は`UserManager.CreateAsync`を使用し、通知設定2項目を有効にした`UserPreference`と同一トランザクションで確定します。

パスワードは通常画面と同じ14～64文字・ASCII・禁止リストの条件を満たす必要があります。DB接続、テーブル、禁止リスト等の通常起動設定も必要です。SQLへの直接INSERTやパスワードハッシュの手動投入で代替しないでください。

## 検証

リポジトリルートから実行します。使用するSDKは`SalesSupport/global.json`で10.0.401に固定しています。別のフィーチャーバンドのSDKしかない環境では「A compatible .NET SDK was not found」で停止するため、global.jsonを書き換えず、指定版数のSDKを導入してください。

```text
dotnet build SalesSupport/SalesSupport.Portal.slnx
dotnet test SalesSupport/SalesSupport.Portal.slnx
dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web --launch-profile https
```

単体テストはDB・SMTPへ接続しません。行ロック、条件付き一意制約、Identityのトランザクション、共有Cookieの複数アプリ往復、実SMTPは使い捨てのSQL Server DBと実環境での検証が必要です。

利用者画面・管理画面を追加した変更では、上記のビルド・単体テストを実施していません。SDKを導入できる環境で`dotnet build`と`dotnet test`を実行し、EF Coreのクエリ変換、モデルバインド、Razorの描画をあわせて確認してください。

## 暫定・残件

- 設定リンクメールの件名・本文は設計書で未確定のため、`PasswordLinkMailTemplates`の暫定文面を使用します。`MailTemplateOptions`へ同じ識別子を登録すると差し替えられます。文面確定時に解消します。
- お知らせメールの件名・本文も未確定のため、`NoticeMailTemplates`の暫定文面（`NOTICE_SYSTEM`／`NOTICE_TOOL`）を使用します。同じ識別子を登録すると差し替えられます。
- TSV取込とお知らせ送信の確認IDは、`ConfirmationStore`が本人束縛・一回消費・30分保持・同時200件までの暫定値で保持します。プロセス再起動で失効し、永続化・自動再開は行いません。保持期限と総保持量は[Portal詳細設計 第14節](../../../../設計書/11_Portal詳細設計.md)の残件です。
- ツール編集の各区画は独立した保存ですが、部分応答による差し替えは未実装です。保存に失敗した区画は入力とエラーを保持したまま同じ画面を再描画し、ほかの区画は保存済みの内容を再表示します。
- 利用マニュアルPDFは`SalesSupport:Manual:*`で指定した永続保存領域の相対配置から配信します。版を含むURLでのキャッシュ制御は運用時の配置方法とあわせて確定が必要です。
- `Portal:SupportContact`の文面、禁止パスワードの初期リスト、SMTP、鍵共有パスは導入前に確定が必要な外部情報です（[Portal詳細設計 第14節](../../../../設計書/11_Portal詳細設計.md)）。
