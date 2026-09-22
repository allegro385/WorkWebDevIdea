# Portal 詳細設計

[資料一覧へ戻る](README.md)

2026-09-21作成。対象は `SalesSupport/src/Portal/SalesSupport.Portal.Web`。本書は実装設計であり、実装済みを示さない。画面・業務条件は[02](02_画面・機能設計.md)、物理列は[04](04_DB設計.md)、共通契約は[10](10_Common詳細設計.md)を正とする。現在のPortalはMVCテンプレートであり、静的モックの認証・保存・メール操作を実装として流用しない。

## 1. 構成・責任分界

```text
SalesSupport.Portal.Web/
├─ Controllers/           利用者画面、認証、共通API
├─ Areas/Admin/
│  ├─ Controllers/        管理者画面
│  └─ Views/              管理者画面のRazor
├─ Services/              画面・業務単位の処理
├─ Authentication/        Identity設定、リンク発行・消費
├─ Entities/              Portal業務Entity
├─ Data/                  PortalDbContext、EntityConfigurations
├─ Models/                入力モデル、表示モデル、応答DTO
├─ Views/                 利用者画面のRazor
├─ Bootstrap/             最初の管理者の導入用処理
└─ wwwroot/               Portal固有のCSS・JavaScript
```

- `Controller → Service → PortalDbContext → SQL Server`。サービスはScoped、Contextを並列処理で共有しない。汎用Repositoryは設けない。
- Portalから単一のCommonをProjectReferenceする。CommonにPortalのサービス・全ツールのEntityを置かない。
- Cookie検証、利用制御、日時、設定・コード取得、ファイル、メール、ログ、出力、共通レイアウトはCommonを使用する。
- Portalはログイン、ユーザー管理、宛先選定、問い合わせ、ツール情報管理を所有する。各Webツールの計算処理・配備・固有データの更新は対象外。
- サービス公開境界はDTOと結果型とし、Entity・IQueryable・DbContextをControllerへ返さない。全非同期処理にCancellationTokenを渡す。

## 2. Entity・DB接続

`PortalDbContext : IdentityUserContext<ApplicationUser, Guid>`とし、ApplicationUserとそのマッピングはCommonを利用する。Identity4テーブルはIdentity API経由で更新する。

| Entity | テーブル | 更新元 |
| --- | --- | --- |
| Tool / ToolCategory | portal.Tools / ToolCategories | ツール編集／カテゴリは運用SQL |
| ToolVersionHistory / ToolFile | portal.ToolVersionHistories / ToolFiles | ツール編集 |
| Notice | portal.Notices | サイト管理・ツール編集 |
| Inquiry | portal.Inquiries | 問い合わせ受付・管理 |
| UserPreference / UserToolFavorite | portal.UserPreferences / UserToolFavorites | 本人の設定・お気に入り |
| FaqItem / FaqCategory | portal.FaqItems / FaqCategories | 参照のみ、運用SQL |

- 設定・コード・アップロード条件はCommon、ログの読取りはCommonのLogDbContextを利用する。採番は既存の`portal.AllocateInquiryId`を呼ぶ専用サービスとし、採番用Entityの通常更新は行わない。
- 列型・桁数・NULL・外部キーは04とDDLに合わせ、起動時にEnsureCreated/Migrateを実行しない。DB変更は管理されたSQLで適用する。
- 参照はAsNoTracking＋表示DTOへの射影を基本とする。約20ツールの一覧は全件表示し、存在しないページ分割やキーワード検索を追加しない。
- 監査対象はUpdateCount、IdentityはConcurrencyStampで競合を検出する。監査トリガー、OUTPUT抑止、生成値の再取得はCommon詳細設計に従う。
- ID、監査列、RoleCodeの変更、MailSentAt、RelativePath等を入力モデルに一括バインドしない。更新可能列を明示して代入する。

## 3. 画面・エンドポイント

