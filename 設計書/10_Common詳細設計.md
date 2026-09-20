# Common 詳細設計

[資料一覧へ戻る](README.md)

2026-09-20作成。対象は `SalesSupport/src/Common/SalesSupport.Common`。本書は合意済みの基本設計を実装可能な契約へ具体化する。業務の可否は02、物理列は04、運用は05を正とする。コード例・クラス名は実装契約であり、実装済みを意味しない。接続情報等の実値は配置時に設定する。

## 1. 構成と責任分界

単一のRazor Class Library（net10.0、MVC Views対応）とする。既存csprojを変更し、名前とProjectReferenceを維持する。CommonからPortal・各ツールへの参照は設けない。CommonはWebアプリとして起動せず、各ホスト内で実行する。

```text
SalesSupport.Common/
├─ Authentication/    Cookie、現在ユーザー、利用制御
├─ Configuration/     Options、設定検証、DB設定取得
├─ MasterData/        コード取得
├─ DateTime/          実時刻・業務日付
├─ Logging/           ログ記録
├─ ErrorHandling/     エラー変換と安全な文言
├─ FileStorage/       ポリシー検証・保存・削除
├─ Mail/              SMTP送信、文面の組立補助
├─ HttpClients/       外部HTTP通信
├─ DataExport/        TSV・CSV書出し
├─ Validation/        共通入力検証
├─ Contracts/         機能間で共有する小さな値型・コード
├─ Entities/          共通機能のEntityのみ
├─ Data/              CommonDbContext、LogDbContext、マッピング
├─ UI/                画面モデル、TagHelper、ViewComponent
├─ Views/Shared/      レイアウト・部品・共通エラー
├─ wwwroot/           CSS・JavaScript・Bootstrap等
└─ DependencyInjection/ 登録用拡張メソッド
```

- csprojは `Microsoft.NET.Sdk.Razor`、`AddRazorSupportForMvc=true`、`Microsoft.AspNetCore.App`のFrameworkReferenceを使用する。C#処理も同じDLLへ含める。
- EF Core SQL Server・Identity EF Coreは10系でホストと統一する。SMTP実装はMailKitを使用する。具体的なパッチ版は実装時に互換性確認して固定し、浮動バージョンを使用しない。
- インターフェースはテスト差替えやホストから呼ぶサービス境界に設ける。各クラスへ機械的にインターフェースを作らない。
- Repository層、全用途のSQL実行基盤、独自DIコンテナーは作らない。
- Portalの問い合わせ保存、通知先選定、ユーザー登録、採番呼出し、ログ集計はPortalのServiceに置く。
- ツールの計算・固有Entity・DbContextは各ツールに置く。共通EntityをHTTP DTOとしてそのまま公開しない。

## 2. Entity・DbContext

### 2.1 配置

| 配置 | 型・テーブル | 読み書き |
| --- | --- | --- |
| Common.Entities.Identity | ApplicationUser : IdentityUser<Guid> | PortalのIdentity APIから更新。Commonの検証では読取り |
| Common.Entities.Configuration | CodeMasterEntry、SystemSetting、UploadPolicy、UploadPolicyExtension、ErrorCodeEntry | Commonは読取り。登録・変更は所定SQL |
| Common.Entities.Authentication | UserAccessRecord、ToolAccessRecord | 認証・公開状態検証用の必要列だけを読取り |
| Common.Entities.Logging | ToolUsageLog、UserActivityLog、SystemErrorLog | 専用Contextから追記 |
| Portal.Entities | Tool、ToolCategory、Notice、Inquiry、FaqItem、FaqCategory、UserPreference、UserToolFavorite、ToolVersionHistory、ToolFile | Portalの業務処理 |
| 各ツール.Entities | 商品・見積等の業務Entity | 各ツールの責務に応じて参照・更新 |

ApplicationUserのマッピングはCommonから提供し、Portalは再定義せず利用する。UserAccessRecordはAspNetUsersのUserId、DisplayName、RoleCode、IsActive、SecurityStampだけを持つ。ToolAccessRecordはToolsのToolId、ToolName、ToolType、Statusだけを持つ。部分Entityを使ってユーザー・ツール管理の更新は行わない。

| Context | 所在 | 構成・用途 |
| --- | --- | --- |
| CommonDbContext | Common | 認証用部分Entity、設定、コード、エラー定義、アップロード条件。AsNoTrackingで読取り専用 |
| LogDbContext | Common | logの3テーブルへの書込み。業務Contextから独立 |
| PortalDbContext | Portal | IdentityUserContext<ApplicationUser, Guid>を継承し、Identity4テーブルとPortal業務Entityを扱う |
| Tool001DbContext等 | 各ツール | ツール固有Entity。同じ共有業務テーブルを各ツールのEntityで参照してよい |

- CommonDbContextはSaveChanges／SaveChangesAsyncを拒否し、部分Entityの誤更新を防ぐ。ログ更新はLogDbContextだけで行う。
- 同じ物理テーブルを複数Contextへマッピングしても、DDLの所有・変更は一か所とする。起動時のEnsureCreated・Migrateは呼ばない。
- Portalのログ参照・集計は読取り用にLogDbContextを利用してよい。Commonに集計業務は置かない。
- 共通DBと各ツールの接続先は初期構成では同じDB。Contextを分けることはDBの分割を意味しない。
- 同一Contextで並列クエリを実行しない。共有要求キャッシュの初回取得も直列化する。

