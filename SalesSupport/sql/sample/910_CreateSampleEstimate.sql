/*
  見積試算・案件記録の開発専用セットアップ。実DB・本番DBへ適用しない。
  実行前に期待DB名を記入し、DBの拡張プロパティ EnvironmentName=DEVELOPMENT、
  SalesSupport.AllowSampleData=1 を管理者が別途設定する。スクリプトは許可標識を作らない。
  再実行時は既存物を上書きせず停止する。
*/
SET XACT_ABORT ON;
DECLARE @ExpectedDevelopmentDatabase sysname = N'__開発DB名を入力__';

IF @ExpectedDevelopmentDatabase = N'__開発DB名を入力__' OR DB_NAME() <> @ExpectedDevelopmentDatabase
    THROW 51900, N'期待する開発DB名を記入し、接続中のDB名と一致させてください。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'EnvironmentName' AND CONVERT(nvarchar(128), value) = N'DEVELOPMENT')
    THROW 51901, N'EnvironmentName=DEVELOPMENT のDBだけに適用できます。', 1;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = N'SalesSupport.AllowSampleData' AND CONVERT(nvarchar(128), value) = N'1')
    THROW 51902, N'SalesSupport.AllowSampleData=1 の明示的な許可標識が必要です。', 1;
IF OBJECT_ID(N'portal.Tools', N'U') IS NULL OR OBJECT_ID(N'portal.FileUploadPolicies', N'U') IS NULL
    THROW 51903, N'先にPortalの001と002を適用してください。', 1;
IF OBJECT_ID(N'dbo.SampleEstimateRecords', N'U') IS NOT NULL
    THROW 51904, N'サンプルの表が既にあります。再実行しないでください。', 1;
IF EXISTS (SELECT 1 FROM portal.Tools WHERE ToolId = 'SAMPLE-ESTIMATE')
    THROW 51905, N'同じToolIdが既にあります。上書きしません。', 1;
IF EXISTS (SELECT 1 FROM portal.ToolCategories WHERE CategoryName = N'[SAMPLE] 見積試算')
    THROW 51906, N'サンプル分類が既にあります。上書きしません。', 1;

DECLARE @OwnerUserId uniqueidentifier =
    (SELECT TOP (1) UserId FROM portal.AspNetUsers WHERE RoleCode = 'ADMIN' AND IsActive = 1 ORDER BY UserId);
