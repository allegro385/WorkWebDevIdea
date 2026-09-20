SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0)
    THROW 50001, N'SalesSupportのテーブルが既に存在します。001_CreateTables.sqlは空のデータベースで実行してください。', 1;

BEGIN TRANSACTION;

IF SCHEMA_ID(N'portal') IS NULL EXEC(N'CREATE SCHEMA [portal] AUTHORIZATION [dbo]');
IF SCHEMA_ID(N'log') IS NULL EXEC(N'CREATE SCHEMA [log] AUTHORIZATION [dbo]');

CREATE TABLE portal.AspNetUsers
(
    UserId uniqueidentifier NOT NULL CONSTRAINT DF_AspNetUsers_UserId DEFAULT NEWSEQUENTIALID(),
    UserName nvarchar(256) NOT NULL,
    NormalizedUserName nvarchar(256) NOT NULL,
    Email nvarchar(256) NOT NULL,
    NormalizedEmail nvarchar(256) NOT NULL,
    EmailConfirmed bit NOT NULL CONSTRAINT DF_AspNetUsers_EmailConfirmed DEFAULT (0),
    PasswordHash nvarchar(max) NULL,
    SecurityStamp nvarchar(max) NOT NULL,
    ConcurrencyStamp nvarchar(max) NOT NULL,
    PhoneNumber nvarchar(max) NULL,
    PhoneNumberConfirmed bit NOT NULL CONSTRAINT DF_AspNetUsers_PhoneNumberConfirmed DEFAULT (0),
    TwoFactorEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_TwoFactorEnabled DEFAULT (0),
    LockoutEnd datetimeoffset(7) NULL,
    LockoutEnabled bit NOT NULL CONSTRAINT DF_AspNetUsers_LockoutEnabled DEFAULT (1),
    AccessFailedCount int NOT NULL CONSTRAINT DF_AspNetUsers_AccessFailedCount DEFAULT (0),
    DisplayName nvarchar(100) NOT NULL,
    RoleCode varchar(20) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT (1),
    LastAccessAt datetime2(3) NULL,
    CONSTRAINT PK_AspNetUsers PRIMARY KEY (UserId),
    CONSTRAINT CK_AspNetUsers_UserName_NotBlank CHECK (LEN(LTRIM(RTRIM(UserName))) > 0),
    CONSTRAINT CK_AspNetUsers_Email_NotBlank CHECK (LEN(LTRIM(RTRIM(Email))) > 0),
    CONSTRAINT CK_AspNetUsers_DisplayName_NotBlank CHECK (LEN(LTRIM(RTRIM(DisplayName))) > 0),
    CONSTRAINT CK_AspNetUsers_RoleCode CHECK (RoleCode IN ('USER', 'ADMIN')),
    CONSTRAINT CK_AspNetUsers_AccessFailedCount CHECK (AccessFailedCount >= 0)
);
CREATE UNIQUE INDEX UX_AspNetUsers_NormalizedUserName ON portal.AspNetUsers (NormalizedUserName);
CREATE UNIQUE INDEX UX_AspNetUsers_NormalizedEmail ON portal.AspNetUsers (NormalizedEmail);

