-- 空の専用検証DBに001、002を適用した後で実行する。既存業務DBには実行しない。
SET NOCOUNT ON;
SET XACT_ABORT OFF;
BEGIN TRANSACTION;
BEGIN TRY
    UPDATE portal.SystemSettings SET SettingValue = NULL WHERE SettingCategory = 'SITE' AND SettingKey = 'PUBLICATION_STATUS';
    THROW 51000, 'NULL publication accepted', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
BEGIN TRY
    UPDATE portal.SystemSettings SET SettingValue = N'2026-9-1' WHERE SettingCategory = 'TEST' AND SettingKey = 'BUSINESS_DATE';
    THROW 51001, 'Noncanonical date accepted', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
BEGIN TRY
    INSERT portal.FileUploadPolicyExtensions (PolicyId, Extension)
    SELECT TOP (1) PolicyId, '.PDF' FROM portal.FileUploadPolicies;
    THROW 51002, 'Uppercase extension accepted', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
-- LENは末尾空白を数えないため、本文の上限はDATALENGTHで確認する。
INSERT portal.Notices (NoticeType, Title, Content, IsPublished)
VALUES ('SYSTEM', N'Boundary notice', REPLICATE(CAST(N'あ' AS nvarchar(max)), 10000), 0);
BEGIN TRY
    INSERT portal.Notices (NoticeType, Title, Content, IsPublished)
    VALUES ('SYSTEM', N'Overflow notice', REPLICATE(CAST(N'あ' AS nvarchar(max)), 10000) + N' ', 0);
    THROW 51007, 'Notice trailing space accepted', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
INSERT portal.FaqCategories (CategoryName, SortOrder) VALUES (N'Boundary FAQ', 0);
DECLARE @FaqCategoryId int = SCOPE_IDENTITY();
INSERT portal.FaqItems (CategoryId, Question, Answer, SortOrder, IsPublished)
VALUES (@FaqCategoryId, N'Boundary answer?', REPLICATE(CAST(N'あ' AS nvarchar(max)), 10000), 0, 0);
BEGIN TRY
    INSERT portal.FaqItems (CategoryId, Question, Answer, SortOrder, IsPublished)
    VALUES (@FaqCategoryId, N'Overflow answer?', REPLICATE(CAST(N'あ' AS nvarchar(max)), 10000) + N' ', 1, 0);
    THROW 51008, 'FAQ trailing space accepted', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547 THROW;
END CATCH;
IF (SELECT COUNT(*) FROM portal.CodeMaster WHERE CodeType = 'TOOL_STATUS' AND
    (CodeValue = 'PUBLIC' AND ColorCode = '#39B54A' OR
     CodeValue = 'PRIVATE' AND ColorCode = '#EADFFF' OR
     CodeValue = 'HIDDEN' AND ColorCode = '#E1E3E6')) <> 3
    THROW 51009, 'Tool status colors invalid', 1;
INSERT portal.ToolCategories (CategoryName) VALUES (N'Audit test'), (N'Audit test');
IF EXISTS (SELECT 1 FROM portal.ToolCategories WHERE CategoryName = N'Audit test' AND
    (UpdateCount <> 0 OR CreatedAt <> UpdatedAt OR CreatedBy <> USER_NAME() OR UpdatedBy <> USER_NAME()))
    THROW 51003, 'Insert audit invalid', 1;
DECLARE @Created datetime2(3) = (SELECT MIN(CreatedAt) FROM portal.ToolCategories WHERE CategoryName = N'Audit test');
UPDATE portal.ToolCategories SET CreatedAt = '20000101', CreatedBy = N'forged', UpdateCount = 100, CategoryName = N'Audit test' WHERE CategoryName = N'Audit test';
IF EXISTS (SELECT 1 FROM portal.ToolCategories WHERE CategoryName = N'Audit test' AND
    (UpdateCount <> 1 OR CreatedAt <> @Created OR CreatedBy <> USER_NAME()))
    THROW 51004, 'Update audit invalid', 1;
EXEC(N'CREATE TRIGGER portal.TR_TestNested ON portal.ToolCategories AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 UPDATE portal.SystemSettings SET SettingName = SettingName WHERE SettingCategory = ''SITE'' AND SettingKey = ''PRIVATE_MESSAGE'';
END');
DECLARE @Before int = (SELECT UpdateCount FROM portal.SystemSettings WHERE SettingCategory = 'SITE' AND SettingKey = 'PRIVATE_MESSAGE');
UPDATE portal.ToolCategories SET CategoryName = N'Audit test' WHERE CategoryName = N'Audit test';
IF (SELECT UpdateCount FROM portal.SystemSettings WHERE SettingCategory = 'SITE' AND SettingKey = 'PRIVATE_MESSAGE') <= @Before
    THROW 51005, 'Nested audit skipped', 1;
IF (SELECT COUNT(*) FROM portal.CodeMaster) <> 18 THROW 51006, 'Master count invalid', 1;
IF (SELECT COUNT(*) FROM portal.Roles WHERE RoleCode IN ('A', 'B', 'C', 'D', 'ADMIN')) <> 5
    THROW 51010, 'Initial roles invalid', 1;
IF COL_LENGTH(N'portal.ToolCategories', N'SortOrder') IS NOT NULL OR OBJECT_ID(N'portal.ToolRoles', N'U') IS NULL
    THROW 51011, 'Role/tool schema invalid', 1;
ROLLBACK TRANSACTION;
PRINT 'PASS: constraints, body length, tool colors, audit, nested trigger, duplicate category, master count';

IF COL_LENGTH(N'portal.AspNetUsers', N'LoginId') IS NULL OR COL_LENGTH(N'portal.AspNetUsers', N'NormalizedLoginId') IS NULL
    THROW 51020, 'LoginId columns missing', 1;
IF COL_LENGTH(N'portal.Roles', N'NoticeMailEnabled') IS NULL
    THROW 51021, 'Role notification column missing', 1;
IF EXISTS (SELECT 1 FROM portal.Roles WHERE RoleCode IN ('ADMIN','A','B','C') AND NoticeMailEnabled <> 1)
 OR EXISTS (SELECT 1 FROM portal.Roles WHERE RoleCode='D' AND NoticeMailEnabled <> 0)
    THROW 51022, 'Initial role notification flags incorrect', 1;
IF (SELECT COUNT(*) FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'portal.UserPreferences')
 AND name IN ('DF_UserPreferences_SystemNotice','DF_UserPreferences_FavoriteNotice')) <> 2
 OR EXISTS (SELECT 1 FROM sys.default_constraints
 WHERE parent_object_id=OBJECT_ID(N'portal.UserPreferences') AND name IN ('DF_UserPreferences_SystemNotice','DF_UserPreferences_FavoriteNotice')
 AND REPLACE(REPLACE(definition,'(',''),')','') <> '0')
    THROW 51023, 'Preference defaults must be OFF', 1;
PRINT 'PASS: LoginId and notification defaults (initial seed only)';