### 2.2 マッピング・監査・同時更新

- スキーマ、列名、桁数、NULL、主キー、外部キーのNO ACTIONをDDLと一致させる。IdentityのIdはUserIdへ対応付け、DB生成GUIDを使用する。
- 共通の `ConfigureAuditColumns` マッピング拡張を提供する。UpdateCountは同時更新トークンかつDB生成値、監査日時・DBユーザーはDB生成値とし、クライアント入力から更新しない。
- トリガー対象テーブルは `UseSqlOutputClause(false)` を設定する。更新成功後は同じトランザクション内でDB生成の監査列を再取得し、次回更新のOriginalValueへ反映する。
- 更新要求のUpdateCountをWHERE条件に使用し、0件更新のDbUpdateConcurrencyExceptionは競合として返す。画面の古い値を最新値で置き換えて再保存しない。
- 一括編集はPortal側で同一トランザクションとし、1件の競合で全変更をロールバックする。
- UserManagerへ登録するStoreはPortalDbContextと同じScopedインスタンスを使う。ユーザーとUserPreferencesの登録は同じ接続・トランザクションで確定する。
- ログ記録には業務トランザクションを渡さない。TransactionScopeを使う場合もログは抑止スコープ＋独立Context・接続とする。
- 問い合わせ番号採番はPortal側の専用処理。外側トランザクションなしで既存プロシージャを呼び、番号確保後に問い合わせ保存のトランザクションを開始する。

## 3. 契約とDI

本書の非同期メソッドには末尾に `CancellationToken ct` を付け、Task／Task<T>を返す。戻り値表の型はTask内の型。ctは通常RequestAbortedから渡す。ファイル清掃とログは後述の独立した制限時間を使う。

| 共通型 | 内容 |
| --- | --- |
| CurrentUser | Guid UserId、string DisplayName、string RoleCode。検証済みの要求内情報 |
| FieldError | Field、Code、Message。入力値そのものを含めない |
| ValidationResult | IsValid、IReadOnlyList<FieldError> |
| AccessDecision | Allowed、AccessFailure?。ユーザー・状態の取得失敗を許可へ変換しない |
| ErrorPresentation | Guid ErrorId、string Message、int StatusCode |
| DeliveryOutcome | Succeeded／Failed／Unknown |
| LogWriteResult | Written／Failed／Skipped。呼出元の業務結果を変えない |

- 読取りで「存在しない」はnullable、DB障害・構成不正は識別可能な例外とする。未登録と取得失敗を同じnullへ潰さない。
- 入力不正・同時更新競合は予定された結果として扱い、予期しない例外と区別する。
- 公開サービスからDbContext、IQueryable、追跡中Entityを返さない。DTOと読み取り専用コレクションを返す。
- RoleCode等の保存値は既存の大文字コードを維持する。enum採用時もDBへ数値を保存しない。

### 登録インターフェース

```csharp
services.AddSalesSupportCommon(configuration, ApplicationKind.Portal);
// 各ツールでは ApplicationKind.Tool。ToolIdは設定から取得。
```

| 寿命 | 対象 |
| --- | --- |
| Singleton | TimeProvider.System、純粋な形式検証器、Options検証器 |
| Scoped | 現在ユーザー、要求内キャッシュ、利用制御、マスタ・設定取得、ポリシー取得、エラー変換、画面モデル生成 |
| Factoryで都度生成・破棄 | CommonDbContext、LogDbContext、SMTP接続 |
| ホストのScoped | PortalDbContext、各ツールのDbContext、UserManager、SignInManager |
| HttpClientFactory | 名前付きHTTPクライアント |

Common登録はPortalのIdentity Storeを自動登録しない。ホストはAddIdentityCore＋SignInManager＋TokenProviders＋PortalDbContextのStoreを登録し、その後Commonの同一認証スキーム設定を適用する。ツールにはパスワード更新用Storeを登録しない。

CommonのDBサービスはIDbContextFactoryから処理ごとにContextを取得・破棄する。要求内の結果共有はScopedサービスで行い、ContextをSingletonに保持しない。

## 4. Configuration

### 外部設定