以下は実装時のルート契約。URLはアプリケーションのPathBaseを考慮して生成する。更新はPOSTまたはPUT、CSRF検証必須。各管理Controllerには管理者ポリシーを明示し、Area名だけで認可しない。

| 画面 | Controller・経路 | Service | 認可 |
| --- | --- | --- | --- |
| P001 ログイン | Account / GET・POST `/account/login` | AccountService | 匿名可、IP制限 |
| P009 設定・再設定 | Account / GET・POST `/account/password/setup`、`/account/password/reset`、POST `/account/password/request` | PasswordLinkService | 匿名可、リンク・要求制限 |
| P010 変更 | Account / GET・POST `/account/password/change` | AccountService | 本人・Site |
| ログアウト | Commonの同一アプリ内POSTハンドラー | Common | 公開状態にかかわらずCSRF検証 |
| P011 非公開案内 | Home / GET `/private` | PortalShellService | 匿名可、情報最小限 |
| P002 トップ | Home / GET `/` | HomeService | Site |
| P003・P008 一覧 | Tools / GET `/tools`、`/tools/favorites` | ToolQueryService | Site |
| P004 詳細 | Tools / GET `/tools/{toolId}` | ToolQueryService | Site＋ToolDetail |
| Web起動 | Tools / GET `/tools/{toolId}/launch` | ToolQueryService | Site＋ToolUse |
| ファイル取得 | ToolFiles / GET `/tools/{toolId}/files/{fileId}` | ToolFileService | Site＋ToolDownload |
| お気に入り | Favorites / POST `/tools/{toolId}/favorite`、`/unfavorite` | FavoriteService | 本人・Site、対象状態再検証 |
| P005 マニュアル・FAQ | Help / GET `/help`、`/help/manual` | HelpService | Site |
| P006 個人設定 | Preferences / GET・POST `/preferences` | PreferenceService | 本人・Site |
| P007 問い合わせ | Inquiries / GET・POST `/inquiries/new` | InquiryService | Site |
| A001 ツール管理 | Admin/Tools / GET `/admin/tools`、GET `/admin/tools/{toolId}` | ToolAdminService | ADMIN・ToolManage |
| ツール各保存 | Admin/Tools / POST 配下の`basic`、`order`、`versions/*`、`files/*` | ToolAdminService / ToolFileService | ADMIN・ToolManage |
| A002 ユーザー管理 | Admin/Users / GET一覧・編集、POST更新・解除・再発行・取込 | UserAdminService / UserImportService | ADMIN |
| A003 ログ管理 | Admin/Logs / GET `/admin/logs`、`/admin/logs/export` | LogExportService | ADMIN |
| A005 問い合わせ管理 | Admin/Inquiries / GET一覧・詳細、POST保存 | InquiryAdminService | ADMIN |
| A006 サイト管理 | Admin/Notices / GET一覧、POST作成・編集・送信 | NoticeService | ADMIN |
| ツールお知らせ | Admin/Notices / ツールID付きGET・POST | NoticeService | ADMIN・ToolManage |

Web起動は登録済みの同一サイト配下のツールURLへの遷移だけを行う。入力された任意URLへ転送しない。起動先でもCommonによる認証・状態検証を行い、Portalの遷移だけを認可の証拠にしない。WEB_OPENは起動先の正常な入口要求で記録し、Portalでは二重記録しない。

共通APIは`GET /api/users/me`、`GET /api/users/me/preferences`、`PUT /api/users/me/preferences`。DTO・CSRFヘッダー・ツールからの利用制限はCommon詳細設計第12節をそのまま使用する。

## 4. Controller・Viewの共通動作

