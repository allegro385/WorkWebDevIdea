SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

IF N'$(EnvironmentName)' <> N'DEVELOPMENT'
    THROW 50900, N'ダミーデータはEnvironmentName=DEVELOPMENTを明示した場合だけ投入できます。', 1;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'SalesSupport.AllowDummyData' AND CONVERT(nvarchar(10), value) = N'YES')
    THROW 50903, N'開発専用DBへのAllowDummyData指定が必要です。本番DBへ設定しないでください。', 1;

IF OBJECT_ID(N'portal.Tools', N'U') IS NULL
    THROW 50901, N'先に001_CreateTables.sqlと002_SeedMasterData.sqlを実行してください。', 1;

DECLARE @AdminUserId uniqueidentifier =
(
    SELECT TOP (1) UserId FROM portal.AspNetUsers
     WHERE RoleCode = 'ADMIN' AND IsActive = 1
     ORDER BY UserId
);
DECLARE @GeneralUserId uniqueidentifier =
(
    SELECT TOP (1) UserId FROM portal.AspNetUsers
     WHERE RoleCode = 'USER' AND IsActive = 1
     ORDER BY UserId
);

IF @AdminUserId IS NULL OR @GeneralUserId IS NULL
    THROW 50902, N'UserManager経由で有効なADMINユーザーとUSERユーザーを一名以上登録してから実行してください。', 1;

BEGIN TRANSACTION;

/* 既存データは削除しない。衝突時は停止し、追加された確認データも保持する。 */
IF EXISTS (SELECT 1 FROM portal.Tools WHERE ToolId IN ('DMY-WEB', 'DMY-DESK', 'DMY-DOC'))
 OR EXISTS (SELECT 1 FROM portal.Inquiries WHERE InquiryId IN ('209912310001', '209912310002', '209912310003'))
 OR EXISTS (SELECT 1 FROM portal.ToolCategories WHERE CategoryName LIKE N'[[]DUMMY]%')
 OR EXISTS (SELECT 1 FROM portal.FaqCategories WHERE CategoryName LIKE N'[[]DUMMY]%')
 OR EXISTS (SELECT 1 FROM portal.Notices WHERE Title LIKE N'[[]DUMMY]%')
 OR EXISTS (SELECT 1 FROM log.SystemErrorLogs WHERE ErrorId = '00000000-0000-0000-0000-00000000E001')
    THROW 50904, N'既存ダミーデータと衝突します。再投入には新しい開発専用DBを使用してください。既存データは削除しません。', 1;

INSERT portal.ToolCategories (CategoryName, SortOrder) VALUES
(N'[DUMMY] 顧客管理', 10),
(N'[DUMMY] 提案・見積', 20),
(N'[DUMMY] 営業資料', 30);

DECLARE @CustomerCategoryId int = (SELECT CategoryId FROM portal.ToolCategories WHERE CategoryName = N'[DUMMY] 顧客管理');
DECLARE @EstimateCategoryId int = (SELECT CategoryId FROM portal.ToolCategories WHERE CategoryName = N'[DUMMY] 提案・見積');
DECLARE @DocumentCategoryId int = (SELECT CategoryId FROM portal.ToolCategories WHERE CategoryName = N'[DUMMY] 営業資料');

INSERT portal.Tools (ToolId, CategoryId, ToolName, ToolSummary, Remarks, OwnerUserId, ToolType, WebAppUrl, Status, SortOrder) VALUES
('DMY-WEB', @CustomerCategoryId, N'[DUMMY] 顧客情報検索ツール', N'顧客の基本情報と対応履歴を検索するWebツールです。', N'開発・画面確認用のダミーデータです。', @AdminUserId, 'WEB', N'/tools/DMY-WEB/app', 'PUBLIC', 10),
('DMY-DESK', @EstimateCategoryId, N'[DUMMY] 見積作成支援ツール', N'見積書作成を支援するデスクトップツールです。', N'実際の配布ファイルは登録されていません。', @AdminUserId, 'DESKTOP', NULL, 'PRIVATE', 10),
('DMY-DOC', @DocumentCategoryId, N'[DUMMY] 提案資料テンプレート集', N'提案資料を提供する資料型ツールの表示確認用です。', NULL, @AdminUserId, 'DOCUMENT', NULL, 'HIDDEN', 10);

INSERT portal.ToolVersionHistories (ToolId, Version, ModifiedByUserId, ChangeDescription, ReleasedAt, IsCurrent) VALUES
('DMY-WEB', '1.0.0', @AdminUserId, N'初回リリース', '2026-08-01', 0),
('DMY-WEB', '1.1.0', @AdminUserId, N'検索条件を追加しました。', '2026-09-01', 1),
('DMY-DESK', '2.0.0', @AdminUserId, N'帳票レイアウトを更新しました。', '2026-09-05', 1);