CREATE TABLE portal.AspNetUserClaims
(
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUserClaims PRIMARY KEY,
    UserId uniqueidentifier NOT NULL,
    ClaimType nvarchar(max) NULL,
    ClaimValue nvarchar(max) NULL,
    CONSTRAINT FK_AspNetUserClaims_AspNetUsers FOREIGN KEY (UserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION
);

CREATE TABLE portal.AspNetUserLogins
(
    LoginProvider nvarchar(128) NOT NULL,
    ProviderKey nvarchar(128) NOT NULL,
    ProviderDisplayName nvarchar(max) NULL,
    UserId uniqueidentifier NOT NULL,
    CONSTRAINT PK_AspNetUserLogins PRIMARY KEY (LoginProvider, ProviderKey),
    CONSTRAINT FK_AspNetUserLogins_AspNetUsers FOREIGN KEY (UserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION
);

CREATE TABLE portal.AspNetUserTokens
(
    UserId uniqueidentifier NOT NULL,
    LoginProvider nvarchar(128) NOT NULL,
    Name nvarchar(128) NOT NULL,
    Value nvarchar(max) NULL,
    CONSTRAINT PK_AspNetUserTokens PRIMARY KEY (UserId, LoginProvider, Name),
    CONSTRAINT FK_AspNetUserTokens_AspNetUsers FOREIGN KEY (UserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION
);

CREATE TABLE portal.ToolCategories
(
    CategoryId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ToolCategories PRIMARY KEY,
    CategoryName nvarchar(100) NOT NULL,
    SortOrder int NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_ToolCategories_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolCategories_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolCategories_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolCategories_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolCategories_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT CK_ToolCategories_CategoryName_NotBlank CHECK (LEN(LTRIM(RTRIM(CategoryName))) > 0),
    CONSTRAINT CK_ToolCategories_SortOrder CHECK (SortOrder >= 0)
);

CREATE TABLE portal.Tools
(
    ToolId varchar(20) NOT NULL,
    CategoryId int NOT NULL,
    ToolName nvarchar(200) NOT NULL,
    ToolSummary nvarchar(1000) NULL,
    Remarks nvarchar(2000) NULL,
    OwnerUserId uniqueidentifier NOT NULL,
    ToolType varchar(20) NOT NULL,
    WebAppUrl nvarchar(2048) NULL,
    Status varchar(20) NOT NULL,
    SortOrder int NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_Tools_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Tools_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Tools_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_Tools_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Tools_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_Tools PRIMARY KEY (ToolId),
    CONSTRAINT FK_Tools_ToolCategories FOREIGN KEY (CategoryId) REFERENCES portal.ToolCategories (CategoryId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_Tools_AspNetUsers_Owner FOREIGN KEY (OwnerUserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_Tools_ToolId CHECK (LEN(LTRIM(RTRIM(ToolId))) > 0 AND ToolId <> 'SYSTEM'),
    CONSTRAINT CK_Tools_ToolName_NotBlank CHECK (LEN(LTRIM(RTRIM(ToolName))) > 0),
    CONSTRAINT CK_Tools_ToolType CHECK (ToolType IN ('DESKTOP', 'WEB', 'DOCUMENT')),
    CONSTRAINT CK_Tools_Status CHECK (Status IN ('PUBLIC', 'PRIVATE', 'HIDDEN')),
    CONSTRAINT CK_Tools_WebAppUrl CHECK ((ToolType = 'WEB') OR WebAppUrl IS NULL),
    CONSTRAINT CK_Tools_SortOrder CHECK (SortOrder >= 0)
);

CREATE TABLE portal.UserPreferences
(
    UserId uniqueidentifier NOT NULL,
    SystemNoticeMailEnabled bit NOT NULL CONSTRAINT DF_UserPreferences_SystemNotice DEFAULT (1),
    FavoriteToolNoticeMailEnabled bit NOT NULL CONSTRAINT DF_UserPreferences_FavoriteNotice DEFAULT (1),
    UpdateCount int NOT NULL CONSTRAINT DF_UserPreferences_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserPreferences_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_UserPreferences_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserPreferences_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_UserPreferences_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_UserPreferences PRIMARY KEY (UserId),
    CONSTRAINT FK_UserPreferences_AspNetUsers FOREIGN KEY (UserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION
);

CREATE TABLE portal.UserToolFavorites
(
    UserId uniqueidentifier NOT NULL,
    ToolId varchar(20) NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_UserToolFavorites_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserToolFavorites_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_UserToolFavorites_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_UserToolFavorites_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_UserToolFavorites_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_UserToolFavorites PRIMARY KEY (UserId, ToolId),
    CONSTRAINT FK_UserToolFavorites_AspNetUsers FOREIGN KEY (UserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_UserToolFavorites_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION
);

CREATE TABLE portal.ToolVersionHistories
(
    ToolHistoryId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ToolVersionHistories PRIMARY KEY,
    ToolId varchar(20) NOT NULL,
    Version nvarchar(50) NOT NULL,
    ModifiedByUserId uniqueidentifier NOT NULL,
    ChangeDescription nvarchar(2000) NOT NULL,
    ReleasedAt date NOT NULL,
    IsCurrent bit NOT NULL CONSTRAINT DF_ToolVersionHistories_IsCurrent DEFAULT (0),
    UpdateCount int NOT NULL CONSTRAINT DF_ToolVersionHistories_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolVersionHistories_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolVersionHistories_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolVersionHistories_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolVersionHistories_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT FK_ToolVersionHistories_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_ToolVersionHistories_AspNetUsers FOREIGN KEY (ModifiedByUserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT UQ_ToolVersionHistories_Tool_Version UNIQUE (ToolId, Version),
    CONSTRAINT CK_ToolVersionHistories_Version CHECK (
        Version COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9.]%'
        AND DATALENGTH(Version) BETWEEN 10 AND 16
        AND LEN(Version) - LEN(REPLACE(Version, N'.', N'')) = 2
        AND PARSENAME(Version, 3) IS NOT NULL AND PARSENAME(Version, 2) IS NOT NULL AND PARSENAME(Version, 1) IS NOT NULL
        AND TRY_CONVERT(int, PARSENAME(Version, 3)) BETWEEN 0 AND 99
        AND TRY_CONVERT(int, PARSENAME(Version, 2)) BETWEEN 0 AND 99
        AND TRY_CONVERT(int, PARSENAME(Version, 1)) BETWEEN 0 AND 99
        AND (LEN(PARSENAME(Version, 3)) = 1 OR LEFT(PARSENAME(Version, 3), 1) <> N'0')
        AND (LEN(PARSENAME(Version, 2)) = 1 OR LEFT(PARSENAME(Version, 2), 1) <> N'0')
        AND (LEN(PARSENAME(Version, 1)) = 1 OR LEFT(PARSENAME(Version, 1), 1) <> N'0')),
    CONSTRAINT CK_ToolVersionHistories_Description CHECK (LEN(LTRIM(RTRIM(ChangeDescription))) > 0)
);
CREATE UNIQUE INDEX UX_ToolVersionHistories_Current ON portal.ToolVersionHistories (ToolId) WHERE IsCurrent = 1;

CREATE TABLE portal.ToolFiles
(
    FileId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ToolFiles PRIMARY KEY,
    ToolId varchar(20) NOT NULL,
    FileCategory varchar(20) NOT NULL,
    DisplayName nvarchar(200) NOT NULL,
    OriginalFileName nvarchar(255) NOT NULL,
    RelativePath nvarchar(500) NOT NULL,
    Extension varchar(20) NOT NULL,
    FileSizeBytes bigint NOT NULL,
    UploadedByUserId uniqueidentifier NOT NULL,
    UploadedAt datetime2(3) NOT NULL,
    SortOrder int NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_ToolFiles_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolFiles_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_ToolFiles_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ToolFiles_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT FK_ToolFiles_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_ToolFiles_Uploader FOREIGN KEY (UploadedByUserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT UQ_ToolFiles_RelativePath UNIQUE (RelativePath),
    CONSTRAINT CK_ToolFiles_Category CHECK (FileCategory IN ('APP', 'REFERENCE')),
    CONSTRAINT CK_ToolFiles_DisplayName CHECK (LEN(LTRIM(RTRIM(DisplayName))) > 0),
    CONSTRAINT CK_ToolFiles_OriginalFileName CHECK (LEN(LTRIM(RTRIM(OriginalFileName))) > 0),
    CONSTRAINT CK_ToolFiles_RelativePath CHECK (LEN(LTRIM(RTRIM(RelativePath))) > 0 AND RelativePath NOT LIKE N'%..%' AND RelativePath NOT LIKE N'%:%' AND LEFT(RelativePath, 1) NOT IN (N'/', N'\')),
    CONSTRAINT CK_ToolFiles_Extension CHECK (DATALENGTH(Extension) > 1 AND Extension LIKE '.%' AND Extension COLLATE Latin1_General_100_BIN2 = LOWER(Extension) COLLATE Latin1_General_100_BIN2 AND Extension NOT LIKE '% %' AND Extension NOT LIKE '%/%' AND Extension NOT LIKE '%\%'),
    CONSTRAINT CK_ToolFiles_FileSize CHECK (FileSizeBytes >= 0),
    CONSTRAINT CK_ToolFiles_SortOrder CHECK (SortOrder >= 0 AND (FileCategory <> 'APP' OR SortOrder = 0))
);
CREATE UNIQUE INDEX UX_ToolFiles_OneApp ON portal.ToolFiles (ToolId) WHERE FileCategory = 'APP';

CREATE TABLE portal.Notices
(
    NoticeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notices PRIMARY KEY,
    NoticeType varchar(20) NOT NULL,
    ToolId varchar(20) NULL,
    Title nvarchar(200) NOT NULL,
    Content nvarchar(max) NOT NULL,
    IsPublished bit NOT NULL CONSTRAINT DF_Notices_IsPublished DEFAULT (0),
    MailSentAt datetime2(3) NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_Notices_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Notices_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Notices_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_Notices_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Notices_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT FK_Notices_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_Notices_TypeTarget CHECK ((NoticeType = 'SYSTEM' AND ToolId IS NULL) OR (NoticeType = 'TOOL' AND ToolId IS NOT NULL)),
    CONSTRAINT CK_Notices_Title CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
    CONSTRAINT CK_Notices_Content CHECK (LEN(LTRIM(RTRIM(Content))) > 0 AND LEN(Content) <= 10000)
);

CREATE TABLE portal.FaqCategories
(
    CategoryId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FaqCategories PRIMARY KEY,
    CategoryName nvarchar(100) NOT NULL,
    SortOrder int NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_FaqCategories_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FaqCategories_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FaqCategories_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_FaqCategories_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FaqCategories_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT CK_FaqCategories_Name CHECK (LEN(LTRIM(RTRIM(CategoryName))) > 0),
    CONSTRAINT CK_FaqCategories_SortOrder CHECK (SortOrder >= 0)
);

CREATE TABLE portal.FaqItems
(
    FaqId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FaqItems PRIMARY KEY,
    CategoryId int NOT NULL,
    Question nvarchar(500) NOT NULL,
    Answer nvarchar(max) NOT NULL,
    SortOrder int NOT NULL,
    IsPublished bit NOT NULL CONSTRAINT DF_FaqItems_IsPublished DEFAULT (0),
    UpdateCount int NOT NULL CONSTRAINT DF_FaqItems_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FaqItems_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FaqItems_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_FaqItems_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FaqItems_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT FK_FaqItems_FaqCategories FOREIGN KEY (CategoryId) REFERENCES portal.FaqCategories (CategoryId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT UQ_FaqItems_Category_SortOrder UNIQUE (CategoryId, SortOrder),
    CONSTRAINT CK_FaqItems_Question CHECK (LEN(LTRIM(RTRIM(Question))) > 0),
    CONSTRAINT CK_FaqItems_Answer CHECK (LEN(LTRIM(RTRIM(Answer))) > 0 AND LEN(Answer) <= 10000),
    CONSTRAINT CK_FaqItems_SortOrder CHECK (SortOrder >= 0)
);

CREATE TABLE portal.CodeMaster
(
    CodeType varchar(50) NOT NULL,
    CodeValue varchar(20) NOT NULL,
    CodeName nvarchar(100) NOT NULL,
    SortOrder int NOT NULL,
    ColorCode varchar(7) NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_CodeMaster_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_CodeMaster_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_CodeMaster_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_CodeMaster_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_CodeMaster_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_CodeMaster PRIMARY KEY (CodeType, CodeValue),
    CONSTRAINT CK_CodeMaster_Type CHECK (LEN(LTRIM(RTRIM(CodeType))) > 0),
    CONSTRAINT CK_CodeMaster_Value CHECK (LEN(LTRIM(RTRIM(CodeValue))) > 0),
    CONSTRAINT CK_CodeMaster_Name CHECK (LEN(LTRIM(RTRIM(CodeName))) > 0),
    CONSTRAINT CK_CodeMaster_SortOrder CHECK (SortOrder >= 0),
    CONSTRAINT CK_CodeMaster_Color CHECK (ColorCode IS NULL OR (ColorCode LIKE '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]' COLLATE Latin1_General_100_BIN2))
);

CREATE TABLE portal.SystemSettings
(
    SettingCategory varchar(50) NOT NULL,
    SettingKey varchar(50) NOT NULL,
    SettingName nvarchar(100) NOT NULL,
    SettingValue nvarchar(2000) NULL,
    Description nvarchar(1000) NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_SystemSettings_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_SystemSettings_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_SystemSettings_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_SystemSettings_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_SystemSettings_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_SystemSettings PRIMARY KEY (SettingCategory, SettingKey),
    CONSTRAINT CK_SystemSettings_Category CHECK (LEN(LTRIM(RTRIM(SettingCategory))) > 0),
    CONSTRAINT CK_SystemSettings_Key CHECK (LEN(LTRIM(RTRIM(SettingKey))) > 0),
    CONSTRAINT CK_SystemSettings_Name CHECK (LEN(LTRIM(RTRIM(SettingName))) > 0),
    CONSTRAINT CK_SystemSettings_PublicationStatus CHECK (SettingCategory <> 'SITE' OR SettingKey <> 'PUBLICATION_STATUS' OR (SettingValue IS NOT NULL AND SettingValue IN (N'PUBLIC', N'PRIVATE'))),
    CONSTRAINT CK_SystemSettings_BusinessDate CHECK (SettingCategory <> 'TEST' OR SettingKey <> 'BUSINESS_DATE' OR SettingValue IS NULL OR (DATALENGTH(SettingValue) = 20 AND SettingValue COLLATE Latin1_General_100_BIN2 LIKE N'[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND TRY_CONVERT(date, SettingValue, 23) IS NOT NULL))
);

CREATE TABLE portal.ErrorCodes
(
    ToolId varchar(20) NOT NULL,
    ErrorNo int NOT NULL,
    ErrorCode varchar(100) NOT NULL,
    DisplayMessage nvarchar(2000) NOT NULL,
    ErrorLevel varchar(20) NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_ErrorCodes_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_ErrorCodes_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ErrorCodes_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_ErrorCodes_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_ErrorCodes_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_ErrorCodes PRIMARY KEY (ToolId, ErrorNo),
    CONSTRAINT UQ_ErrorCodes_ErrorCode UNIQUE (ErrorCode),
    CONSTRAINT CK_ErrorCodes_ErrorNo CHECK (ErrorNo BETWEEN 1 AND 99999),
    CONSTRAINT CK_ErrorCodes_ErrorCode CHECK (LEN(LTRIM(RTRIM(ErrorCode))) > 0),
    CONSTRAINT CK_ErrorCodes_Message CHECK (LEN(LTRIM(RTRIM(DisplayMessage))) > 0),
    CONSTRAINT CK_ErrorCodes_Level CHECK (ErrorLevel IN ('ERROR', 'CRITICAL')),
    CONSTRAINT CK_ErrorCodes_ToolFormat CHECK (ToolId = 'SYSTEM' OR ErrorCode = ToolId + '_' + RIGHT('00000' + CONVERT(varchar(5), ErrorNo), 5))
);

CREATE TABLE portal.FileUploadPolicies
(
    PolicyId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FileUploadPolicies PRIMARY KEY,
    ScopeType varchar(20) NOT NULL,
    ToolId varchar(20) NULL,
    PurposeCode varchar(30) NOT NULL,
    MaxFileSizeBytes bigint NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_FileUploadPolicies_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FileUploadPolicies_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FileUploadPolicies_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_FileUploadPolicies_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FileUploadPolicies_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT FK_FileUploadPolicies_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_FileUploadPolicies_MaxSize CHECK (MaxFileSizeBytes > 0),
    CONSTRAINT CK_FileUploadPolicies_ScopePurpose CHECK
    (
        (ScopeType = 'SITE' AND ToolId IS NULL AND PurposeCode IN ('INQUIRY_ATTACHMENT', 'USER_IMPORT')) OR
        (ScopeType = 'TOOL_COMMON' AND ToolId IS NULL AND PurposeCode IN ('REFERENCE', 'APP')) OR
        (ScopeType = 'TOOL' AND ToolId IS NOT NULL AND PurposeCode = 'TOOL_INPUT')
    )
);
CREATE UNIQUE INDEX UX_FileUploadPolicies_Common ON portal.FileUploadPolicies (ScopeType, PurposeCode) WHERE ToolId IS NULL;
CREATE UNIQUE INDEX UX_FileUploadPolicies_Tool ON portal.FileUploadPolicies (ScopeType, ToolId, PurposeCode) WHERE ToolId IS NOT NULL;

CREATE TABLE portal.FileUploadPolicyExtensions
(
    PolicyId int NOT NULL,
    Extension varchar(20) NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_FileUploadPolicyExtensions_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_FileUploadPolicyExtensions_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FileUploadPolicyExtensions_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_FileUploadPolicyExtensions_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_FileUploadPolicyExtensions_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_FileUploadPolicyExtensions PRIMARY KEY (PolicyId, Extension),
    CONSTRAINT FK_FileUploadPolicyExtensions_Policies FOREIGN KEY (PolicyId) REFERENCES portal.FileUploadPolicies (PolicyId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_FileUploadPolicyExtensions_Extension CHECK (DATALENGTH(Extension) > 1 AND Extension LIKE '.%' AND Extension COLLATE Latin1_General_100_BIN2 = LOWER(Extension) COLLATE Latin1_General_100_BIN2 AND Extension NOT LIKE '% %' AND Extension NOT LIKE '%,%' AND Extension NOT LIKE '%*%' AND Extension NOT LIKE '%/%' AND Extension NOT LIKE '%\%')
);

CREATE TABLE portal.InquiryDailySequences
(
    SequenceDate date NOT NULL CONSTRAINT PK_InquiryDailySequences PRIMARY KEY,
    LastNumber int NOT NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_InquiryDailySequences_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_InquiryDailySequences_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_InquiryDailySequences_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_InquiryDailySequences_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_InquiryDailySequences_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT CK_InquiryDailySequences_LastNumber CHECK (LastNumber BETWEEN 1 AND 9999)
);

CREATE TABLE portal.Inquiries
(
    InquiryId char(12) NOT NULL,
    CategoryCode varchar(20) NOT NULL,
    TargetType varchar(20) NOT NULL,
    ToolId varchar(20) NULL,
    SubmittedByUserId uniqueidentifier NOT NULL,
    Content nvarchar(2000) NOT NULL,
    Status varchar(20) NOT NULL CONSTRAINT DF_Inquiries_Status DEFAULT ('ACTION_REQUIRED'),
    AssigneeUserId uniqueidentifier NULL,
    AdminNote nvarchar(2000) NULL,
    UpdateCount int NOT NULL CONSTRAINT DF_Inquiries_UpdateCount DEFAULT (0),
    CreatedAt datetime2(3) NOT NULL CONSTRAINT DF_Inquiries_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Inquiries_CreatedBy DEFAULT USER_NAME(),
    UpdatedAt datetime2(3) NOT NULL CONSTRAINT DF_Inquiries_UpdatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedBy nvarchar(128) NOT NULL CONSTRAINT DF_Inquiries_UpdatedBy DEFAULT USER_NAME(),
    CONSTRAINT PK_Inquiries PRIMARY KEY (InquiryId),
    CONSTRAINT FK_Inquiries_Tools FOREIGN KEY (ToolId) REFERENCES portal.Tools (ToolId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_Inquiries_AspNetUsers_Submitter FOREIGN KEY (SubmittedByUserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT FK_Inquiries_AspNetUsers_Assignee FOREIGN KEY (AssigneeUserId) REFERENCES portal.AspNetUsers (UserId) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT CK_Inquiries_Id CHECK (InquiryId NOT LIKE '%[^0-9]%'),
    CONSTRAINT CK_Inquiries_Category CHECK (CategoryCode IN ('QUESTION', 'REQUEST', 'OPINION', 'PROBLEM', 'OTHER')),
    CONSTRAINT CK_Inquiries_Target CHECK ((TargetType = 'TOOL' AND ToolId IS NOT NULL) OR (TargetType IN ('PORTAL', 'OTHER') AND ToolId IS NULL)),
    CONSTRAINT CK_Inquiries_Status CHECK (Status IN ('ACTION_REQUIRED', 'IN_PROGRESS', 'UNDER_REVIEW', 'COMPLETED', 'NO_ACTION')),
    CONSTRAINT CK_Inquiries_Content CHECK (LEN(LTRIM(RTRIM(Content))) > 0)
);

CREATE TABLE log.ToolUsageLogs
(
    ToolUsageLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ToolUsageLogs PRIMARY KEY,
    ToolId varchar(20) NOT NULL,
    UserId uniqueidentifier NOT NULL,
    OccurredAt datetime2(3) NOT NULL,
    EventType varchar(30) NOT NULL,
    ResultCode varchar(20) NOT NULL,
    CorrelationId uniqueidentifier NULL,
    CONSTRAINT CK_ToolUsageLogs_EventType CHECK (EventType IN ('DESKTOP_DOWNLOAD', 'WEB_OPEN', 'WEB_EXECUTE')),
    CONSTRAINT CK_ToolUsageLogs_ResultCode CHECK (ResultCode IN ('SUCCESS', 'FAILURE'))
);

CREATE TABLE log.UserActivityLogs
(
    UserActivityLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserActivityLogs PRIMARY KEY,
    UserId uniqueidentifier NULL,
    OccurredAt datetime2(3) NOT NULL,
    EventType varchar(50) NOT NULL,
    ResultCode varchar(20) NOT NULL,
    FailureReason varchar(50) NULL,
    RequestPath nvarchar(500) NULL,
    OperationTargetType varchar(30) NULL,
    OperationTargetId varchar(100) NULL,
    OperationDetails nvarchar(1000) NULL,
    IpAddress varchar(45) NULL,
    CorrelationId uniqueidentifier NULL,
    CONSTRAINT CK_UserActivityLogs_EventType CHECK (LEN(LTRIM(RTRIM(EventType))) > 0),
    CONSTRAINT CK_UserActivityLogs_ResultCode CHECK (ResultCode IN ('SUCCESS', 'FAILURE', 'DENIED'))
);

CREATE TABLE log.SystemErrorLogs
(
    SystemErrorLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_SystemErrorLogs PRIMARY KEY,
    ErrorId uniqueidentifier NOT NULL,
    ErrorCode varchar(100) NULL,
    OccurredAt datetime2(3) NOT NULL,
    ApplicationName varchar(100) NOT NULL,
    ToolId varchar(20) NULL,
    UserId uniqueidentifier NULL,
    ErrorLevel varchar(20) NOT NULL,
    ErrorType nvarchar(300) NULL,
    ErrorMessage nvarchar(2000) NOT NULL,
    StackTrace nvarchar(max) NULL,
    RequestPath nvarchar(500) NULL,
    HttpMethod varchar(10) NULL,
    CorrelationId uniqueidentifier NULL,
    CONSTRAINT UQ_SystemErrorLogs_ErrorId UNIQUE (ErrorId),
    CONSTRAINT CK_SystemErrorLogs_ApplicationName CHECK (LEN(LTRIM(RTRIM(ApplicationName))) > 0),
    CONSTRAINT CK_SystemErrorLogs_ErrorLevel CHECK (ErrorLevel IN ('ERROR', 'CRITICAL')),
    CONSTRAINT CK_SystemErrorLogs_ErrorMessage CHECK (LEN(LTRIM(RTRIM(ErrorMessage))) > 0),
    CONSTRAINT CK_SystemErrorLogs_HttpMethod CHECK (HttpMethod IS NULL OR HttpMethod IN ('GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'))
);

COMMIT TRANSACTION;
GO

CREATE OR ALTER PROCEDURE portal.AllocateInquiryId
    @SequenceDate date,
    @InquiryId char(12) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @SequenceDate IS NULL THROW 50010, N'問い合わせ番号の採番日が必要です。', 1;

    DECLARE @NextNumber int;

    IF @@TRANCOUNT <> 0
        THROW 50012, N'問い合わせ番号の採番は、問い合わせ保存とは別のトランザクションで実行してください。', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE portal.InquiryDailySequences WITH (UPDLOCK, HOLDLOCK)
           SET @NextNumber = LastNumber = LastNumber + 1
         WHERE SequenceDate = @SequenceDate
           AND LastNumber < 9999;

        IF @@ROWCOUNT = 0
        BEGIN
            IF EXISTS (SELECT 1 FROM portal.InquiryDailySequences WITH (UPDLOCK, HOLDLOCK) WHERE SequenceDate = @SequenceDate)
                THROW 50011, N'問い合わせ番号が一日の上限9,999件へ到達しました。', 1;

            INSERT portal.InquiryDailySequences (SequenceDate, LastNumber) VALUES (@SequenceDate, 1);
            SET @NextNumber = 1;
        END;

        SET @InquiryId = CONVERT(char(8), @SequenceDate, 112) + RIGHT('0000' + CONVERT(varchar(4), @NextNumber), 4);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* 共通監査列対象テーブルへ、更新回数・更新日時・DBユーザーを自動設定するトリガーを生成する。 */
DECLARE @AuditedTables TABLE (SchemaName sysname NOT NULL, TableName sysname NOT NULL);
INSERT @AuditedTables (SchemaName, TableName) VALUES
('portal','ToolCategories'), ('portal','Tools'), ('portal','UserPreferences'), ('portal','UserToolFavorites'),
('portal','ToolVersionHistories'), ('portal','ToolFiles'), ('portal','Notices'), ('portal','FaqCategories'),
('portal','FaqItems'), ('portal','Inquiries'), ('portal','CodeMaster'), ('portal','SystemSettings'),
('portal','ErrorCodes'), ('portal','FileUploadPolicies'), ('portal','FileUploadPolicyExtensions'),
('portal','InquiryDailySequences');

DECLARE @SchemaName sysname, @TableName sysname, @Join nvarchar(max), @Sql nvarchar(max);
DECLARE audit_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT SchemaName, TableName FROM @AuditedTables;
OPEN audit_cursor;
FETCH NEXT FROM audit_cursor INTO @SchemaName, @TableName;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @Join = STRING_AGG(N't.' + QUOTENAME(c.name) + N' = i.' + QUOTENAME(c.name), N' AND ')
                   WITHIN GROUP (ORDER BY ic.key_ordinal)
      FROM sys.tables tb
      JOIN sys.schemas s ON s.schema_id = tb.schema_id
      JOIN sys.indexes ix ON ix.object_id = tb.object_id AND ix.is_primary_key = 1
      JOIN sys.index_columns ic ON ic.object_id = ix.object_id AND ic.index_id = ix.index_id
      JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
     WHERE s.name = @SchemaName AND tb.name = @TableName;

    SET @Sql = N'CREATE TRIGGER ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(N'TR_' + @TableName + N'_AuditUpdate') +
        N' ON ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N' AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL(@@PROCID, ''AFTER'', ''DML'') > 1 RETURN;
    DECLARE @AuditNow datetime2(3) = SYSUTCDATETIME();
    UPDATE t
       SET UpdateCount = CASE WHEN d.CreatedAt IS NULL THEN 0 ELSE d.UpdateCount + 1 END,
           CreatedAt = COALESCE(d.CreatedAt, @AuditNow),
           CreatedBy = COALESCE(d.CreatedBy, USER_NAME()),
           UpdatedAt = @AuditNow,
           UpdatedBy = USER_NAME()
      FROM ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N' t
      JOIN inserted i ON ' + @Join + N'
      LEFT JOIN deleted d ON ' + REPLACE(@Join, N'i.', N'd.') + N';
END;';
    EXEC sys.sp_executesql @Sql;

    FETCH NEXT FROM audit_cursor INTO @SchemaName, @TableName;
END;
CLOSE audit_cursor;
DEALLOCATE audit_cursor;
GO