- Controllerは認可、モデル検証、Service呼出し、画面／応答の選択のみを担当する。送信者UserIdは検証済みCurrentUserから取得する。
- 通常フォームは成功後リダイレクト。入力不正は入力値と項目別エラーを再表示し、パスワードとファイル選択は復元しない。秘密情報・問い合わせ本文をTempDataのCookieに保存しない。
- ツール編集の4区画（お知らせ、履歴、基本情報、提供内容）は独立フォーム・独立保存。部分応答で対象区画のみ差し替え、他区画の未保存入力を維持する。フォームを入れ子にしない。
- 保存中のボタン無効化は操作補助であり、二重実行防止や認可の代わりにしない。既知の業務競合は409、入力不正は400、取得不能は503を基本とし、画面では安全な日本語に変換する。
- 共通認証の401/403/404等の扱いを継承する。HTMLとAPIの応答を混同せず、APIへログインHTMLを返さない。
- テキストはRazorでエスケープし、本文はCSSで改行表示する。HTMLとして保存・描画しない。認証・個人情報画面とダウンロードにはno-storeを設定する。
- 認証フォームのトークン付きURLはReferer送信を抑止し、第三者リソースを読み込まない。IIS・アプリの要求ログにもトークン／パスワードを出さない。

## 5. 認証・パスワード

### 5.1 ログインと変更

1. 信頼するプロキシ設定を適用後、IP制限（ログイン30回/分）とCSRFを検証する。
2. メールを正規化してUserManagerで取得し、有効・初回設定済み・ロック状態を確認する。不在等の理由を外部へ細分化しない。
3. `CheckPasswordSignInAsync`でパスワード検証と失敗回数更新を行う。5回失敗で30分ロック。成功時は失敗回数をリセットする。
4. 成功したユーザーの最新状態を確認し、LastAccessAtをUserManager経由で更新・確定してから非永続Cookieを発行する。初回UTC時刻・60分スライド・最大8時間はCommon契約に従う。
5. ログイン後の戻り先は安全なローカルURLだけとする。Private時の一般ユーザーは通常機能へ入れず非公開案内へ案内する。

現在パスワードからの変更は`ChangePasswordAsync`を使う。成功時SecurityStampを更新し、既存リンクを消費・共有Cookieを破棄してログインへ戻す。パスワードはトリムせず、14～64文字、ASCII U+0021～U+007E、禁止リストを検証する。

### 5.2 リンクの記録形式

AspNetUserTokensのLoginProviderを`SalesSupport.PasswordLinks`に固定する。トークン本文はDBに保存しない。

| Name | Value |
| --- | --- |
| InitialIssue | 最新発行番号。GuidのN形式（32桁）。未発行・消費済みは行なし |
| ResetIssue | 同上、再設定用 |
| LastRequestUtc | UTC日時のラウンドトリップ形式（O）。再設定請求の最終受付 |

初回用・再設定用に別のDataProtectorTokenProviderを登録し、有効期限を14日・1時間にする。標準のGenerateAsync／ValidateAsyncに渡すpurposeへDBの発行番号を追加する薄い派生クラスを用意する。暗号・ハッシュ・期限検証自体は標準実装に委譲する。発行番号がない／形式不正なら拒否する。初回はGenerateUserTokenAsync／VerifyUserTokenAsyncとAddPasswordAsync、再設定は設定済みプロバイダーを使うGeneratePasswordResetTokenAsync／ResetPasswordAsyncを利用する。

### 5.3 発行・消費の原子性