| キー | 必須範囲・初期値 | 検証 |
| --- | --- | --- |
| ConnectionStrings:SalesSupport | 全アプリ・実値は配置時 | 未設定拒否。値をエラー本文へ出さない |
| Portal:EnvironmentCode | 全アプリ | DEVELOPMENT／PRODUCTIONのみ |
| SalesSupport:Application:Name | 全アプリ | 1～100文字、ログのApplicationName |
| SalesSupport:Application:ToolId | Toolだけ必須 | 1～20文字。DB上のWEBツールと実行時照合 |
| SalesSupport:Portal:BaseUrl | 全アプリ | HTTPSの絶対URL、末尾スラッシュ。許可したPortalへのリンク生成用 |
| SalesSupport:DataProtection:KeyDirectory | 全アプリ | 配置領域・Web公開領域の外。実行アカウントのアクセス権が必要 |
| SalesSupport:Storage:TemporaryRoot | ファイル利用アプリ | 絶対パス、公開・配置領域外 |
| SalesSupport:Storage:PermanentRoot | 永続ファイル利用アプリ | 同上。一時領域と分離 |
| SalesSupport:Mail:Enabled | Portalはtrue、Toolはfalseが既定 | 未使用ツールにはSMTP設定を要求しない |
| SalesSupport:Mail:Host／Port／TlsMode | Mail有効時 | ポート1～65535。StartTls／SslOnConnectを明示 |
| SalesSupport:Mail:UserName／Password | 認証が必要な場合 | 一組で設定、ソース管理外 |
| SalesSupport:Mail:From／ReplyTo | From必須、ReplyTo任意 | メール形式、改行禁止 |
| SalesSupport:Mail:DevelopmentRecipient | 開発でMail有効時に必須 | 開発時は全宛先をこの1件へ置換、元宛先を本文に出さない |
| SalesSupport:Mail:TimeoutSeconds | 30秒 | 正の整数 |
| SalesSupport:Mail:MaxRecipients | 実SMTP確認時に設定 | 正の整数。超過時は送信前失敗、自動分割しない |
| SalesSupport:Http:TimeoutSeconds | 30秒 | 正の整数。呼出先ごとの上書きを許可 |
| SalesSupport:Logging:TimeoutSeconds | 3秒 | 正の整数。独立したログ処理の上限 |
| SalesSupport:Storage:CleanupTimeoutSeconds | 5秒 | 正の整数。清掃の待機上限 |
| SalesSupport:Proxy:KnownProxies | 既定空 | 信頼済みのIPだけを設定 |

- ASP.NET Core標準の設定プロバイダー順に従い、配置環境変数で上書き可能とする。`Portal__EnvironmentCode`との既存契約を維持する。
- Cookie名は `.SalesSupport.Auth`、スキームは `Identity.Application`、Data ProtectionのApplicationNameは `SalesSupport` に統一する。いずれもCommonの定数。Cookie Path=/、Domain未指定。別環境は別ホスト・別キー領域で分離し、同一Cookieが混在する配置を行わない。
- IOptions<T>＋ValidateOnStartを使用し、必要な機能の設定だけ検証する。設定の変更反映はアプリ再起動で行う。
- DB設定は起動時に全件固定しない。公開状態・アップロード条件は対象要求ごとに取得する。
- 保存ディレクトリとアクセス権は運用で用意する。Commonの起動処理で任意フォルダーの作成や権限変更をしない。
- 要求サイズはIISとホスト側で調整する。業務の最大容量はDBポリシーで別途必ず検証し、Configから緩和しない。

### DB設定取得

| メソッド | 戻り値 | ルール |
| --- | --- | --- |
| ISystemSettingsReader.GetPublicationStatusAsync | string | SITE＋PUBLICATION_STATUS。欠落・NULL・未定義は構成エラー |
| GetPrivateMessageAsync | string | 欠落・NULLは「メンテナンス中です。」 |
| GetBusinessDateAsync | DateOnly? | 開発だけTEST＋BUSINESS_DATEを参照。YYYY-MM-DDの完全一致で検証 |

汎用的な設定書換えAPIは提供しない。秘密情報と物理パスを画面・ProblemDetails・ログへ出さない。

## 5. Authentication

### 認証・要求処理

1. Cookie標準ハンドラーで復号・期限を検証する。
2. CommonのCookie検証イベントでUserId、初回認証時刻、SecurityStampを確認する。
3. CommonDbContextから現在のIsActive・RoleCode・SecurityStampを読み、無効・不一致・未定義を拒否する。Identity既定の間隔検証だけに依存しない。
4. ScopedのCurrentUserへ検証済み情報を置く。表示名・権限はDBの現行値を使用し、Cookieの古い表示値から判定しない。
5. Authorizationでサイト公開状態、対象ツールの状態、管理者権限を順に確認する。
6. Controllerへ渡す。リクエスト途中の状態変更による実行中処理の強制中断はしない。

| 公開契約 | 内容 |
| --- | --- |
| ICurrentUserAccessor.User | 検証済みCurrentUser?。未認証はnull |
| IAccessEvaluator.EvaluateAsync(AccessRequest) | AccessDecision。目的と対象ツールIDから判定 |
| AccessRequest | Purpose=Site／ToolDetail／ToolUse／ToolDownload／ToolManage、ToolId? |
| SalesSupportAdminポリシー | サイト入場＋ADMIN |
| SalesSupportToolポリシー | サイト入場＋ConfigのToolIdの利用可否 |
| ToolEntry属性 | WEB_OPEN対象の入口アクションに付与 |
| NoSlidingRenewal属性 | 自動ポーリング等、認証は必要だがCookie更新対象外の要求 |

Tools.StatusのPUBLIC／PRIVATE／HIDDENとRoleCodeの可否は02を正とする。ToolManageはADMINに限定し、HIDDENも編集できる。ToolUseではADMINでもHIDDENを拒否する。一覧の掲載条件と利用認可を共用の単一boolへまとめない。

