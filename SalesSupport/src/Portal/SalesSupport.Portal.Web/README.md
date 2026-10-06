# SalesSupport.Portal.Web

営業支援ポータル本体です。仕様は[Portal詳細設計](../../../../設計書/11_Portal詳細設計.md)、共通契約は[Common詳細設計](../../../../設計書/10_Common詳細設計.md)を正とします。本書は実装済みの範囲と実行に必要な設定だけを記載します。

## 実装済みの範囲

| 区分 | 状態 |
| --- | --- |
| PortalDbContext・業務Entity・既存DDLマッピング | 実装済み |
| Common登録、Identity Store、認証・認可パイプライン | 実装済み |
| P001 ログイン（IP制限・アカウントロック・共有Cookie発行） | 実装済み |
| P011 入場制限案内 | 実装済み |
| P002 トップ（公開システムお知らせ・メニューカード） | 実装済み |
| P003・P008 ツール一覧／お気に入り（ロール別表示、直接起動・取得、星印の登録・解除） | 実装済み |
| P004 ツール詳細（お知らせ・現在版以下の履歴・提供内容）、Web起動、ファイル取得 | 実装済み |
| P005 利用マニュアル・FAQ、P006 個人設定、P007 問い合わせ受付 | 実装済み |
| 共通API（`/api/users/me`、`/api/users/me/preferences`） | 実装済み |
| A001 ツール管理（選択・表示順・基本情報・バージョン・提供内容） | 実装済み |
| A002 ユーザー管理（ログインID検索・外部属性参照・一般ロール間変更・有効状態変更・ロック解除） | 実装済み |
| A005 問い合わせ管理（検索・一括保存・詳細更新） | 実装済み |
| A006 サイト管理（システムお知らせの保存・確認付き手動送信） | 実装済み |

