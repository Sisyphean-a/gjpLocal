USE [master];
GO

/*
使用前先执行下面语句查看逻辑文件名，再替换 MOVE 中的占位符：
RESTORE FILELISTONLY FROM DISK = 'F:\swcs_backup.bak';
*/

ALTER DATABASE [swcs] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO

RESTORE DATABASE [swcs]
FROM DISK = 'F:\swcs_backup.bak'
WITH REPLACE,
     MOVE 'swcs_Data' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\swcs.mdf',
     MOVE 'swcs_Log' TO 'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\swcs_log.ldf',
     STATS = 5;
GO

ALTER DATABASE [swcs] SET MULTI_USER;
GO