- ユーザーのセキュリティ更新は短いDBトランザクション内で対象AspNetUsers行を`UPDLOCK,HOLDLOCK`で読取り、最新Entityで判定する。これは同時発行・消費の直列化のための限定的なパラメーター化SQLであり、IdentityテーブルをSQLで更新しない。
- 同じPortalDbContextをUserManager Storeにも渡す。トークン行はSetAuthenticationTokenAsync／RemoveAuthenticationTokenAsync、ユーザーはUserManager経由で更新する。途中のIdentityResult失敗でも全体をロールバックする。
- 再設定請求はIP30回/時を先に検証。対象行をロックしLastRequestUtcと実時刻を比較して5分未満を抑止、受付時刻と発行番号を同じトランザクションで確定する。有効でパスワード未設定なら初回リンクを発行する。
- 不在・無効・アカウント単位の抑止は同じ受付文言「対象のアカウントが利用可能な場合、設定用メールを送信します。」。IP制限は429。SMTPは確定後に一度だけ呼び、失敗・結果不明でも古い番号へ戻さない。
- 消費POSTでは同じユーザー行ロック取得後に最新番号でトークンを検証する。パスワード更新、初回のEmailConfirmed=true、SecurityStamp更新、両用途の発行番号削除を一括確定する。並行した2要求のうち成功するのは1件のみ。
- 入力不正は番号を消費しない。期限切れ・改変・消費済みは共通の無効リンク案内。GETでは消費しない。再設定だけではロックを解除しない。
- メール送信中に次の発行が成立すると先行メールが無効になることは許容し、最新リンクのみを有効とする。

## 6. ユーザー管理・初期登録

- 一般ユーザーの新規登録はTSVだけとし、単独登録画面・APIを設けない。各行のEmail=UserName、DisplayNameを検証し、RoleCode=USER、IsActive=trueで、UserManager.CreateAsyncと通知2項目が有効のUserPreference作成を同一トランザクションで行う。パスワードは管理者が設定しない。確定後に初回リンクを発行・送信する。
- 編集はConcurrencyStampを受け取り比較し、表示名・メール・有効状態だけを更新する。登録済みRoleCodeは画面から変更しない。ユーザーを物理削除しない。
- 無効化前に自分自身・最後の有効管理者・担当中のTools/問い合わせを検査する。担当の引継ぎは既存編集画面で先に行う。複数ユーザーにまたがる管理者数の検査はSerializableトランザクションで行い、デッドロックを自動再試行せず再読込を促す。
- 担当者を設定する各サービスも対象ユーザー行をロックして有効ADMINを確認する。無効化側と同じ規約で直列化し、確認直後の担当追加を防ぐ。複数ユーザーのロック順はUserId順に統一する。
- メール変更はSetEmailAsync／SetUserNameAsyncで正規化列も更新し、初回未設定は未確認のまま、設定済みは社内確認済みとしてEmailConfirmedを保持する。SecurityStamp更新と発行番号削除を同時確定する。本人への確認メール画面は追加しない。
- ロック解除は対象行のロックとConcurrencyStamp検査後、SetLockoutEndDateAsync(null)とResetAccessFailedCountAsyncを同時確定する。有効状態・パスワード設定状態は変更しない。ログイン側の失敗回数更新も同じ行直列化規約を適用する。
- 最初の管理者はWeb公開ルートではなく導入用コマンドモードで作成する。有効管理者が既にいる場合は作成を拒否し、同じIdentity・Preference作成サービスを利用する。メール・秘密をコマンド履歴やログへ出さず、安全な入力経路を使う。DDLやダミーデータの直接パスワード投入で代替しない。

### TSV一括登録

1. ADMIN・CSRF確認後、UTF-8のEmail／DisplayNameの2列を読み取る。ヘッダー、列数、必須・桁数、ファイル内と既存ユーザーの正規化メール重複を全行検査する。
2. エラーが1件でもあれば登録せず行番号と項目エラーを表示する。パスワード・RoleCode列の持込みは拒否する。
3. 確認データはランダムな確認IDでサーバー側に一時保持し、実行者UserIdに結び付ける。ブラウザーのhidden値だけを登録データの正本にしない。
4. 実行時にADMIN、期限、未実行を再検証して確認IDを原子的に使用中へ変える。全行を再検証後、各行をUSER・有効で登録する。ユーザー＋設定は1行ごとのトランザクション。
5. DB失敗で以後を停止する。確定済み行は戻さない。確定後のリンク発行・メール失敗を理由に登録をやり直さない。
6. 結果は行番号、メール、登録済み／登録失敗／未処理を表示する。メール成否の件数は表示しない。再実行は未登録分を手動で新しいTSVにする。