- 各ツールのFallbackPolicyへSalesSupportToolを適用し、新規アクションへの付け忘れを防ぐ。Portalはサイト入場をFallbackPolicyとし、管理機能にADMINを追加する。
- ログイン・初回設定・再設定・案内表示等だけを明示的な例外にする。ログアウトは認証必須POSTだがサイト入場条件は不要。公開静的資産には機密データを置かない。
- DB障害では保護要求を503、未認証・失効はAPIで401、権限不足は403、存在しない／HIDDENの利用対象は404とする。サイトPRIVATEの一般ユーザーはAPIで403、HTMLではPortalの案内画面へ案内する。
- Cookie改ざん・無効ユーザー・Stamp不一致はCookieを失効させる。DB障害を「ユーザー不存在」とみなして恒久的にログアウトさせない。
- HTML未認証時はPortalへ戻り先付きで案内する。戻り先は同一サイトのローカルパスのみ許可し、スキーム相対URL、外部ホスト、制御文字を拒否する。POST本文を保存・自動再送しない。
- 更新要求へ標準CSRFを適用する。共通ヘッダーのログアウトは表示中アプリへのCSRF付きPOSTとする。Commonは共有Cookieを破棄してPortalへ戻す小さなログアウトハンドラーを提供し、Portalと各ツールに同じ処理を組み込む。ログイン・パスワード操作の画面と業務処理はPortalへ集約する。

### 時刻・Cookie更新

- ExpireTimeSpan=60分、SlidingExpiration=true、非永続。初回認証時のUTC Unix秒をAuthenticationPropertiesの `sales_support.initial_utc` へ記録する。
- ログイン成功時だけ初回時刻を作成し、チケット再発行でも維持する。欠落・不正・未来時刻は拒否する。全アプリの時刻同期を運用前提とする。
- 各保護要求で初回認証から8時間以上なら拒否する。Cookieの更新でこの上限は延長しない。
- CheckSlidingExpirationでは標準のShouldRenew判定を維持し、NoSlidingRenewal要求だけfalseにする。ユーザー情報更新時も対象外要求から不要な再発行をしない。
- 静的ファイルは認証処理より前に提供できる公開資産に限定する。保護ファイルは認証対象経路で取得する。
- 独自の無操作タイマー、Cookie時刻同期テーブル、セッション起動回数の重複排除は作らない。長時間入力だけでは延長されない旨は02の仕様を維持する。

### Portalとの境界

パスワードポリシー、禁止リスト、設定リンク発行・消費、UserManagerによるユーザー更新、初期管理者作成はPortalの詳細設計対象。CommonはApplicationUserとIdentityマッピング・共有Cookie契約を提供する。設定リンクの暗号・パスワードハッシュを独自実装しない。

## 6. MasterData・DateTime

| サービス・メソッド | 戻り値・動作 |
| --- | --- |
| ICodeMasterReader.GetOptionsAsync(codeType) | IReadOnlyList<CodeOption>。CodeValue、CodeName、SortOrder、ColorCode。SortOrder→CodeValue順 |
| ICodeMasterReader.FindAsync(codeType, codeValue) | CodeOption?。組合せで検索 |
| IApplicationClock.GetUtcNow() | DateTimeOffset。TimeProviderを使用 |
| IApplicationClock.ToJst(utc) | DateTimeOffset。UTCからJSTへ変換 |
| IBusinessDateProvider.GetTodayAsync | DateOnly。開発の指定日または実際のJST日付 |

- マスタ取得結果は同一要求内だけ共有する。未知の区分は入力エラー、既知区分で必須値が欠けていれば構成エラーとして選択保存を拒否する。
- 名称だけ欠落した既存行の表示は「不明」とし、認可や更新の許可には使用しない。ColorCode不正はCSSへ渡さず標準色にする。
- INQUIRY_TARGETは登録しない。Portalが固定のポータルサイト・その他と、許可されたToolsから対象選択肢を生成する。
- 実日時と業務日付を別インターフェースにし、認証・ログ・監査・採番へ業務日付を渡さない。本番の業務日付取得ではTEST設定を読み込まない。
- DateTimeという独自型名は使用しない。DBのdatetime2はUTCとして扱い、Kind未指定値のローカル時刻解釈を防ぐ。

## 7. Logging・ErrorHandling

### ログ契約

| メソッド | 入力 | 戻り値 |
| --- | --- | --- |
| IUsageLogger.WriteAsync(UsageEvent) | ToolId、EventType、ResultCode | LogWriteResult |
| IActivityLogger.WriteAsync(ActivityEvent) | EventType、ResultCode、FailureReason?、対象種別・ID?、許可済み変更コード? | LogWriteResult |
| ISystemErrorLogger.WriteAsync(SystemErrorEvent) | ErrorId、ErrorCode?、表示用でない安全なログ本文、例外種別?、安全化済みスタック? | LogWriteResult |

UserId、発生UTC日時、ApplicationName、要求内CorrelationIdはCommonが補う。未知ユーザーの認証失敗はUserId=nullとし、入力メールを保存しない。UsageEventには検証済みユーザーが必須。ツールのToolIdはConfig、Portalのダウンロード対象はDBから解決する。

