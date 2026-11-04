/*
  ツール1件をPortalへ登録するひな形。開発・本番のどちらのDBにも使用できる。
  ファイルをコピーし、「記入欄」を書き換えてから、接続中のDBを確認してファイル全体を実行する。
  手順と公開までの流れは同じフォルダーの上位にある README.md の「新しいツールを登録する」を参照する。
  再実行時は既存物を上書きせず停止する。状態は非公開（HIDDEN）で登録し、確認後に公開状態を変更する。
*/
SET XACT_ABORT ON;
-- アップロード条件の表はフィルター索引を持つため、sqlcmdの既定値（OFF）でも登録できるようにする。
SET QUOTED_IDENTIFIER ON;

/* ---- 記入欄 ---- */
DECLARE @ExpectedDatabase sysname = N'__DB名を入力__';
DECLARE @ToolId varchar(20) = '__ToolIdを入力__';
DECLARE @ToolName nvarchar(200) = N'__ツール名を入力__';
DECLARE @ToolSummary nvarchar(1000) = NULL;
-- 提供種別はWEB、DESKTOP、DOCUMENTのいずれか。WEBの起動先は標準の/tools/<ToolId>/appで登録する。
DECLARE @ToolType varchar(20) = 'WEB';
-- 担当者にする有効なADMINのログインID。
DECLARE @OwnerLoginId nvarchar(256) = N'__担当管理者のログインIDを入力__';
-- 利用できる一般ロール。ADMINは割当てなしで全ツールを扱えるため登録しない。ログインなしの公開（匿名モード）はロールAで判定する。
DECLARE @Roles TABLE (RoleCode varchar(20) NOT NULL PRIMARY KEY);
INSERT @Roles (RoleCode) VALUES ('A');
-- 一時アップロードを使うツールだけ、上限バイト数と拡張子（小文字、ピリオド付き）を指定する。使わない場合はNULLと空のままにする。
DECLARE @UploadMaxFileSizeBytes bigint = NULL;
DECLARE @UploadExtensions TABLE (Extension varchar(20) NOT NULL PRIMARY KEY);
-- INSERT @UploadExtensions (Extension) VALUES ('.tsv');
/* ---- 記入欄ここまで ---- */

IF @ExpectedDatabase = N'__DB名を入力__' OR DB_NAME() <> @ExpectedDatabase
    THROW 52000, N'期待するDB名を記入し、接続中のDB名と一致させてください。', 1;
IF @ToolId = '__ToolIdを入力__' OR @ToolName = N'__ツール名を入力__' OR @OwnerLoginId = N'__担当管理者のログインIDを入力__'
    THROW 52001, N'記入欄のToolId、ツール名、担当管理者を入力してください。', 1;
IF EXISTS (SELECT 1 FROM portal.Tools WHERE ToolId = @ToolId)
    THROW 52002, N'同じToolIdが既にあります。上書きしません。', 1;
IF EXISTS (SELECT 1 FROM @Roles WHERE RoleCode = 'ADMIN')
 OR EXISTS (SELECT 1 FROM @Roles AS input WHERE NOT EXISTS (SELECT 1 FROM portal.Roles AS role WHERE role.RoleCode = input.RoleCode))
    THROW 52003, N'ロールには登録済みの一般ロールだけを指定してください。', 1;
IF (@UploadMaxFileSizeBytes IS NULL AND EXISTS (SELECT 1 FROM @UploadExtensions))
 OR (@UploadMaxFileSizeBytes IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @UploadExtensions))
    THROW 52004, N'アップロードの上限バイト数と拡張子は両方を指定するか、両方とも空にしてください。', 1;

DECLARE @OwnerUserId uniqueidentifier =
    (SELECT UserId FROM portal.AspNetUsers WHERE LoginId = @OwnerLoginId AND RoleCode = 'ADMIN' AND IsActive = 1);
IF @OwnerUserId IS NULL
    THROW 52005, N'担当管理者には有効なADMINのログインIDを指定してください。', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    INSERT portal.Tools (ToolId, ToolName, ToolSummary, OwnerUserId, ToolType, WebAppUrl, Status, SortOrder)
    SELECT @ToolId, @ToolName, @ToolSummary, @OwnerUserId, @ToolType,
           CASE WHEN @ToolType = 'WEB' THEN N'/tools/' + CONVERT(nvarchar(20), @ToolId) + N'/app' END,
           'HIDDEN', ISNULL(MAX(SortOrder), 0) + 10
      FROM portal.Tools;
    INSERT portal.ToolRoles (ToolId, RoleCode) SELECT @ToolId, RoleCode FROM @Roles;
    IF @UploadMaxFileSizeBytes IS NOT NULL
    BEGIN
        INSERT portal.FileUploadPolicies (ScopeType, ToolId, PurposeCode, MaxFileSizeBytes)
        VALUES ('TOOL', @ToolId, 'TOOL_INPUT', @UploadMaxFileSizeBytes);
        DECLARE @PolicyId int = SCOPE_IDENTITY();
        INSERT portal.FileUploadPolicyExtensions (PolicyId, Extension) SELECT @PolicyId, Extension FROM @UploadExtensions;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