容量1,000,000バイト以下、データ行100件以下（ヘッダーを除く）、SITE＋USER_IMPORTで.tsvのみ許可する。101件目で全体を拒否し切り捨て登録しない。Commonで容量を検証し、Portalでヘッダー・UTF-8・行数を検証する。一時保持期限は未確定（第14節）。初期実装候補は上限付きメモリー保持で、再起動時は失効・再取込とし永続ジョブや自動再開を追加しない。確認データの期限・総保持量は実装前に確定する。

## 7. 一覧・個人設定・FAQ

- 一覧はPUBLICとPRIVATEだけを取得し、カテゴリ順→ツール順→名前→ToolIdで安定ソートする。PRIVATEは一般ユーザーにリンクを出さない。ADMINも通常一覧ではHIDDENを表示しない。
- 現在版なしは表示だけVer.1.0.0、更新日なし。履歴は数値3組比較で現在版以下を表示する。文字列順で比較しない。
- お気に入りは本人IDとToolIdの複合キーを使用し、追加済みへの追加・未登録への解除は成功扱いとする。HIDDENの既存登録は消さず一覧から除外する。追加・解除は対象の表示可能状態を再確認する。
- 個人設定は本人の2項目とUpdateCountだけを受け取り、1行の競合更新とする。存在しない設定を無条件に通知有効として扱わず、整合性エラーとして検出する。
- トップは公開SYSTEMお知らせ、詳細は公開TOOLお知らせを取得する。FAQは公開行をカテゴリ順・項目順・ID順で表示する。FAQ編集・カテゴリ編集画面は追加しない。
- マニュアルPDFは管理された相対配置から認可済み経路で取得する。任意パスは受け付けない。

## 8. ツール・履歴・ファイル管理

### 保存単位

| 操作 | 競合対象・トランザクション |
| --- | --- |
| 基本情報 | ToolsのUpdateCount。担当者・カテゴリ・コード・種別別条件を再検証 |
| ツール順序 | 提出された並べ替え対象全件のID集合・UpdateCountを検査し一括確定 |
| 履歴追加・編集・削除・現在版 | 対象ツール行をロックし、履歴集合と提出時UpdateCountを検査。対象履歴の変更を一括確定 |
| ファイル追加・差替え・削除・順序 | 対象ツール行ロック＋対象ファイル集合・UpdateCount検査。ファイルDB変更を一括確定 |

現在版・順序のフォームには対象集合のIDと版を含め、別要求による追加・削除も競合にする。基本情報・履歴・ファイル間の排他ロックは整合性検査用であり、無関係な区画の入力を保存しない。

- 新規ツールは運用SQL、初期HIDDEN。種別は編集不可。DOCUMENTのPUBLIC化は参考資料1件以上が必要。WEBのURL未登録、DESKTOPのAPP未登録、履歴・現在版未登録を一律に公開禁止にしない。
- 現在版は履歴区画内に専用の保存操作を設ける。新規履歴はIsCurrent=false。切替では旧版falseを保存後、新版trueを保存し、同じトランザクションで確定して条件付き一意制約に合わせる。
- 履歴削除は数値最大・現在版でない・総数2件以上を同時検査する。履歴編集による現在版変更は行わない。各組0～99、先頭ゼロ禁止で検証する（0単独は許可）。
- REFERENCE新規は表示名＋ファイル必須。編集は表示名のみ可。APP最大1件、DESKTOPだけ、削除UIなし。DB一意制約も併用する。
- 新しい物理ファイルをCommonで保存→DB参照確定→旧物理ファイル削除。DB競合／失敗時は新ファイルを清掃し旧参照を維持する。旧ファイル削除失敗はログ＋運用清掃とし、成功したDB変更を戻さない。
- ファイル削除はDB参照削除を先に確定し、実体を削除する。ダウンロードはToolIdとFileIdの所属を照合し、認可後にCommonでOpenReadする。常に添付として返し、元ファイル名を安全化する。物理パスを応答に含めない。
- 配布アプリの認可済みダウンロード要求はDESKTOP_DOWNLOADを一度記録する。参考資料の取得を同じイベントとして集計しない。
- ToolFilesのUploadedByUserId・UploadedAtは新規保存／ファイル差替え時だけ、検証済み管理者ID・実UTC時刻で設定する。表示名変更・並べ替えでは更新しない。AspNetUsersへ結合し現在のDisplayName・Email、日時はJSTで表示する。APP／REFERENCE共通の属性とし、クライアント入力を信用しない。