IF @OwnerUserId IS NULL
    THROW 51907, N'PortalのIdentity APIから有効なADMINユーザーを登録してください。', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    CREATE TABLE dbo.SampleEstimateRecords
    (
        RecordId uniqueidentifier NOT NULL CONSTRAINT PK_SampleEstimateRecords PRIMARY KEY,
        SubmissionId uniqueidentifier NOT NULL,
        OwnerUserId uniqueidentifier NOT NULL,
        ProjectName nvarchar(60) NOT NULL,
        AppliedOn date NOT NULL,
        CategoryCode varchar(20) NOT NULL,
        Quantity int NOT NULL,
        UnitPrice int NOT NULL,
        Subtotal decimal(18,0) NOT NULL,
        DiscountAmount decimal(18,0) NOT NULL,
        Total decimal(18,0) NOT NULL,
        DetailRowCount int NULL,
        UpdateCount int NOT NULL CONSTRAINT DF_SampleEstimateRecords_UpdateCount DEFAULT (0),
        CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_SampleEstimateRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_SampleEstimateRecords_CreatedBy DEFAULT USER_NAME(),
        UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_SampleEstimateRecords_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_SampleEstimateRecords_UpdatedBy DEFAULT USER_NAME(),
        CONSTRAINT FK_SampleEstimateRecords_User FOREIGN KEY (OwnerUserId) REFERENCES portal.AspNetUsers(UserId),
        CONSTRAINT CK_SampleEstimateRecords_ProjectName CHECK (LEN(LTRIM(RTRIM(ProjectName))) > 0),
        CONSTRAINT CK_SampleEstimateRecords_Category CHECK (CategoryCode IN ('STANDARD','CAMPAIGN')),
        CONSTRAINT CK_SampleEstimateRecords_Quantity CHECK (Quantity BETWEEN 1 AND 9999),
        CONSTRAINT CK_SampleEstimateRecords_UnitPrice CHECK (UnitPrice BETWEEN 0 AND 9999999),
        CONSTRAINT CK_SampleEstimateRecords_Total CHECK (Subtotal >= 0 AND DiscountAmount >= 0 AND Total = Subtotal - DiscountAmount),
        CONSTRAINT CK_SampleEstimateRecords_DetailCount CHECK (DetailRowCount IS NULL OR DetailRowCount >= 0)
    );
    CREATE UNIQUE INDEX UX_SampleEstimateRecords_SubmissionId ON dbo.SampleEstimateRecords(SubmissionId);
    CREATE INDEX IX_SampleEstimateRecords_OwnerUserId_AppliedOn ON dbo.SampleEstimateRecords(OwnerUserId, AppliedOn);
    EXEC(N'CREATE TRIGGER dbo.TR_SampleEstimateRecords_AuditUpdate ON dbo.SampleEstimateRecords AFTER INSERT, UPDATE AS
    BEGIN
        SET NOCOUNT ON;
        IF TRIGGER_NESTLEVEL(@@PROCID, ''AFTER'', ''DML'') > 1 RETURN;
        DECLARE @AuditNow datetime2(3) = SYSUTCDATETIME();
        UPDATE target
           SET UpdateCount = CASE WHEN deletedRow.CreatedAt IS NULL THEN 0 ELSE deletedRow.UpdateCount + 1 END,
               CreatedAt = COALESCE(deletedRow.CreatedAt, @AuditNow),
               CreatedBy = COALESCE(deletedRow.CreatedBy, USER_NAME()),
               UpdatedAt = @AuditNow,
               UpdatedBy = USER_NAME()
          FROM dbo.SampleEstimateRecords target
          JOIN inserted insertedRow ON target.RecordId = insertedRow.RecordId
          LEFT JOIN deleted deletedRow ON target.RecordId = deletedRow.RecordId;
    END;');

    INSERT portal.ToolCategories(CategoryName, SortOrder) VALUES (N'[SAMPLE] 見積試算', 900);
    DECLARE @CategoryId int = SCOPE_IDENTITY();
    INSERT portal.Tools(ToolId, CategoryId, ToolName, ToolSummary, Remarks, OwnerUserId, ToolType, WebAppUrl, Status, SortOrder)
    VALUES ('SAMPLE-ESTIMATE', @CategoryId, N'[SAMPLE] 見積試算・案件記録', N'計算と本人の案件保存・検索・更新を学ぶ開発専用ツールです。',
            N'開発専用。実業務データを入力しないでください。', @OwnerUserId, 'WEB', N'/tools/SAMPLE-ESTIMATE/app', 'PRIVATE', 900);
    INSERT portal.FileUploadPolicies(ScopeType, ToolId, PurposeCode, MaxFileSizeBytes)
    VALUES ('TOOL', 'SAMPLE-ESTIMATE', 'TOOL_INPUT', 1048576);
    DECLARE @PolicyId int = SCOPE_IDENTITY();
    INSERT portal.FileUploadPolicyExtensions(PolicyId, Extension) VALUES (@PolicyId, '.tsv');

    INSERT dbo.SampleEstimateRecords(RecordId, SubmissionId, OwnerUserId, ProjectName, AppliedOn, CategoryCode, Quantity, UnitPrice,
        Subtotal, DiscountAmount, Total, DetailRowCount)
    VALUES
        ('00000000-0000-0000-0000-00000000A101', '00000000-0000-0000-0000-00000000B101', @OwnerUserId,
         N'[SAMPLE] 標準案件', '2026-09-23', 'STANDARD', 2, 50000, 100000, 0, 100000, NULL),
        ('00000000-0000-0000-0000-00000000A102', '00000000-0000-0000-0000-00000000B102', @OwnerUserId,
         N'[SAMPLE] キャンペーン案件', '2026-09-24', 'CAMPAIGN', 3, 10000, 30000, 3000, 27000, 4);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
