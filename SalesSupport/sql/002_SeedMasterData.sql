SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

IF OBJECT_ID(N'portal.CodeMaster', N'U') IS NULL
    THROW 50100, N'先に001_CreateTables.sqlを実行してください。', 1;

BEGIN TRANSACTION;

DECLARE @Codes TABLE
(
    CodeType varchar(50) NOT NULL,
    CodeValue varchar(20) NOT NULL,
    CodeName nvarchar(100) NOT NULL,
    SortOrder int NOT NULL,
    ColorCode varchar(7) NULL,
    PRIMARY KEY (CodeType, CodeValue)
);

INSERT @Codes (CodeType, CodeValue, CodeName, SortOrder, ColorCode) VALUES
('TOOL_STATUS',      'PUBLIC',          N'一般公開',         10, '#39B54A'),
('TOOL_STATUS',      'PRIVATE',         N'限定公開',         20, '#EADFFF'),
('TOOL_STATUS',      'HIDDEN',          N'非公開',           30, '#E1E3E6'),
('SITE_STATUS',      'PUBLIC',          N'Public公開',       10, '#2E7D32'),
('SITE_STATUS',      'PRIVATE',         N'Private公開',      20, '#6A1B9A'),
('TOOL_TYPE',        'DESKTOP',         N'デスクトップ',     10, NULL),
('TOOL_TYPE',        'WEB',             N'Web',              20, NULL),
('TOOL_TYPE',        'DOCUMENT',        N'資料',             30, NULL),
('INQUIRY_CATEGORY', 'QUESTION',        N'質問',             10, NULL),
('INQUIRY_CATEGORY', 'REQUEST',         N'要望',             20, NULL),
('INQUIRY_CATEGORY', 'OPINION',         N'意見',             30, NULL),
('INQUIRY_CATEGORY', 'PROBLEM',         N'不具合報告',       40, NULL),
('INQUIRY_CATEGORY', 'OTHER',           N'その他',           50, NULL),
('INQUIRY_STATUS',   'ACTION_REQUIRED', N'要対応',           10, '#C62828'),
('INQUIRY_STATUS',   'IN_PROGRESS',     N'対応中',           20, '#1565C0'),
('INQUIRY_STATUS',   'UNDER_REVIEW',    N'検討中',           30, '#B45309'),
('INQUIRY_STATUS',   'COMPLETED',       N'完了',             40, '#616161'),
('INQUIRY_STATUS',   'NO_ACTION',       N'対応不要',         50, '#455A64'),
('USER_ROLE',        'USER',            N'一般ユーザー',     10, NULL),
('USER_ROLE',        'ADMIN',           N'システム管理者',   20, NULL);

UPDATE target
   SET CodeName = source.CodeName,
       SortOrder = source.SortOrder,
       ColorCode = source.ColorCode
  FROM portal.CodeMaster target
  JOIN @Codes source ON source.CodeType = target.CodeType AND source.CodeValue = target.CodeValue
 WHERE target.CodeName <> source.CodeName
    OR target.SortOrder <> source.SortOrder
    OR ISNULL(target.ColorCode, '') <> ISNULL(source.ColorCode, '');

INSERT portal.CodeMaster (CodeType, CodeValue, CodeName, SortOrder, ColorCode)
SELECT source.CodeType, source.CodeValue, source.CodeName, source.SortOrder, source.ColorCode
  FROM @Codes source
 WHERE NOT EXISTS
       (SELECT 1 FROM portal.CodeMaster target WHERE target.CodeType = source.CodeType AND target.CodeValue = source.CodeValue);

DECLARE @Settings TABLE
(
    SettingCategory varchar(50) NOT NULL,
    SettingKey varchar(50) NOT NULL,
    SettingName nvarchar(100) NOT NULL,
    InitialValue nvarchar(2000) NULL,
    Description nvarchar(1000) NULL,
    PRIMARY KEY (SettingCategory, SettingKey)
);

INSERT @Settings VALUES
('SITE', 'PUBLICATION_STATUS', N'サイト公開状態', N'PRIVATE', N'PUBLICまたはPRIVATE。通常運用では所定SQLで変更する。'),
('SITE', 'PRIVATE_MESSAGE', N'入場制限時の案内文', N'メンテナンス中です。', N'一般ユーザーへ表示するプレーンテキスト。'),
('TEST', 'BUSINESS_DATE', N'テスト用業務日付', NULL, N'開発環境専用。NULLまたはYYYY-MM-DD。');

UPDATE target
   SET SettingName = source.SettingName,
       Description = source.Description
  FROM portal.SystemSettings target
  JOIN @Settings source ON source.SettingCategory = target.SettingCategory AND source.SettingKey = target.SettingKey
 WHERE target.SettingName <> source.SettingName
    OR ISNULL(target.Description, N'') <> ISNULL(source.Description, N'');

INSERT portal.SystemSettings (SettingCategory, SettingKey, SettingName, SettingValue, Description)
SELECT SettingCategory, SettingKey, SettingName, InitialValue, Description
  FROM @Settings source
 WHERE NOT EXISTS
       (SELECT 1 FROM portal.SystemSettings target WHERE target.SettingCategory = source.SettingCategory AND target.SettingKey = source.SettingKey);

DECLARE @Policies TABLE
(
    ScopeType varchar(20) NOT NULL,
    PurposeCode varchar(30) NOT NULL,
    MaxFileSizeBytes bigint NOT NULL,
    PRIMARY KEY (ScopeType, PurposeCode)
);
INSERT @Policies VALUES
('SITE', 'INQUIRY_ATTACHMENT', 10000000),
('SITE', 'USER_IMPORT', 1000000),
('TOOL_COMMON', 'REFERENCE', 100000000),
('TOOL_COMMON', 'APP', 500000000);