INSERT portal.Notices (NoticeType, ToolId, Title, Content, IsPublished, MailSentAt) VALUES
('SYSTEM', NULL, N'[DUMMY] システムメンテナンスのお知らせ', N'開発環境の表示確認用お知らせです。', 1, NULL),
('TOOL', 'DMY-WEB', N'[DUMMY] 検索条件を追加しました', N'検索条件の追加を想定したダミーのお知らせです。', 1, SYSUTCDATETIME()),
('TOOL', 'DMY-DESK', N'[DUMMY] 新しい版を公開しました', N'限定公開ツールのお知らせ表示確認用です。', 1, NULL);

INSERT portal.FaqCategories (CategoryName, SortOrder) VALUES
(N'[DUMMY] ログイン・アカウント', 10),
(N'[DUMMY] ツールの利用', 20);

DECLARE @LoginFaqCategoryId int = (SELECT CategoryId FROM portal.FaqCategories WHERE CategoryName = N'[DUMMY] ログイン・アカウント');
DECLARE @ToolFaqCategoryId int = (SELECT CategoryId FROM portal.FaqCategories WHERE CategoryName = N'[DUMMY] ツールの利用');

INSERT portal.FaqItems (CategoryId, Question, Answer, SortOrder, IsPublished) VALUES
(@LoginFaqCategoryId, N'パスワードを忘れた場合はどうすればよいですか？', N'ログイン画面のパスワード再設定から手続きしてください。', 10, 1),
(@LoginFaqCategoryId, N'アカウントがロックされた場合はどうすればよいですか？', N'時間をおいて再度試すか、システム管理者へ連絡してください。', 20, 1),
(@ToolFaqCategoryId, N'処理結果はどこに保存されますか？', N'処理結果はサイトへ恒久保存されません。必要な結果をダウンロードしてください。', 10, 1),
(@ToolFaqCategoryId, N'限定公開ツールを利用できますか？', N'限定公開ツールはシステム管理者だけが利用できます。', 20, 1);

INSERT portal.UserToolFavorites (UserId, ToolId) VALUES (@GeneralUserId, 'DMY-WEB');

INSERT portal.Inquiries
    (InquiryId, CategoryCode, TargetType, ToolId, SubmittedByUserId, Content, Status, AssigneeUserId, AdminNote)
VALUES
('209912310001', 'QUESTION', 'TOOL', 'DMY-WEB', @GeneralUserId, N'検索条件の指定方法を教えてください。', 'IN_PROGRESS', @AdminUserId, N'操作手順を確認中。'),
('209912310002', 'PROBLEM', 'PORTAL', NULL, @GeneralUserId, N'ポータルの画面表示が崩れることがあります。', 'ACTION_REQUIRED', NULL, N'ブラウザー情報の確認が必要。'),
('209912310003', 'REQUEST', 'OTHER', NULL, @GeneralUserId, N'新しい営業資料の掲載を希望します。', 'UNDER_REVIEW', @AdminUserId, NULL);

INSERT log.ToolUsageLogs (ToolId, UserId, OccurredAt, EventType, ResultCode, CorrelationId) VALUES
('DMY-WEB', @GeneralUserId, DATEADD(minute, -30, SYSUTCDATETIME()), 'WEB_OPEN', 'SUCCESS', '00000000-0000-0000-0000-00000000D001'),
('DMY-WEB', @GeneralUserId, DATEADD(minute, -29, SYSUTCDATETIME()), 'WEB_EXECUTE', 'SUCCESS', '00000000-0000-0000-0000-00000000D001'),
('DMY-DESK', @GeneralUserId, DATEADD(day, -1, SYSUTCDATETIME()), 'DESKTOP_DOWNLOAD', 'SUCCESS', '00000000-0000-0000-0000-00000000D002');

INSERT log.UserActivityLogs
    (UserId, OccurredAt, EventType, ResultCode, FailureReason, RequestPath, OperationTargetType, OperationTargetId, OperationDetails, IpAddress, CorrelationId)
VALUES
(@GeneralUserId, DATEADD(minute, -35, SYSUTCDATETIME()), 'LOGIN', 'SUCCESS', NULL, N'/account/login', NULL, NULL, NULL, '127.0.0.1', '00000000-0000-0000-0000-00000000D001'),
(@AdminUserId, DATEADD(minute, -10, SYSUTCDATETIME()), 'INQUIRY_CLASSIFICATION_UPDATE', 'SUCCESS', NULL, N'/admin/inquiries/209912310001', 'INQUIRY', '209912310001', N'CategoryCode:QUESTION->QUESTION;TargetType:PORTAL->TOOL', '127.0.0.1', '00000000-0000-0000-0000-00000000D002');

INSERT log.SystemErrorLogs
    (ErrorId, ErrorCode, OccurredAt, ApplicationName, ToolId, UserId, ErrorLevel, ErrorType, ErrorMessage, StackTrace, RequestPath, HttpMethod, CorrelationId)
VALUES
('00000000-0000-0000-0000-00000000E001', 'SYS_UNEXPECTED_ERROR', DATEADD(day, -1, SYSUTCDATETIME()), 'Portal', NULL, @GeneralUserId, 'ERROR', N'DummyException', N'ダミーデータとして登録したシステムエラーです。', NULL, N'/dummy/error', 'GET', '00000000-0000-0000-0000-00000000D002');

COMMIT TRANSACTION;
