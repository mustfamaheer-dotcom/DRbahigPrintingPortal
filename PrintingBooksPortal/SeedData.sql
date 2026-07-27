-- Seed roles
IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'Admin')
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'Admin', 'ADMIN', NEWID());

IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = 'Shop')
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (NEWID(), 'Shop', 'SHOP', NEWID());

-- Seed admin user (password: Admin@123)
DECLARE @AdminId NVARCHAR(450) = NEWID();
IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE Email = 'admin@printingbooks.com')
BEGIN
    INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName)
    VALUES (@AdminId, 'admin@printingbooks.com', 'ADMIN@PRINTINGBOOKS.COM', 'admin@printingbooks.com', 'ADMIN@PRINTINGBOOKS.COM', 1,
            'AQAAAAIAAYagAAAAEOUhfO3Th1pDkNP49TSzYJ1kKygqFBSEp19xSv7vCUIbHcy9AE7IWVgpr3LhnCXJyg==',
            NEWID(), NEWID(), 0, 0, 1, 0, 'System Administrator');

    -- Assign Admin role
    INSERT INTO AspNetUserRoles (UserId, RoleId)
    SELECT @AdminId, Id FROM AspNetRoles WHERE Name = 'Admin';
END;

-- Seed educational boards
IF NOT EXISTS (SELECT 1 FROM EducationalBoards WHERE Name = 'Cambridge IGCSE')
    INSERT INTO EducationalBoards (Name, Description, IsActive, CreatedAt)
    VALUES ('Cambridge IGCSE', 'Cambridge International General Certificate of Secondary Education', 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM EducationalBoards WHERE Name = 'Edexcel International')
    INSERT INTO EducationalBoards (Name, Description, IsActive, CreatedAt)
    VALUES ('Edexcel International', 'Pearson Edexcel International Curriculum', 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM EducationalBoards WHERE Name = 'IB Diploma')
    INSERT INTO EducationalBoards (Name, Description, IsActive, CreatedAt)
    VALUES ('IB Diploma', 'International Baccalaureate Diploma Programme', 1, SYSDATETIME());

IF NOT EXISTS (SELECT 1 FROM EducationalBoards WHERE Name = 'National Curriculum')
    INSERT INTO EducationalBoards (Name, Description, IsActive, CreatedAt)
    VALUES ('National Curriculum', 'Local National Educational Board', 1, SYSDATETIME());

PRINT 'Seed data completed successfully.';
GO