## 9. お知らせ保存・手動送信

- SYSTEM／TOOL別に保存し、Title・Content・IsPublishedだけを編集する。MailSentAtは編集保存で変更しない。削除機能は設けない。
- 送信確認で対象ID・UpdateCount・宛先人数をサーバー側に保持し、実行時に再取得して相違があれば送信せず再確認を求める。SYSTEMまたは同一ToolIdのTOOLだけを1通にまとめる。
- 宛先は有効ユーザーの設定から選定し、TOOLはさらにお気に入りを条件とする。サイト・ツールの公開範囲で通知先を狭めない。宛先は重複除去しBCC、0人は失敗扱い。宛先アドレスを画面やログへ展開しない。
- 確認IDを一度だけ消費して送信する。送信対象の本文・宛先は確定したスナップショットとし、SMTP中にDBトランザクションを保持しない。処理結果不明の再試行はしない。
- SMTP成功時だけ各行のMailSentAtを既存日時と今回成功UTCの新しい方に更新する。NULLなら今回日時を採用する。古い日時へ戻さないことを優先し、グループ内で日時が異なることを許容する。送信開始後の本文編集は妨げず、MailSentAtは内容の最新版の送信保証ではなく過去の送信成功日時として扱う。
- 成功日時の更新は短いトランザクションで行ロックを取得し、本文等を上書きせずMailSentAtだけを更新する。並行送信が後から終了しても既存日時より古い値へ戻さない。全選択行を一括確定する。
- SMTP失敗・一部拒否・結果不明は以前の日時を保持する。送信成功後のDB更新失敗もログへ記録し、メールを自動再送しない。画面は「送信処理が終了しました。」とし、配達成功や失敗を断定しない。
- 明示的に新しく確認した手動再送は許可する。永続キュー・送信試行テーブル・自動配信は追加しない。確認IDの保持方式は一括取込と同じ有効期限付き・本人束縛・再起動失効方式とする。

## 10. 問い合わせ受付・管理

### 受付

1. Categoryと対象の選択を必須にし、INQUIRY_TARGETマスタは作らない。PORTAL／OTHERはToolId=null、TOOLは表示・選択可能なToolsのIDを検証する。
2. 本文と一時添付を検証する。条件はCommonの専用アップロードポリシーから取得し、取得失敗は拒否する。SubmittedByUserIdは本人、StatusはACTION_REQUIREDに固定する。
3. 宛先を決定する。送信者をTo、ツール担当者または代替の有効管理者をBCC。PORTAL／OTHERは有効管理者、初期AssigneeUserIdはツール担当者またはnullとする。
4. UTCから求めた実際のJST日付で既存採番プロシージャを外側トランザクションなしで呼ぶ。採番後、問い合わせ保存トランザクションを開始・確定する。失敗しても採番を戻さない。
5. Common.Mailを一度呼ぶ。成功ならレコードを保持し問い合わせIDを表示する。失敗・一部拒否・結果不明ならレコードを削除し「受付できませんでした。改めて送信してください。」と表示する。削除失敗の特別な修復フローは追加しない。
6. 成否にかかわらずfinallyで一時添付を削除する。要求中断時も期限付きの清掃を試みる。添付をDBや公開領域に残さない。