UPDATE target
   SET MaxFileSizeBytes = source.MaxFileSizeBytes
  FROM portal.FileUploadPolicies target
  JOIN @Policies source ON source.ScopeType = target.ScopeType AND source.PurposeCode = target.PurposeCode
 WHERE target.ToolId IS NULL AND target.MaxFileSizeBytes <> source.MaxFileSizeBytes;

INSERT portal.FileUploadPolicies (ScopeType, ToolId, PurposeCode, MaxFileSizeBytes)
SELECT ScopeType, NULL, PurposeCode, MaxFileSizeBytes
  FROM @Policies source
 WHERE NOT EXISTS
       (SELECT 1 FROM portal.FileUploadPolicies target
         WHERE target.ScopeType = source.ScopeType AND target.PurposeCode = source.PurposeCode AND target.ToolId IS NULL);

DECLARE @Extensions TABLE
(
    ScopeType varchar(20) NOT NULL,
    PurposeCode varchar(30) NOT NULL,
    Extension varchar(20) NOT NULL,
    PRIMARY KEY (ScopeType, PurposeCode, Extension)
);
INSERT @Extensions VALUES
('SITE', 'USER_IMPORT', '.tsv'),
('SITE', 'INQUIRY_ATTACHMENT', '.pdf'),
('SITE', 'INQUIRY_ATTACHMENT', '.xlsx'),
('SITE', 'INQUIRY_ATTACHMENT', '.xlsm'),
('SITE', 'INQUIRY_ATTACHMENT', '.docx'),
('SITE', 'INQUIRY_ATTACHMENT', '.csv'),
('SITE', 'INQUIRY_ATTACHMENT', '.txt'),
('SITE', 'INQUIRY_ATTACHMENT', '.png'),
('SITE', 'INQUIRY_ATTACHMENT', '.jpg'),
('SITE', 'INQUIRY_ATTACHMENT', '.jpeg'),
('TOOL_COMMON', 'REFERENCE', '.pdf'),
('TOOL_COMMON', 'REFERENCE', '.xlsx'),
('TOOL_COMMON', 'REFERENCE', '.xlsm'),
('TOOL_COMMON', 'REFERENCE', '.docx'),
('TOOL_COMMON', 'REFERENCE', '.pptx'),
('TOOL_COMMON', 'REFERENCE', '.csv'),
('TOOL_COMMON', 'REFERENCE', '.tsv'),
('TOOL_COMMON', 'REFERENCE', '.txt'),
('TOOL_COMMON', 'REFERENCE', '.png'),
('TOOL_COMMON', 'REFERENCE', '.jpg'),
('TOOL_COMMON', 'REFERENCE', '.jpeg'),
('TOOL_COMMON', 'REFERENCE', '.zip'),
('TOOL_COMMON', 'APP', '.zip'),
('TOOL_COMMON', 'APP', '.msi'),
('TOOL_COMMON', 'APP', '.exe');

INSERT portal.FileUploadPolicyExtensions (PolicyId, Extension)
SELECT policy.PolicyId, source.Extension
  FROM @Extensions source
  JOIN portal.FileUploadPolicies policy
    ON policy.ScopeType = source.ScopeType
   AND policy.PurposeCode = source.PurposeCode
   AND policy.ToolId IS NULL
 WHERE NOT EXISTS
       (SELECT 1 FROM portal.FileUploadPolicyExtensions target
         WHERE target.PolicyId = policy.PolicyId AND target.Extension = source.Extension);

DECLARE @Errors TABLE
(
    ToolId varchar(20) NOT NULL,
    ErrorNo int NOT NULL,
    ErrorCode varchar(100) NOT NULL,
    DisplayMessage nvarchar(2000) NOT NULL,
    ErrorLevel varchar(20) NOT NULL,
    PRIMARY KEY (ToolId, ErrorNo)
);
INSERT @Errors VALUES
('SYSTEM', 1, 'SYS_UNEXPECTED_ERROR', N'予期しないエラーが発生しました。時間をおいて再度お試しください。', 'ERROR'),
('SYSTEM', 2, 'SYS_ACCESS_DENIED', N'この操作を実行する権限がありません。', 'ERROR'),
('SYSTEM', 3, 'SYS_CONFIGURATION_ERROR', N'システム設定を確認できないため処理を継続できません。', 'CRITICAL'),
('SYSTEM', 4, 'SYS_FILE_VALIDATION_ERROR', N'ファイルの形式または容量が許可条件を満たしていません。', 'ERROR'),
('SYSTEM', 5, 'SYS_MAIL_SEND_ERROR', N'メールを送信できませんでした。時間をおいて新しく操作してください。', 'ERROR');

UPDATE target
   SET ErrorCode = source.ErrorCode,
       DisplayMessage = source.DisplayMessage,
       ErrorLevel = source.ErrorLevel
  FROM portal.ErrorCodes target
  JOIN @Errors source ON source.ToolId = target.ToolId AND source.ErrorNo = target.ErrorNo
 WHERE target.ErrorCode <> source.ErrorCode
    OR target.DisplayMessage <> source.DisplayMessage
    OR target.ErrorLevel <> source.ErrorLevel;

INSERT portal.ErrorCodes (ToolId, ErrorNo, ErrorCode, DisplayMessage, ErrorLevel)
SELECT ToolId, ErrorNo, ErrorCode, DisplayMessage, ErrorLevel
  FROM @Errors source
 WHERE NOT EXISTS
       (SELECT 1 FROM portal.ErrorCodes target WHERE target.ToolId = source.ToolId AND target.ErrorNo = source.ErrorNo);

COMMIT TRANSACTION;
