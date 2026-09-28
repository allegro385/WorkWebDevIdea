-- 既存DB専用。接続先とバックアップを運用手順で確認してから一度だけ実行する。
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'portal.AspNetUsers', N'U') IS NULL OR OBJECT_ID(N'portal.Tools', N'U') IS NULL
    THROW 50300, N'既存のPortalテーブルがありません。新規DBには001と002を使用してください。', 1;
IF OBJECT_ID(N'portal.Roles', N'U') IS NOT NULL OR OBJECT_ID(N'portal.ToolRoles', N'U') IS NOT NULL
    THROW 50301, N'ロール移行は適用済み、または途中です。状態を確認してください。', 1;
IF COL_LENGTH(N'portal.ToolCategories', N'SortOrder') IS NULL
    THROW 50302, N'旧カテゴリ並び順列がありません。移行元の状態を確認してください。', 1;

BEGIN TRANSACTION;

CREATE TABLE portal.Roles
(
    RoleCode varchar(20) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    RoleName nvarchar(100) NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_Roles_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Roles_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_Roles_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Roles_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT CK_Roles_Code CHECK (LEN(LTRIM(RTRIM(RoleCode))) > 0),
    CONSTRAINT CK_Roles_Name CHECK (LEN(LTRIM(RTRIM(RoleName))) > 0)
);

-- USERはIdentity APIによる移行完了までの一時的な参照先。
INSERT portal.Roles (RoleCode, RoleName) VALUES
('A', N'ロールA'), ('B', N'ロールB'), ('C', N'ロールC'), ('D', N'ロールD'),
('ADMIN', N'システム管理者'), ('USER', N'移行待ち');

ALTER TABLE portal.AspNetUsers DROP CONSTRAINT CK_AspNetUsers_RoleCode;
ALTER TABLE portal.AspNetUsers WITH CHECK ADD CONSTRAINT FK_AspNetUsers_Roles
    FOREIGN KEY (RoleCode) REFERENCES portal.Roles (RoleCode) ON DELETE NO ACTION ON UPDATE NO ACTION;

ALTER TABLE portal.ToolCategories DROP CONSTRAINT CK_ToolCategories_SortOrder;
ALTER TABLE portal.ToolCategories DROP COLUMN SortOrder;

CREATE TABLE portal.ToolRoles
(
    ToolId varchar(20) NOT NULL,
    RoleCode varchar(20) NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_ToolRoles_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolRoles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolRoles_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolRoles_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolRoles_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_ToolRoles PRIMARY KEY (ToolId, RoleCode),
    CONSTRAINT FK_ToolRoles_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_ToolRoles_Roles FOREIGN KEY (RoleCode) REFERENCES portal.Roles (RoleCode) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_ToolRoles_NoAdmin CHECK (RoleCode <> 'ADMIN')
);

INSERT portal.ToolRoles (ToolId, RoleCode) SELECT ToolId, 'A' FROM portal.Tools;

-- 既存の監査トリガーと同じ規則を、新しい2表にも適用する。
EXEC(N'CREATE TRIGGER portal.TR_Roles_AuditUpdate ON portal.Roles AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL(@@PROCID, ''AFTER'', ''DML'') > 1 RETURN;
    DECLARE @AuditNow datetime2(3) = SYSUTCDATETIME();
    UPDATE t SET UpdateCount = CASE WHEN d.CreatedAt IS NULL THEN 0 ELSE d.UpdateCount + 1 END,
        CreatedAt = COALESCE(d.CreatedAt, @AuditNow), CreatedBy = COALESCE(d.CreatedBy, USER_NAME()),
        UpdatedAt = @AuditNow, UpdatedBy = USER_NAME()
    FROM portal.Roles t JOIN inserted i ON t.RoleCode = i.RoleCode
    LEFT JOIN deleted d ON t.RoleCode = d.RoleCode;
END');
EXEC(N'CREATE TRIGGER portal.TR_ToolRoles_AuditUpdate ON portal.ToolRoles AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL(@@PROCID, ''AFTER'', ''DML'') > 1 RETURN;
    DECLARE @AuditNow datetime2(3) = SYSUTCDATETIME();
    UPDATE t SET UpdateCount = CASE WHEN d.CreatedAt IS NULL THEN 0 ELSE d.UpdateCount + 1 END,
        CreatedAt = COALESCE(d.CreatedAt, @AuditNow), CreatedBy = COALESCE(d.CreatedBy, USER_NAME()),
        UpdatedAt = @AuditNow, UpdatedBy = USER_NAME()
    FROM portal.ToolRoles t JOIN inserted i ON t.ToolId = i.ToolId AND t.RoleCode = i.RoleCode
    LEFT JOIN deleted d ON t.ToolId = d.ToolId AND t.RoleCode = d.RoleCode;
END');

DELETE FROM portal.CodeMaster WHERE CodeType = 'USER_ROLE';
COMMIT TRANSACTION;
