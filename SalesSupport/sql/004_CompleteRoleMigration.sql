-- Portalの migrate-user-roles コマンドが成功し、USERが0件であることを確認してから実行する。
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF OBJECT_ID(N'portal.Roles', N'U') IS NULL THROW 50400, N'003を先に適用してください。', 1;
IF EXISTS (SELECT 1 FROM portal.AspNetUsers WHERE RoleCode = 'USER')
    THROW 50401, N'USERユーザーが残っています。Identity APIによる移行を完了してください。', 1;
BEGIN TRANSACTION;
DELETE FROM portal.Roles WHERE RoleCode = 'USER';
COMMIT TRANSACTION;