検証の実行方法は[検証](#検証)を参照してください。実行結果は[プロジェクト操作履歴](../../../../作業記録/プロジェクト操作履歴.md)へ記録します。

ログインには外部連携されたログインIDとIdentity互換PasswordHashを持つ有効ユーザーが必要です。連携プロジェクトは未実装です。メール確認済みはログイン条件にしません。

## 実行に必要な設定

設定の取得元は共通設定ファイルとPortal固有の設定です。必要な値が欠けると起動時に構成エラーで停止します。

### 1. Commonの共通設定ファイル（接続文字列を含む共通設定）

接続文字列を含むCommonの設定は、Portalの`appsettings.json`ではなく[共通設定ファイル](../../../config/README.md)で管理します。Portalは接続文字列を自身の設定から読まず、Commonの`IConnectionStringProvider`から受け取ります。

環境変数`SalesSupport__CommonConfigPath`へ、Portalの実行フォルダーから共通設定ファイルへの相対パスを設定してください。未設定・絶対パス・不在・書式不正は構成エラーです。共通設定ファイル内のフォルダー（鍵・保存領域）は、共通設定ファイルがあるフォルダーからの相対パスで指定します。

| キー | 内容 |
| --- | --- |
| `ConnectionStrings:SalesSupport` | SQL Serverの接続文字列 |
| `Portal:EnvironmentCode` | `DEVELOPMENT` または `PRODUCTION` |
| `SalesSupport:Application:Name` | 画面・メールで使用するシステム名 |
| `SalesSupport:Portal:BaseUrl` | httpsで末尾スラッシュ付きのPortal公開URL |
| `SalesSupport:DataProtection:KeyDirectory` | 各アプリから参照できる実在の鍵共有フォルダー |
| `SalesSupport:Storage:TemporaryRoot` / `PermanentRoot` | 一時・永続のファイル保存領域。Webルートと配置先の外に置きます |
| `SalesSupport:Mail:*` | SMTPのHost・Port・TlsMode・From・MaxRecipients等。`DEVELOPMENT`では`DevelopmentRecipient`も必須です |
| `SalesSupport:Proxy:KnownProxies` | 転送ヘッダーを信頼するプロキシのIP。未設定では転送ヘッダーを採用しません |
| `SalesSupport:Http:TimeoutSeconds` / `SalesSupport:Logging:TimeoutSeconds` | 外部HTTPとログ処理の制限時間（既定30秒・3秒） |

### 2. Portal固有の設定（`appsettings.json`・環境変数）

`appsettings.json`と環境別の`appsettings.Development.json`／`appsettings.Production.json`はPortal固有の非機密値だけに使用します。機密値は配置環境の設定で与えます。Commonが読む値はこれらのファイルからは取得されません。

| キー | 内容 |
| --- | --- |
| `Portal:SupportContact` | ログイン画面へ表示する社内システム担当の連絡先。未設定なら表示しません |
| `SalesSupport:RateLimits:LoginPermitLimit` | ログイン要求の上限（既定30回/1分） |
| `SalesSupport:Manual:RelativePath` | 利用マニュアルPDFの配置。永続保存領域からの相対パスで、既定は`Manual/sales-support-portal-manual.pdf` |
| `SalesSupport:Manual:FileName` | マニュアル取得時のファイル名。既定は`sales-support-portal-manual.pdf` |

マニュアルは`/help/manual`から認可済みの経路で配信します。絶対パス・親ディレクトリーを含む指定は起動時に構成エラーとして拒否します。配置がない場合は取得だけが404となり、FAQの表示は継続します。


Data Protectionの鍵はWindows DPAPIで保護するため、起動できるのはWindowsだけです。Linuxではビルドと単体テストのみ実行できます。

## 開発試験用のユーザーを追加する

開発専用DBのログイン確認だけに使用します。本番ユーザーと初期管理者は移行プロジェクトで供給する方針で、Portalの新規登録・TSV取込・設定メール・パスワード変更／再設定は提供しません。

```powershell
dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web -- add-test-user
```

CommonのPortal:EnvironmentCode=DEVELOPMENTとホストDevelopmentの両方を必須とします。ログインID、メールアドレス、表示名、ロールA～D／ADMIN、パスワードと確認を対話入力します。秘密値を引数・ログに出さず、UserManagerでユーザーと通知初期OFFの設定を一括保存します。Identityの開発用パスワード条件は6文字以上で文字種混在を要求しません。連携済みハッシュの検証へ新規作成条件を適用しません。

## お知らせ通知と個人設定

Roles.NoticeMailEnabledは初期ADMIN・A～C=ON、D=OFF。管理者がDB操作で変更します。お知らせはロールの通知許可と本人設定の両方ONの場合に送信します。ツールのお知らせはお気に入り・現行割当ても必要です。問い合わせメールには適用しません。

本人設定は初期OFFです。個人設定のヘッダー・トップメニューはロール通知許可ONの場合だけ表示し、直接画面GET/POSTとAPI GET/PUTもOFFなら403で拒否します。ロールフラグがOFFになっても本人設定は保持します。

## 検証

リポジトリルートから実行します。使用するSDKは`SalesSupport/global.json`で10.0.401に固定しています。別のフィーチャーバンドのSDKしかない環境では「A compatible .NET SDK was not found」で停止するため、global.jsonを書き換えず、指定版数のSDKを導入してください。

```text
dotnet build SalesSupport/SalesSupport.Portal.slnx
dotnet test SalesSupport/SalesSupport.Portal.slnx
dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web --launch-profile https
```

起動には共通設定ファイルが必要です。ローカルでは`SalesSupport/config/salessupport.common.Development.sample.json`をリポジトリ外へコピーして開発用DBの接続文字列と実際の配置パスを設定し、環境変数`SalesSupport__CommonConfigPath`へPortalの実行フォルダーからの相対パスを設定してから実行してください。ビルドと単体テストには不要です。

単体テストはDB・SMTPへ接続しません。行ロック、条件付き一意制約、Identityのトランザクション、共有Cookieの複数アプリ往復、実SMTPは使い捨てのSQL Server DBと実環境での検証が必要です。

実DBでのEF Coreクエリ実行、モデルバインド、ブラウザーでのRazor表示は環境を用意して別途検証してください。

## 暫定・残件

- お知らせメールの件名・本文も未確定のため、`NoticeMailTemplates`の暫定文面（`NOTICE_SYSTEM`／`NOTICE_TOOL`）を使用します。同じ識別子を登録すると差し替えられます。
- お知らせ送信の確認IDと問い合わせの送信IDは、`ConfirmationStore`が本人束縛・一回消費・発行から30分・全用途合計で同時200件まで保持します。上限到達時は新規発行を拒否し、期限切れの整理または消費後に再試行できます。プロセス再起動で失効し、永続化・自動再開は行いません。バイト数の独立した上限は設けません。
- ツール編集の各区画は独立した保存ですが、部分応答による差し替えは未実装です。保存に失敗した区画は入力とエラーを保持したまま同じ画面を再描画し、ほかの区画は保存済みの内容を再表示します。
- 利用マニュアルPDFは`SalesSupport:Manual:*`で指定した永続保存領域の相対配置から配信します。版を含むURLでのキャッシュ制御は運用時の配置方法とあわせて確定が必要です。
- `Portal:SupportContact`の文面、SMTP、鍵共有パスは導入前に確定が必要な外部情報です（[Portal詳細設計 第14節](../../../../設計書/11_Portal詳細設計.md)）。

ログインIDの文字ルール・大小文字・変更可否、ハッシュ受渡しと自動連携契約は[検討事項](../../../../設計書/06_検討事項.md#identity)に残します。現行256文字とIdentity標準正規化は暫定です。
