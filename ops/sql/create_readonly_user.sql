USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'swcs_reader')
BEGIN
    CREATE LOGIN [swcs_reader] WITH PASSWORD = N'ChangeMeStrongPassword!', CHECK_POLICY = ON;
END
GO

USE [swcs];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'swcs_reader')
BEGIN
    CREATE USER [swcs_reader] FOR LOGIN [swcs_reader];
END
GO

ALTER ROLE [db_datareader] ADD MEMBER [swcs_reader];
GO

DENY INSERT, UPDATE, DELETE, EXECUTE TO [swcs_reader];
GO
