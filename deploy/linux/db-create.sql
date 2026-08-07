-- ============================================================================
-- DR. Bahig Books Portal - Create database and app login on the new VPS
-- Run as SA (or sysadmin) on the new SQL Server instance.
-- ============================================================================

IF DB_ID(N'PrintingBooksPortal') IS NULL
BEGIN
    CREATE DATABASE [PrintingBooksPortal];
    PRINT 'Database PrintingBooksPortal created.';
END
ELSE
BEGIN
    PRINT 'Database PrintingBooksPortal already exists.';
END
GO

-- Login used by the web app (password = generated, stored in appsettings.Production.json)
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'booksportal_app')
BEGIN
    CREATE LOGIN [booksportal_app]
        WITH PASSWORD = N'REPLACE_WITH_GENERATED_DB_PASSWORD',
             CHECK_POLICY = ON,
             CHECK_EXPIRATION = OFF;
    PRINT 'Login booksportal_app created.';
END
ELSE
BEGIN
    PRINT 'Login booksportal_app already exists.';
END
GO

USE [PrintingBooksPortal];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'booksportal_app')
BEGIN
    CREATE USER [booksportal_app] FOR LOGIN [booksportal_app];
END
GO

-- App needs DDL rights so EF MigrateAsync() can create/update the schema on startup
ALTER ROLE [db_owner] ADD MEMBER [booksportal_app];
PRINT 'booksportal_app granted db_owner.';
GO

-- ============================================================================
-- Optional: pre-check row counts after data import (should match old db59750)
-- SELECT 'AspNetUsers' t, COUNT(*) n FROM AspNetUsers
-- UNION ALL SELECT 'AspNetRoles', COUNT(*) FROM AspNetRoles
-- UNION ALL SELECT 'Books', COUNT(*) FROM Books
-- UNION ALL SELECT 'Shops', COUNT(*) FROM Shops
-- UNION ALL SELECT 'ShopBookAssignments', COUNT(*) FROM ShopBookAssignments
-- UNION ALL SELECT 'PrintLogs', COUNT(*) FROM PrintLogs
-- UNION ALL SELECT 'EducationalBoards', COUNT(*) FROM EducationalBoards
-- UNION ALL SELECT 'SystemSettings', COUNT(*) FROM SystemSettings;
-- ============================================================================