- CorrelationIdは要求開始時にGUID生成し、同じ要求内で共有する。外部から任意文字列を採用しない。アプリ間HTTP連携が必要な場合だけ信頼済み内部要求として引継ぎを設計する。
- WEB_OPENはToolEntry付きアクションの正常な画面応答1回につき1件。失敗・リダイレクト・静的資産は対象外。内部APIや再計算は起動ログにしない。
- WEB_EXECUTEは業務処理の完了箇所で1回。成功・失敗を記録し、ControllerとServiceの両方で二重記録しない。キャンセルはFAILUREとして扱い、結果未確定なら成功を記録しない。
- DESKTOP_DOWNLOADは認可後の取得要求を1件記録し、取得準備の結果をSUCCESS／FAILUREとする。端末での受信完了を保証しない。
- LogDbContextを都度作成し、業務の変更追跡やトランザクションを共有しない。3秒の独立上限でawaitし、fire-and-forgetや永続キューは作らない。
- 書込み失敗はFailedを返し、再帰記録・代替ログ・自動再試行をしない。元の計算・保存結果を失敗へ変更しない。
- RequestPathはクエリ文字列を除き、パス中のトークンもマスクする。物理パス・メール・パスワード・入力本文を記録しない。EFの機密値ログを有効にしない。

### 操作コード

既存のINQUIRY_CLASSIFICATION_UPDATEを維持し、初期コードを次とする。ResultCodeはSQLのSUCCESS／FAILURE／DENIED、失敗理由は固定コード。管理操作のユーザー名・入力本文を補足情報へ含めない。

| EventType | 使用箇所 |
| --- | --- |
| LOGIN、LOGOUT、ACCOUNT_LOCK、ACCESS_DENIED | 認証・認可 |
| USER_CREATE、USER_UPDATE、USER_UNLOCK | ユーザー管理 |
| TOOL_UPDATE、TOOL_ORDER_UPDATE | ツール基本情報・順序 |
| NOTICE_CREATE、NOTICE_UPDATE | お知らせ操作 |
| VERSION_CREATE、VERSION_UPDATE、VERSION_DELETE、VERSION_SELECT | 履歴管理 |
| FILE_CREATE、FILE_REPLACE、FILE_UPDATE、FILE_DELETE、FILE_ORDER_UPDATE | 提供ファイル管理 |
| INQUIRY_CREATE、INQUIRY_UPDATE、INQUIRY_CLASSIFICATION_UPDATE | 問い合わせ |
| PREFERENCE_UPDATE、FAVORITE_ADD、FAVORITE_REMOVE | 個人設定 |
| MAIL_SEND | メール送信結果 |

FailureReasonはINVALID_CREDENTIALS、ACCOUNT_LOCKED、UNAUTHENTICATED、INACTIVE_USER、STAMP_MISMATCH、ROLE_DENIED、SITE_PRIVATE、TOOL_UNAVAILABLE、INVALID_INPUT、CONFLICT、DEPENDENCY_UNAVAILABLE、SEND_FAILED、SEND_UNKNOWN、NO_RECIPIENTS、CANCELLEDを初期集合とする。ユーザーに見せる文言と一致させず、認証画面は既定の共通メッセージを使う。

### エラー変換

`IErrorHandler.HandleAsync(ErrorRequest)` → ErrorPresentation。ErrorRequestの識別子はErrorCodeまたはToolId＋ErrorNoのどちらか一方。任意の例外と、許可したキーの関連情報を付けられる。

1. 要求内の発生ごとにErrorIdを生成する。
2. ErrorCodesを取得し、未登録・取得失敗は固定の一般エラー文言へ代替する。
3. 文言と安全な関連情報からログを1回だけ試行する。
4. 画面には表示文言とErrorIdのみ返す。APIには安全なProblemDetailsを返す。

ErrorHandling→Loggingの一方向とする。LoggingはErrorHandlingを呼ばない。Mail内で記録済みの失敗はErrorId／記録済み情報を結果へ付け、呼出元が再記録しない。入力エラーは400、競合409、予期しないエラー500、依存先障害503を基本とし、認証応答は5節に従う。

関連情報は行番号・処理段階・件数等の許可キーのみ。例外Message／Data／ToStringをそのまま保存しない。例外型とファイルパスを含まないスタックを抽出し、既知の秘密パターンを除去して列長へ切り詰める。不明な関連値は捨てる。

## 8. FileStorage

### 型とメソッド

| 契約 | 内容 |
| --- | --- |
| UploadPurpose | InquiryAttachment／UserImport／Reference／App／ToolInput |
| UploadPolicySnapshot | PolicyId、Purpose、ToolId?、MaxFileSizeBytes、Extensions |
| IUploadPolicyProvider.GetAsync(purpose, toolId?) | DBのポリシーと拡張子を一緒に取得 |
| IUploadValidator.ValidateAsync(stream, originalName, policy) | 検証結果。ストリームを読みながら実容量を制限 |
| IFileStorage.SaveTemporaryAsync(request, stream) | TemporaryFileHandle |
| IFileStorage.SavePermanentAsync(request, stream) | StoredFile |
| IFileStorage.OpenReadAsync(handle) | 読取りStream |
| IFileStorage.DeleteAsync(handle) | Deleted／NotFound／Failed |
| TemporaryFileHandle | サーバー発行識別子、所有スコープ、内部相対パス。IAsyncDisposable |
| StoredFile | サーバー生成相対パス、正規化拡張子、実容量、元ファイル名 |

