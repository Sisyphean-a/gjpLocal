USE [master];
GO

RESTORE DATABASE swcs
FROM DISK = 'F:\swcs_backup.bak'
WITH REPLACE;
GO
