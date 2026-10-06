# Portal 詳細設計

[資料一覧へ戻る](README.md)

対象は `SalesSupport/src/Portal/SalesSupport.Portal.Web`。画面・業務条件は[02](02_画面・機能設計.md)、物理列は[04](04_DB設計.md)、共通契約は[10](10_Common詳細設計.md)を正とする。PortalはASP.NET Core MVCで実装する。静的モックの認証・保存・メール操作を実装として流用しない。

## 1. 構成・責任分界

```text
SalesSupport.Portal.Web/
├─ Controllers/           利用者画面、認証、共通API
├─ Areas/Admin/
│  ├─ Controllers/        管理者画面
│  └─ Views/              管理者画面のRazor
├─ Services/              画面・業務単位の処理
├─ Entities/              Portal業務Entity
├─ Data/                  PortalDbContext、EntityConfigurations
├─ Models/                入力モデル、表示モデル、応答DTO
├─ Views/                 利用者画面のRazor
├─ Bootstrap/             開発限定の試験用ユーザー作成
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
| Role / ToolRole | portal.Roles / ToolRoles | ロール定義・ツール割当ては運用SQL、認可時に参照 |
| ToolVersionHistory / ToolFile | portal.ToolVersionHistories / ToolFiles | ツール編集 |
| Notice | portal.Notices | サイト管理・ツール編集 |
| Inquiry | portal.Inquiries | 問い合わせ受付・管理 |
| UserPreference / UserToolFavorite | portal.UserPreferences / UserToolFavorites | 本人の設定・お気に入り |
| FaqItem / FaqCategory | portal.FaqItems / FaqCategories | 参照のみ、運用SQL |

- 設定・コード・アップロード条件はCommonを利用する。ログ記録はCommonのLoggerを使用し、ログ読取り画面は設けない。採番は既存の`portal.AllocateInquiryId`を呼ぶ専用サービスとし、採番用Entityの通常更新は行わない。
- 列型・桁数・NULL・外部キーは04とDDLに合わせ、起動時にEnsureCreated/Migrateを実行しない。DB変更は管理されたSQLで適用する。
- 参照はAsNoTracking＋表示DTOへの射影を基本とする。約20ツールの一覧は全件表示し、存在しないページ分割やキーワード検索を追加しない。
- 監査対象はUpdateCount、IdentityはConcurrencyStampで競合を検出する。監査トリガー、OUTPUT抑止、生成値の再取得はCommon詳細設計に従う。
- ID、監査列、RoleCodeの変更、MailSentAt、RelativePath等を入力モデルに一括バインドしない。更新可能列を明示して代入する。

## 3. 画面・エンドポイント

以下は実装時のルート契約。URLはアプリケーションのPathBaseを考慮して生成する。更新はPOSTまたはPUT、CSRF検証必須。各管理Controllerには管理者ポリシーを明示し、Area名だけで認可しない。

| 画面 | Controller・経路 | Service | 認可 |
| --- | --- | --- | --- |
| P001 ログイン | Account / GET・POST `/account/login` | AccountService | 匿名可、IP制限 |
| ログアウト | Commonの同一アプリ内POSTハンドラー | Common | 公開状態にかかわらずCSRF検証 |
| P011 非公開案内 | Home / GET `/private` | ISystemSettingsReader | 匿名可、情報最小限 |
| P002 トップ | Home / GET `/` | HomeService | Site |
| P003・P008 一覧 | Tools / GET `/tools`、`/tools/favorites` | ToolQueryService | Site |
| P004 詳細 | Tools / GET `/tools/{toolId}` | ToolQueryService | Site＋ToolDetail |
| Web起動 | Tools / GET `/tools/{toolId}/launch` | ToolQueryService | Site＋ToolUse |
| ファイル取得 | ToolFiles / GET `/tools/{toolId}/files/{fileId}` | ToolFileService | Site＋ToolDownload |
| お気に入り | Favorites / POST `/tools/{toolId}/favorite`、`/unfavorite` | FavoriteService | 本人・Site、対象状態再検証 |
| P005 マニュアル・FAQ | Help / GET `/help`、`/help/manual` | HelpService | Site |
| P006 個人設定 | Preferences / GET・POST `/preferences` | PreferenceService | 本人・Site＋ロール通知許可 |
| P007 問い合わせ | Inquiries / GET・POST `/inquiries/new` | InquiryService | Site |
| A001 ツール管理 | Admin/Tools / GET `/admin/tools`、GET `/admin/tools/{toolId}` | ToolAdminService | ADMIN・ToolManage |
| ツール各保存 | Admin/Tools / POST 配下の`basic`、`order`、`versions/*`、`files/*` | ToolAdminService / ToolFileAdminService | ADMIN・ToolManage |
| A002 ユーザー管理 | Admin/Users / GET一覧・編集、POST更新・解除 | UserAdminService | ADMIN |
| A005 問い合わせ管理 | Admin/Inquiries / GET一覧・詳細、POST保存 | InquiryAdminService | ADMIN |
| A006 サイト管理 | Admin/Notices / GET一覧、POST作成・編集・送信 | NoticeService | ADMIN |
| ツールお知らせ | Admin/Notices / ツールID付きGET・POST | NoticeService | ADMIN・ToolManage |

Web起動は登録済みの同一サイト配下のツールURLへの遷移だけを行う。入力された任意URLへ転送しない。起動先でもCommonによる認証・状態検証を行い、Portalの遷移だけを認可の証拠にしない。WEB_OPENは起動先の正常な入口要求で記録し、Portalでは二重記録しない。

共通APIは`GET /api/users/me`、`GET /api/users/me/preferences`、`PUT /api/users/me/preferences`。DTO・CSRFヘッダー・ツールからの利用制限はCommon詳細設計第12節をそのまま使用する。

## 4. Controller・Viewの共通動作

- Controllerは認可、モデル検証、Service呼出し、画面／応答の選択のみを担当する。送信者UserIdは検証済みCurrentUserから取得する。
- 通常フォームは成功後リダイレクト。入力不正は入力値と項目別エラーを再表示し、パスワードとファイル選択は復元しない。秘密情報・問い合わせ本文をTempDataのCookieに保存しない。
- ツール編集の4区画（お知らせ、履歴、基本情報、提供内容）は独立フォーム・独立保存。保存後は画面全体を再描画する。入力不正時は対象区画の入力とエラーを保持し、他区画は保存済みの内容を再表示する。フォームを入れ子にしない。
- 保存中のボタン無効化は操作補助であり、二重実行防止や認可の代わりにしない。既知の業務競合は409、入力不正は400、取得不能は503を基本とし、画面では安全な日本語に変換する。
- 共通認証の401/403/404等の扱いを継承する。HTMLとAPIの応答を混同せず、APIへログインHTMLを返さない。
- テキストはRazorでエスケープし、本文はCSSで改行表示する。HTMLとして保存・描画しない。認証・個人情報画面とダウンロードにはno-storeを設定する。

## 5. 外部連携資格情報による認証

1. 信頼するプロキシ適用後、IP制限30回/分とCSRFを検証する。
2. ログインIDをIdentity標準のNormalizeNameで正規化し、ポータルのNormalizedLoginIdからユーザーを取得する。Emailで検索しない。有効状態・ロール存在を確認する。
3. CheckPasswordSignInAsyncで連携済みPasswordHashを検証し、失敗回数・30分ロックを管理する。EmailConfirmedは条件にしない。パスワードをトリムしない。
4. 行ロック後に最新状態を再取得し、資格情報検証時とSecurityStampが一致する場合だけLastAccessAtをUserManagerで更新・確定し、非永続共有Cookieを発行する。
5. 安全なローカル戻り先だけを許可し、Privateサイトの一般ユーザーは入場制限案内へ遷移する。失敗文言はログインIDまたはパスワードの共通文言に統一する。

- UserIdはGuidのまま、UserNameをLoginId、NormalizedUserNameをNormalizedLoginIdへマッピングする。ログインIDの文字ルールは未決定で、現行256文字と標準正規化は暫定。
- パスワードの変更・再設定は連携元で行う。ポータルのPasswordLinkService、設定トークンProvider、設定メール、パスワード変更・再設定経路を設けない。
- 外部データ受信・移行プロジェクトとハッシュ受渡し契約は[未決定事項](06_検討事項.md#identity)として残す。ログインで連携元へ都度照会しない。

## 6. ユーザー管理・外部連携

- ユーザー管理はログインID・表示名・メール・ロール・有効状態・ロック状態の一覧／検索を提供する。連携管理項目は参照表示とし、入力モデルに含めない。
- 更新は一般ロール間のRoleCodeとIsActiveだけ。現在値・変更先の一般ロール存在を検証し、ADMINとの相互変更、自身と最後の有効管理者の無効化、担当中管理者の無効化を拒否する。
- ConcurrencyStampとUserManagerを使い、変更時にSecurityStampを更新する。外部連携属性を上書きせず、ユーザーを物理削除しない。
- ロック解除は行ロック・ConcurrencyStamp照合後、SetLockoutEndDateAsync(null)とResetAccessFailedCountAsyncを同時確定する。有効状態やパスワードを変更しない。
- 本番ユーザー作成と資格情報・表示名・メールの更新はデータ移行用プロジェクトからIdentity APIで行う。初期管理者も連携で供給する。TSV登録・bootstrap-admin・設定メール発行は廃止。
- 開発限定add-test-userはCommonのDEVELOPMENTとホストDevelopmentを確認し、ログインID・メール・表示名・ロールA～D／ADMIN・秘密入力パスワードを取得する。開発限定import-test-usersも同じ環境制限と保存処理を使う。UserManager.CreateAsyncと通知初期OFFのUserPreferenceを一人ごとの同一トランザクションで作成する。メール送信は行わない。Identityの開発用作成条件を連携済みハッシュの照合へ適用しない。
- import-test-users [--role A|B|C|D|ADMIN]はロール省略時A。UTF-8の標準入力からログインID・メールアドレス・平文パスワード・表示名の4列を読み、全行の形式・上限1,000件／1,048,576文字・Identity正規化後のファイル内重複を保存前に検証する。ヘッダーは任意、入力値のタブ・改行・制御文字は不可。ホスト構成へ取込引数を渡さない。入力不備では0件、DB重複・保存失敗では最初の失敗で停止し、確定済み行は保持する。既存ユーザーを更新せず、行番号・登録済み件数だけを表示する。入力・例外内容・パスワードを結果へ表示せず、ファイル保存・Webアップロード・自動再試行を追加しない。終了コードは0成功／1保存失敗／2環境拒否／3入力不備。[具体的な形式と実行手順](../SalesSupport/src/Portal/SalesSupport.Portal.Web/README.md#tsvで複数の試験ユーザーを追加する)を参照。
- 通知設定は連携作成時に2項目OFF、後続連携で保持する。内部UserIdを変えない。同期方式と初期ロール・有効状態・管理者供給手順は移行設計で確定する。
- お知らせ送信確認と問い合わせ送信IDはConfirmationStoreで本人束縛・一回消費・30分・全用途200件上限。再起動で失効し、永続化・自動再開を行わない。

## 7. 一覧・個人設定・FAQ

- 一覧はPUBLICとPRIVATEだけを取得し、一般ユーザーはToolRolesに本人のRoleCodeがあるツールに絞る。ADMINはロール割当てに関係なく表示する。SortOrder→ツール名→ToolIdで安定ソートし、カテゴリは並び順に使わない。PRIVATEは一般ユーザーにリンクを出さない。ADMINも通常一覧ではHIDDENを表示しない。
- 全ツールとお気に入りの一覧ではツール名を詳細へのリンクとし、別列に提供内容の直接操作を表示する。WEBは登録済みURLがある場合だけ既存の`/tools/{toolId}/launch`へ、DESKTOPはAPPファイルがある場合だけ既存のファイル取得経路へ、関連資料は登録済みの各FileIdの取得経路へリンクする。DOCUMENTは関連資料だけを表示する。コンテンツ未登録時はリンクを作らない。PRIVATEの一般ユーザーには詳細・直接操作のリンクを出さない。
- 一覧からの直接操作も詳細画面からの操作と同じService・認可経路を使用する。ファイル取得ではToolIdとFileIdの所属を照合し、Web起動先でもCommonの認可を実行する。WEB_OPENはWebツール入口、DESKTOP_DOWNLOADはダウンロード要求でそれぞれ既存の記録規則に従い、一覧からの操作を理由に二重記録しない。
- 詳細・Web起動・ファイル取得と各Webツールの直接要求では、状態と現在のロール割当てを再検証する。一般ロールに割当てがなければ拒否し、ADMINは割当てを要しない。割当て取得失敗は拒否する。
- 現在版なしは表示だけVer.1.0.0、更新日なし。履歴は数値3組比較で現在版以下を表示する。文字列順で比較しない。
- お気に入りは本人IDとToolIdの複合キーを使用し、追加済みへの追加・未登録への解除は成功扱いとする。HIDDENの既存登録は消さず一覧から除外する。追加・解除は対象の表示可能状態を再確認する。
- 個人設定はロールのNoticeMailEnabledをDBで確認し、OFF・未定義は画面GET/POST・API GET/PUTを403で拒否する。本人の2項目とUpdateCountだけを受け取り、1行の競合更新とする。存在しない設定を無条件に通知有効として扱わず、整合性エラーとして検出する。
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

- 宛先はユーザー・ロール・本人設定を結合し、有効かつRoles.NoticeMailEnabled=ONかつ本人設定ONに絞る。ツールのお知らせはお気に入りとロール割当て条件も適用する。ADMINもロール通知許可と本人設定を必須にする。問い合わせの宛先には適用しない。
- 初期ADMIN・A～Cは通知許可ON、DはOFF。管理者がDB操作で変更する。本人通知は両項目初期OFF。ヘッダー・トップの個人設定リンクもロールフラグで表示する。

- SYSTEM／TOOL別に保存し、Title・Content・IsPublishedだけを編集する。MailSentAtは編集保存で変更しない。削除機能は設けない。
- 送信確認で対象ID・UpdateCount・宛先人数をサーバー側に保持し、実行時に再取得して相違があれば送信せず再確認を求める。SYSTEMまたは同一ToolIdのTOOLだけを1通にまとめる。
- 宛先は有効ユーザーの設定から選定し、TOOLはさらにお気に入りを条件とする。サイト・ツールの公開範囲で通知先を狭めない。宛先は重複除去しBCC、0人は失敗扱い。宛先アドレスを画面やログへ展開しない。
- 確認IDを一度だけ消費して送信する。送信対象の本文・宛先は確定したスナップショットとし、SMTP中にDBトランザクションを保持しない。処理結果不明の再試行はしない。
- SMTP成功時だけ各行のMailSentAtを既存日時と今回成功UTCの新しい方に更新する。NULLなら今回日時を採用する。古い日時へ戻さないことを優先し、グループ内で日時が異なることを許容する。送信開始後の本文編集は妨げず、MailSentAtは内容の最新版の送信保証ではなく過去の送信成功日時として扱う。
- 成功日時の更新は短いトランザクションで行ロックを取得し、本文等を上書きせずMailSentAtだけを更新する。並行送信が後から終了しても既存日時より古い値へ戻さない。全選択行を一括確定する。
- SMTP失敗・一部拒否・結果不明は以前の日時を保持する。送信成功後のDB更新失敗もログへ記録し、メールを自動再送しない。画面は「送信処理が終了しました。」とし、配達成功や失敗を断定しない。
- 明示的に新しく確認した手動再送は許可する。永続キュー・送信試行テーブル・自動配信は追加しない。確認IDの保持方式は有効期限付き・本人束縛・再起動失効方式とする。

## 10. 問い合わせ受付・管理

- 利用者の問い合わせ対象は一般公開・限定公開のツールを共通に表示し、POSTでも現在の状態を再検証する。非公開は選択不可とする。対象としての表示はツール本体の認可を変更しない。
- 管理一覧のステータス色は`INQUIRY_STATUS`のCodeMaster.ColorCodeを表示用に検証した値から取得し、固定CSSのコード別色は持たない。

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

## 11. ログ記録

- 業務成功ログはDB確定後にCommonへ依頼する。拒否・失敗は実際の結果で記録し、ログ失敗で本処理を戻さない。Commonが記録済みのメールエラーをPortalで重複記録しない。
- UIでいう操作ログの物理名は`log.UserActivityLogs`。旧呼称UserAccessLogsで別テーブルを作らない。
- ログ管理画面、ログ出力URL、LogExportServiceおよびログ専用TSV出力処理は設けない。必要な抽出・集計はDB権限を持つ運用担当者がSQLで行う。

## 12. 起動・設定・実装順序

1. 設定読込み、Common登録、PortalDbContext、Identity Store、Portalサービス、MVC・CSRF・要求制限を登録する。
2. 例外処理、信頼する転送ヘッダー、HTTPS、ルーティング、認証、要求制限、認可を適切な順で構成する。DBやSMTPの起動時書込みは行わない。
3. 保護画面は既定Siteポリシー、管理はADMIN、匿名経路は明示する。静的ファイルには機密・提供ファイルを置かない。
4. Commonの契約とDDL差分を整合→Identityと状態・ロール制御→参照画面→設定・管理更新→ファイル・メール→外部連携仕様の確認の順に実装する。

接続文字列、SMTP、Portalの実URL、保存領域、鍵共有は共通設定ファイルの配置設定とし、Portalは接続文字列を自身の設定から読まずCommonの`IConnectionStringProvider`から受け取る。実運用連絡先などPortal固有の設定はPortalの設定から取得する。設定欠落をデモ値や許可状態で代替しない。サイト公開状態はDB運用で変更し、サイト管理画面へ設定編集を追加しない。

## 13. 検証・受入条件

| 分類 | 必須検証 |
| --- | --- |
| 認可 | PUBLIC/PRIVATEサイト×A～D/ADMIN×ツール3状態と割当て有無。直URL、API、ファイルID差替え、無効化・ロール変更・stamp変更 |
| Identity | 5回失敗、30分満了、管理解除、同時失敗要求、変更後全Cookie失効、60分スライドと8時間境界 |
| 競合 | 基本編集、現在版切替、履歴削除、ファイル順序、問い合わせ一括の一部競合で全取消 |
| ユーザー | 自分・最後の管理者・担当者の無効化拒否、同時担当割当、ユーザー＋設定の原子性 |
| ファイル | 許可外・上限丁度／超過・設定欠落、パス改変、保存／DB／旧削除失敗、切断時ストリーム解放 |
| メール | ロール通知許可×本人設定、未定義ロール拒否、宛先0・重複・通知設定、非公開ツール通知、SMTP失敗／部分拒否／不明、成功後DB失敗、二重クリック |
| 問い合わせ | 対象とToolId整合、同日並行採番、JST日替わり、失敗時削除・一時添付清掃・欠番 |
| 画面 | ロール通知許可OFFの直接個人設定GET/POST・API GET/PUT拒否、本人初期OFF、モックとの項目照合、未保存区画維持、キーボード操作、入力エラー保持、匿名ページの情報最小化 |

単体テストは日時・SMTP・ファイル・Common境界を差し替える。トリガー・一意制約・行ロック・Identityのトランザクションは使い捨てSQL Server DBで統合検証し、インメモリーDBだけで合格としない。実SMTP・実IIS・共有Cookieの複数アプリ往復は環境を用意して別途検証する。

## 14. 導入前の確認事項

- 既存DBにツールファイルがある場合、最終登録・差し替え管理者と日時の実情報を確認して移行する。推測値で補完しない。
- 既存のバージョン値は各組0～99・先頭ゼロ禁止の形式に照らして確認し、履歴重複と現在版を個別に修正する。
- 既存業務DBへのSQL適用手順は[SQL README](../SalesSupport/sql/README.md)を参照する。
- SMTP、送信元・返信先・送信用宛先、運用連絡先、IISと鍵共有パス・ACLは導入前に確定する。未決定事項は[検討事項](06_検討事項.md)で管理する。

## 15. 技術資料

Identityのユーザー管理、Cookie共有、EFマッピング等の技術資料は[Common詳細設計](10_Common詳細設計.md)を参照する。外部連携契約は[検討事項](06_検討事項.md#identity)で管理する。