保存メソッドは内部で検証を必ず適用し、Validateだけを呼んだことを信頼しない。非seek Streamは事前検証で読み尽くしてから保存せず、保存中に一度だけ読みながら検証する。検証だけを行うメソッドを使用した場合、そのStreamを消費する契約とする。外部から任意の保存先・ToolId・PolicyIdを指定させず、認可済みの業務情報からrequestを構築する。ファイル認可は呼出元、パスと容量の検証はCommonが担当する。

### 処理規則

- ユーザー取込はSITE＋USER_IMPORT（.tsv、1,000,000バイト）、問い合わせはSITE＋INQUIRY_ATTACHMENT、参考資料・配布アプリはTOOL_COMMON、ツール入力はTOOL＋TOOL_INPUT。用途・ToolIdの組合せ不正、ポリシー欠落、拡張子0件、容量不正、DB障害は保存前に拒否する。
- パスを除いた元ファイル名の最終拡張子を小文字化して完全一致で検証する。空名・制御文字・拡張子なしは拒否する。圧縮ファイルを自動展開しない。
- Content-Lengthだけで判定せず、上限を超えた時点で書込み停止・途中ファイル削除を行う。
- 物理名はGUID等で発行し、CreateNewで作成する。一時ファイルは用途・ツール別領域、永続ファイルはシステム生成相対パスとする。
- ルートと結合後の絶対パスを正規化し、区切り付きルート配下であることを検証する。絶対パス、親移動、別ボリューム、リンク・再解析ポイントによる領域外アクセスを拒否する。利用者がルート配下へリンクを作成できないACLとする。
- 入力Streamは呼出元が所有する。Commonが開いたStreamは呼出元がDisposeする。OpenRead結果をHTTPへ渡す場合はレスポンス処理終了まで維持する。
- 入力一時ファイルは業務処理のfinally／await usingで清掃する。ダウンロード用一時ファイルは送信処理のfinallyでStreamを閉じてから清掃し、切断時も同じ扱いにする。
- 清掃失敗はログを1回だけ試行し、成功した業務を取り消さない。正常清掃が走らないプロセス停止時の残存分は運用対象。OnCompletedだけを唯一の清掃手段にしない。
- 永続ファイルは新規保存→PortalのToolFiles更新確定→旧ファイル削除。DB更新失敗は新ファイルを清掃して旧参照を維持する。CommonからToolFilesを直接更新しない。
- FileIdはDBのIDENTITYであり、Commonで採番しない。保存先物理名はFileIdに依存しないGUIDとする。
- 配信はattachment固定。HTML等も実行表示しない。元ファイル名をヘッダーへ直接連結せず、標準のダウンロード応答を使う。

## 9. Mail

`IMailSender.SendAsync(MailRequest)` → `MailSendResult(Outcome, ErrorId?, FailureReason?)`。

MailRequestはTo／Cc／Bccの宛先、Subject、プレーンテキスト本文、添付のサーバー側Handleからなる。ユーザー入力から宛先や添付パスを直接作らない。件名・アドレスのCR/LFを拒否し、宛先を重複排除する。

- MailKitのSMTP接続を1送信単位で生成・切断・破棄する。認証方式はConfigで与え、証明書検証を無効化しない。
- 対象ユーザーの選定と通知許可判定はPortal、SMTP・MIME組立と送信結果はCommon。Commonは受信対象のDB検索やMailSentAt更新をしない。
- 成功はSMTPの受付完了を意味し、配達確認を意味しない。一部拒否はFailed、送信後の応答喪失・送信途中タイムアウトはUnknownとする。どちらも自動再送しない。
- 接続・認証・送信前検証の失敗はFailed。送信後のキャンセルで結果が断定できない場合もUnknown。呼出元は02の失敗フローへ進める。
- 添付の読取りStreamは送信完了まで保持して破棄する。一時Handleの削除は業務処理終了時の呼出元が担当する。
- 開発環境は実宛先をすべて除去しDevelopmentRecipientだけへ送信する。未指定では送信しない。
- 宛先0件はSMTPへ接続せずNO_RECIPIENTS、上限超過はINVALID_INPUTとして返す。お知らせを複数メールへ自動分割しない。
- メールテンプレートは `IMailTemplateRenderer.Render(templateKey, model)` → Subject＋Body。Commonが整形を担当し、業務用モデル・対象選定はPortalが渡す。
- 初期設定・再設定リンクを含む本文・URL・宛先をログへ出さない。失敗ログの詳細は送信段階・固定理由コードに限定する。
- From／Reply-To、文面の最終校正、お知らせTO、SMTPの上限は配置・Portal運用の残件。共通送信器の実装は実値を後から設定できる。

## 10. HttpClients

初期の共通ユーザーAPIはブラウザーからPortalを呼ぶためのもので、ツールのサーバーからPortalへCookieを転送して認証照会する方式は採らない。サーバー側はCommonのDB検証を使う。