同じフォーム送信の二重実行は本人に束縛した一回限りの送信IDで抑止する。検証エラー時は再入力可、送信開始後は同じIDで再送しない。受付失敗後は新しい送信IDで新規送信でき、欠番・結果不明時の重複メールは既定どおり許容する。プロセス停止をまたぐ厳密なexactly-once保証は設けない。

### 管理

- 検索条件は02の項目に限定する。ID完全一致、日付はJSTの開始以上・終了翌日未満をUTCへ変換、本文・管理者備考は部分一致、指定条件はAND。SQLはEFでパラメーター化する。
- 初期表示は要対応・対応中・検討中を優先する。同順位は受付日時降順・InquiryId降順で安定化する。
- 変更行だけのID・UpdateCount・CategoryCode・TargetType・ToolId・AssigneeUserId・Status・AdminNoteを受け取り、全行を再検証して同一トランザクションで保存する。1件でも不正・競合なら全取消。
- 送信者・本文は変更不可。対象変更で担当者を自動変更せず、管理者が明示した値を検証する。既存の非公開ツールとの関連は保持できるよう現在値を表示し、通常利用の認可を拡張しない。
- 分類変更は確定後にINQUIRY_CLASSIFICATION_UPDATEとして変更前後のコード・IDのみを記録する。本文・備考・宛先は記録しない。管理更新による再メールは行わない。

## 11. ログ・TSV出力

- 業務成功ログはDB確定後にCommonへ依頼する。拒否・失敗は実際の結果で記録し、ログ失敗で本処理を戻さない。Commonが記録済みのメールエラーをPortalで重複記録しない。
- UIでいう操作ログの物理名は`log.UserActivityLogs`。旧呼称UserAccessLogsで別テーブルを作らない。
- 明細は選択した3ログのいずれかの全列・全件をDB定義順で出力し、行順は各ログの主キー昇順とする。固定の列リストを定義し、リフレクション順やSELECT *に依存しない。
- 利用者数はDESKTOP_DOWNLOAD全件、WEB_EXECUTEのSUCCESSを対象にToolId別UserId重複排除で集計する。Toolsを起点に左結合し、未利用・HIDDENを含め0件も出す。WEB_OPENを加算しない。
- AsNoTrackingの逐次読取りからCommon.DataExportへストリーミングし、全件をメモリーへ読み込まない。UTF-8 BOM、CRLF、タブ・改行整形、文字列の数式対策を適用する。
- 読取り開始前に認可・入力を確認し、クライアント切断は読取りを止めて破棄する。出力途中の障害で完全なファイルを保証しない。結果ファイルはサーバーに恒久保存しない。
- ログ記録は並行して継続するため、出力は厳密な時点スナップショットではない。必要なら開始時の最大主キーを上限に固定し、その範囲を昇順で読み取る。

## 12. 起動・設定・実装順序

1. 設定読込み、Common登録、PortalDbContext、Identity Store・TokenProvider、Portalサービス、MVC・CSRF・要求制限を登録する。
2. 例外処理、信頼する転送ヘッダー、HTTPS、ルーティング、認証、要求制限、認可を適切な順で構成する。DBやSMTPの起動時書込みは行わない。
3. 保護画面は既定Siteポリシー、管理はADMIN、匿名経路は明示する。静的ファイルには機密・提供ファイルを置かない。
4. Commonの契約とDDL差分を整合→Identityと状態制御→参照画面→設定・管理更新→ファイル・メール→TSV・導入処理の順に実装する。

接続文字列、SMTP、Portalの実URL、保存領域、鍵共有は共通設定ファイルの配置設定とし、Portalは接続文字列を自身の設定から読まずCommonの`IConnectionStringProvider`から受け取る。実運用連絡先などPortal固有の設定はPortalの設定から取得する。設定欠落をデモ値や許可状態で代替しない。サイト公開状態はDB運用で変更し、サイト管理画面へ設定編集を追加しない。

