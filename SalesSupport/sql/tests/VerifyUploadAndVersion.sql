-- 001・002適用済みの使い捨てDB専用。検証変更はロールバックする。
SET NOCOUNT ON;
SET XACT_ABORT OFF;
BEGIN TRANSACTION;
DECLARE @Definition nvarchar(max) = (SELECT definition FROM sys.check_constraints WHERE name = N'CK_ToolVersionHistories_Version');
EXEC(N'CREATE TABLE dbo.VersionConstraintProbe (Version nvarchar(50) NOT NULL CHECK ' + @Definition + N');');
INSERT dbo.VersionConstraintProbe VALUES (N'0.0.0'), (N'1.2.3'), (N'99.99.99'), (N'10.0.9');
DECLARE @Bad TABLE (Version nvarchar(50));
INSERT @Bad VALUES (N'01.2.3'),(N'1.02.3'),(N'1.2.03'),(N'100.0.0'),(N'0.100.0'),(N'0.0.100'),(N'1..2'),(N'.1.2'),(N'1.2.'),(N'1.2.3 '),(N'１.2.3'),(N'-1.2.3'),(N'1.2.3.4'),(N'00.0.0');
DECLARE @Version nvarchar(50);
DECLARE versions CURSOR LOCAL FAST_FORWARD FOR SELECT Version FROM @Bad;
OPEN versions;
FETCH NEXT FROM versions INTO @Version;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        INSERT dbo.VersionConstraintProbe VALUES (@Version);
        THROW 51100, 'Invalid version accepted', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
    END CATCH;
    FETCH NEXT FROM versions INTO @Version;
END;
CLOSE versions;
DEALLOCATE versions;
IF NOT EXISTS (SELECT 1 FROM portal.FileUploadPolicies p JOIN portal.FileUploadPolicyExtensions e ON p.PolicyId=e.PolicyId
 WHERE p.ScopeType='SITE' AND p.PurposeCode='USER_IMPORT' AND p.ToolId IS NULL AND p.MaxFileSizeBytes=1000000 AND e.Extension='.tsv')
 THROW 51101, 'Import policy missing', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('portal.ToolFiles') AND name IN ('UploadedByUserId','UploadedAt') AND is_nullable=0) <> 2
 THROW 51102, 'Upload columns missing', 1;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ToolFiles_Uploader' AND referenced_object_id=OBJECT_ID('portal.AspNetUsers'))
 THROW 51103, 'Uploader foreign key missing', 1;
ROLLBACK TRANSACTION;
PRINT 'PASS: version boundaries, import policy, upload columns and foreign key';