- 実際の外部HTTP連携が必要なツールだけ、IHttpClientFactoryの名前付きクライアントを登録する。
- 共通処理はタイムアウト、JSON直列化、安全なエラー変換に限定する。全HTTPを包む汎用RPC基盤は作らない。
- 接続先は検証済みBaseAddressと固定・エンコード済み相対パス。ユーザー指定の任意URLを実行しない。
- 成功、HTTP失敗（StatusCodeあり）、通信失敗、タイムアウト、キャンセルを区別する。非成功本文・Authorization・Cookieをログへ出さない。
- 初期はGETを含め自動再試行なし。副作用のあるPOST等は送信後の結果不明を保持し、成功扱いにも自動再送にも変えない。
- 認証は各接続先に応じた設定とし、Portalの認証Cookieを外部へ送らない。
- 呼出先・認証契約が未定のツールについて、仮の接続先や専用DTOを先に作らない。

## 11. DataExport・Validation

### DataExport

`IDelimitedTextWriter.WriteAsync(Stream destination, ExportDefinition definition, IAsyncEnumerable<ExportRow> rows)`。

- ExportDefinitionはCSV／TSV、順序付き列定義、見出し。行数上限を暗黙設定してデータを切り捨てない。
- UTF-8 BOM、CRLF、見出しあり、0件は見出しのみ。出力先Streamは呼出元所有で閉じない。
- 改行CRLF／CR／LF・タブを半角スペースへ整形し、文字列の先頭空白を読み飛ばした位置が =、+、-、@ の場合は先頭にアポストロフィを付ける。CSVではさらに引用符を二重化し、カンマ・引用符を含むセルを引用符で囲む。
- 型付き数値はInvariantCultureの数値として出力し、ユーザー由来文字列とは区別する。NULLは空欄、boolは1／0、GUIDはD形式、dateはyyyy-MM-dd、UTC日時はJSTへ変換してyyyy-MM-dd HH:mm:ss.fff、offset付き日時はJSTオフセット付きで出力する。
- 大量行はストリームへ順次出力し、結果全体をメモリへ保持しない。読取り途中の失敗は応答開始後なら接続を中断し、途中ファイルを成功扱いしない。
- ファイル名と集計・DB検索・管理者認可はPortalの責務。SQLへユーザー指定のテーブル名や列名を連結しない。

### Validation

- 共通の必須文字列、文字数、コード、メール形式、ローカル戻り先、拡張子、バージョンの形式検証を提供する。業務固有の公開条件・担当者権限はServiceで検証する。
- DB列の上限に合わせUTF-16単位で文字数を計測する。任意文字列の空欄はnull、必須の空白だけの値はエラー。
- パスワードはPortalのIdentity検証へ渡し、Trim・文字置換・切り詰めをしない。
- バージョンは半角数字3組、各組0～99、複数桁の先頭ゼロ禁止。完全一致の正規表現 `\A(?:0|[1-9][0-9]?)\.(?:0|[1-9][0-9]?)\.(?:0|[1-9][0-9]?)\z` で検証し、数値比較する。
- サーバー側検証を正とし、Razorの入力検証は同じ条件と文言へ対応させる。FieldErrorをModelStateへ追加するヘルパーを提供する。

## 12. 共通UIとHTTP DTO

### UI

| 部品 | 入力・担当 |
| --- | --- |
| _SalesSupportLayout.cshtml | PageShellModel、RenderBody、画面固有Scripts |
| SalesSupportHeader ViewComponent | 検証済みユーザー、環境帯、Portalの固定リンク |
| Breadcrumbs | ラベルと検証済みサイト内URLの配列 |
| CodeBadge TagHelper | codeType、codeValue。名称・色取得、HTMLエンコード |
| ValidationSummary／FieldError | ModelStateのエラー |
| OperationMessage | 保存結果・競合・入力失敗の画面内表示 |
| CommonError | ErrorPresentation。ErrorId表示、例外・物理パスは非表示 |

- PageShellModelはPageTitle、Breadcrumbs、CurrentToolName?を持つ。ユーザーや権限を画面入力から受け取らない。
- CSS／JSはRCLの `_content/SalesSupport.Common/` を使用し、各アプリのPathBaseを考慮する。Portalへのリンクとツール内リンクを区別する。
- Portalと各ツールはレイアウトを指定し、本文だけ実装する。Bootstrap等をホストとCommonで二重読込みしない。
- モックの固定ユーザー、固定色・データ、同名関数の重複は移植せず、仕様に対応する部品へ置換する。
- 管理メニューの非表示だけで認可済みとしない。固定ヘッダー・同幅メニュー・キーボード操作・画面内競合案内は02を維持する。
- ログアウトは表示中アプリの認証必須POSTハンドラーへ送信する。各アプリ自身が発行したCSRFトークンを検証し、Commonの処理で共有Cookieを削除する。サイトPRIVATEやツールHIDDENでもログアウトは許可し、完了後はPortalへ戻す。追加の確認画面とGETによる状態変更は設けない。

### Portal APIの共有契約

Common.ContractsにDTOだけ置き、Controllerと保存処理はPortalに置く。

| 経路 | 入出力 |
| --- | --- |
| GET /api/users/me | UserId、DisplayName、RoleCode |
| GET /api/users/me/preferences | SystemNoticeMailEnabled、FavoriteToolNoticeMailEnabled、UpdateCount |
| PUT /api/users/me/preferences | 上記2フラグ＋取得時UpdateCount。成功は保存後の同じDTO |

