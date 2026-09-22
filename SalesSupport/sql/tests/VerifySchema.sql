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
INSERT portal.ToolCategories (CategoryName, SortOrder) VALUES (N'Audit test', 0), (N'Audit test', 0);
IF EXISTS (SELECT 1 FROM portal.ToolCategories WHERE CategoryName = N'Audit test' AND
    (UpdateCount <> 0 OR CreatedAt <> UpdatedAt OR CreatedBy <> USER_NAME() OR UpdatedBy <> USER_NAME()))
    THROW 51003, 'Insert audit invalid', 1;
DECLARE @Created datetime2(3) = (SELECT MIN(CreatedAt) FROM portal.ToolCategories WHERE CategoryName = N'Audit test');
UPDATE portal.ToolCategories SET CreatedAt = '20000101', CreatedBy = N'forged', UpdateCount = 100, SortOrder = 1 WHERE CategoryName = N'Audit test';
IF EXISTS (SELECT 1 FROM portal.ToolCategories WHERE CategoryName = N'Audit test' AND
    (UpdateCount <> 1 OR CreatedAt <> @Created OR CreatedBy <> USER_NAME()))
    THROW 51004, 'Update audit invalid', 1;
EXEC(N'CREATE TRIGGER portal.TR_TestNested ON portal.ToolCategories AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 UPDATE portal.SystemSettings SET SettingName = SettingName WHERE SettingCategory = ''SITE'' AND SettingKey = ''PRIVATE_MESSAGE'';
END');
DECLARE @Before int = (SELECT UpdateCount FROM portal.SystemSettings WHERE SettingCategory = 'SITE' AND SettingKey = 'PRIVATE_MESSAGE');
UPDATE portal.ToolCategories SET SortOrder = 2 WHERE CategoryName = N'Audit test';
IF (SELECT UpdateCount FROM portal.SystemSettings WHERE SettingCategory = 'SITE' AND SettingKey = 'PRIVATE_MESSAGE') <= @Before
    THROW 51005, 'Nested audit skipped', 1;
IF (SELECT COUNT(*) FROM portal.CodeMaster) <> 20 THROW 51006, 'Master count invalid', 1;
ROLLBACK TRANSACTION;
PRINT 'PASS: constraints, body length, tool colors, audit, nested trigger, duplicate category, master count';