## 13. 検証・受入条件

| 分類 | 必須検証 |
| --- | --- |
| 認可 | PUBLIC/PRIVATEサイト×USER/ADMIN×ツール3状態。直URL、API、ファイルID差替え、無効化・権限変更・stamp変更 |
| Identity | 5回失敗、30分満了、管理解除、同時失敗要求、変更後全Cookie失効、60分スライドと8時間境界 |
| リンク | 初回14日・再設定1時間、改変、再発行、同時2消費、同時2請求、ロールバック時未消費、メール変更失効 |
| 競合 | 基本編集、現在版切替、履歴削除、ファイル順序、問い合わせ一括の一部競合で全取消 |
| ユーザー | 自分・最後の管理者・担当者の無効化拒否、同時担当割当、ユーザー＋設定の原子性 |
| ファイル | 許可外・上限丁度／超過・設定欠落、パス改変、保存／DB／旧削除失敗、切断時ストリーム解放 |
| メール | 宛先0・重複・通知設定、非公開ツール通知、SMTP失敗／部分拒否／不明、成功後DB失敗、二重クリック |
| 問い合わせ | 対象とToolId整合、同日並行採番、JST日替わり、失敗時削除・一時添付清掃・欠番 |
| TSV | BOM有無、不正UTF-8、列数・重複、登録途中停止、確認の期限・別管理者・二重実行、式・改行対策 |
| 画面 | モックとの項目照合、未保存区画維持、キーボード操作、入力エラー保持、匿名ページの情報最小化 |

単体テストは日時・SMTP・ファイル・Common境界を差し替える。トリガー・一意制約・行ロック・Identityのトランザクションは使い捨てSQL Server DBで統合検証し、インメモリーDBだけで合格としない。実SMTP・実IIS・共有Cookieの複数アプリ往復は別途環境が必要。本書作成では未実施。

## 14. 未確定事項・実装前の差分

| 項目 | 状態・対応 |
| --- | --- |
| APP最終アップロード者・日時 | 確定：ToolFilesにUploadedByUserId・UploadedAtを必須列として追加。現在のユーザー情報を結合表示。既存DBに行がある場合は移行時に実情報を確認し、推測で補完しない |
| TSV取込 | 確定：100件、1,000,000バイト、SITE＋USER_IMPORT、.tsv。確認期限・総保持量は残件 |
| 一時確認ID | 本人束縛・一回消費・再起動失効は本書で具体化。保持期限・メモリー総量はTSV上限と合わせて確定。長期の個人情報保存を追加しない |
| バージョン | 確定：各組0～99、先頭ゼロ禁止。既存DB移行時は不適合値を洗い出し、履歴重複・現在版を確認して個別修正 |
| SQL | 2026-09-21に確定済みのCHECK・監査トリガー等を新規構築SQLへ反映。既存業務DBへの移行・EF統合は未実施。詳細はSQL README参照 |
| 外部情報 | SMTP・From/ReplyTo/BCC送信用To、運用連絡先、禁止パスワード初期リスト、IIS/鍵共有パス・ACLは導入前に必要 |

本書は上記未決事項を恒久仕様に変えず、それ以外のPortal実装境界と処理手順を定義する。コード・DDL・マスタ投入・モックの変更は今回行わない。

## 15. 技術資料

標準TokenProviderの期限設定・派生方法は[MicrosoftのIdentityトークンプロバイダー資料](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/standalone-with-identity/account-confirmation-and-password-recovery?view=aspnetcore-10.0)を参照する。画面方式は本案件のMVCを維持し、リンクの用途別番号・原子的消費は本書固有の設計である。Cookie・EFマッピング等の資料は[Common詳細設計](10_Common詳細設計.md)を参照する。