- 自分のユーザーIDは認証情報から取得し、更新本文に任意UserIdを受け付けない。
- 401／403／400／409／503を使い分け、安全なProblemDetailsを返す。SecurityStamp、パスワード、トークンはDTOへ含めない。
- 更新はCSRF必須。Portalページから同一オリジンで呼ぶ。トークンは `X-CSRF-TOKEN` ヘッダーで送る。
- ツールページが通知設定を操作する必要がある場合はPortalの個人設定画面へ遷移する。初期はツールとPortal間のCSRFトークン共有APIを追加しない。

## 13. ホストへの組込み順序

1. 設定読込み、AddSalesSupportCommon、ホストDbContext・Identity（Portalのみ）、MVC・認可を登録。
2. 信頼済みプロキシ設定を適用し、CorrelationIdと共通例外処理を開始。
3. HTTPS、公開静的資産、ルーティングを適用。
4. AuthenticationでCookieと現行ユーザーを検証。
5. Authorizationでサイト・ツール・管理者ポリシーを適用。
6. MVCのCSRF・入力検証を適用し、Controller→Serviceを実行。
7. 必要な利用ログ・操作ログを1回記録。Streamと一時ファイルを清掃。

EndpointメタデータをCookie更新判定で参照できるよう、ルーティングを認証より前に配置する。Cookie標準イベントを置き換える際はIdentityの検証責務を落とさず、本書の要求ごとの照合へ明示的に統合する。

## 14. 検証計画・残件

### 実装時の受入確認

| 対象 | 確認内容 |
| --- | --- |
| 認証 | Portal→Tool001→Tool002の往復、期限更新、最大8時間、Stamp変更・無効化、DB障害時の拒否 |
| 認可 | USER／ADMIN×サイト2状態×ツール3状態、直接URL、API、ファイル、管理機能の例外 |
| Cookie更新 | NoSlidingRenewal、自動ポーリング、静的資産で延長しない。戻り先の外部URL拒否 |
| Entity・更新 | 既存DDLとの列対応、DB生成GUID、監査値再取得、同時更新拒否、Identityと設定の原子的保存 |
| ログ | 書込み失敗で業務成功を維持、二重記録なし、機密値除去、起動と計算の記録単位 |
| ファイル | 上限境界・非seek Stream・途中切断・領域外パス・再解析ポイント・DB失敗・清掃失敗 |
| Mail | 開発宛先置換、一部拒否、結果不明、タイムアウト、添付破棄、自動再送なし |
| 出力 | NULL・日本語・引用符・数式先頭・改行・タブ・大量行・途中失敗 |
| UI | Portalルート／ツールPathBaseで共通資産・リンク・CSRF・権限表示・エラー表示 |

### 実装前に整合させる既存SQL

2026-09-21に新規構築SQLを整合修正した。以下は検出時の差分記録であり、公開状態NULL、DBユーザー・監査保持、再入判定、不要な一意制約、確定済み形式、ダミーの削除と環境ガードは修正済み。検証方法・適用範囲は[SQL README](../SalesSupport/sql/README.md)を参照。EF統合、実運用DBへの移行、既存データの移行判断等は引き続き対象外。

- SystemSettingsの公開状態CHECKはNULLを明示拒否する必要がある。現在のIN条件だけではSQLのUNKNOWN評価でNULLが通る。
- トリガーのDBユーザー取得はORIGINAL_LOGINであり、DB設計の「DBユーザー」と一致していない。DBユーザーを記録するUSER_NAMEへ合わせる。作成日時と更新日時の初期一致・作成監査列の保持も共通トリガーのテスト対象にする。
- 監査トリガーの再帰防止は対象トリガー自身の再入だけを判定し、別トリガーからの正当な更新を一律除外しない。
- CodeMasterの表示順やカテゴリ名称へ、基本設計にない一意制約が加わっている。FAQのカテゴリ内表示順など合意済み制約を除き、不要な一意制約を削除する方向でDDLを整合させる。
- バージョン、日付、拡張子のCHECKは部分的な形式確認に留まる。実装の検証器とDB側の拒否条件を揃える。部分Entityからの更新禁止はCommonDbContextでも確認する。
- ダミーSQLの削除対象の識別・再実行時に追加されたテストデータの扱いを見直す。環境名の自己申告だけで本番を識別したとは扱わない。

### 外部情報・業務判断として残すもの

- SMTP実値、From／Reply-To、お知らせTO・宛先上限、文面、開発送信先。
- 配置先、鍵フォルダー・ACL、証明書、信頼するプロキシ、IIS上限。
- 禁止パスワードリストの出所・内容・更新担当。設定リンク発行番号の原子的更新はPortal詳細設計に定義済み。
- 各ツール固有入力・出力・外部HTTP接続。
- 実SMTP、IIS配置でのCookie往復、SQL Serverの同時更新・行ロックの実証。本書の作成時点では未検証。

## 15. 技術資料

- [Microsoft：Razor Class LibraryによるMVC Viewsと静的資産の共有](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/ui-class?view=aspnetcore-10.0)
- [Microsoft：共有認証Cookie](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0)
- [Microsoft：UseSqlOutputClauseとトリガー対象テーブル](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.sqlserverentitytypebuilderextensions.usesqloutputclause?view=efcore-10.0)
- [ASP.NET Core：Cookie更新判定の実装](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs)
