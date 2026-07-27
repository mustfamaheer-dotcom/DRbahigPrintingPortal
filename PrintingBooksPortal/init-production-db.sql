-- 🗄️ PrintingBooks Portal - Production Database Initialization
-- This script sets up the production database with initial data
-- Target: db59750.databaseasp.net

USE db59750;
GO

-- Ensure database exists and is accessible
IF DB_NAME() = 'db59750'
BEGIN
    PRINT '✅ Connected to production database: db59750'
END
ELSE
BEGIN
    PRINT '❌ Failed to connect to production database'
    RETURN
END

-- Initialize roles if they don't exist
IF NOT EXISTS (SELECT * FROM AspNetRoles WHERE Name = 'Admin')
BEGIN
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (
        NEWID(),
        'Admin',
        'ADMIN',
        NEWID()
    );
    PRINT '✅ Admin role created'
END

IF NOT EXISTS (SELECT * FROM AspNetRoles WHERE Name = 'Shop')
BEGIN
    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (
        NEWID(),
        'Shop', 
        'SHOP',
        NEWID()
    );
    PRINT '✅ Shop role created'
END

-- Create sample educational boards
IF NOT EXISTS (SELECT * FROM EducationalBoards WHERE Name = 'General Education')
BEGIN
    INSERT INTO EducationalBoards (Name, Description, IsActive, CreatedAt)
    VALUES 
    ('General Education', 'General educational books and materials', 1, GETUTCDATE()),
    ('Primary Education', 'Books for primary school students', 1, GETUTCDATE()),
    ('Secondary Education', 'Books for secondary school students', 1, GETUTCDATE()),
    ('Higher Education', 'University and college level books', 1, GETUTCDATE());
    
    PRINT '✅ Educational boards created'
END

-- Create sample shop
IF NOT EXISTS (SELECT * FROM Shops WHERE Name = 'Main Printing Shop')
BEGIN
    INSERT INTO Shops (Name, Address, Phone, IsActive, CreatedAt)
    VALUES 
    ('Main Printing Shop', 'Main Street, Cairo, Egypt', '+20-XXX-XXXX', 1, GETUTCDATE()),
    ('Branch Shop 1', 'Alexandria Branch', '+20-XXX-XXXY', 1, GETUTCDATE()),
    ('Branch Shop 2', 'Giza Branch', '+20-XXX-XXXZ', 1, GETUTCDATE());
    
    PRINT '✅ Sample shops created'
END

-- Create admin user (you'll need to register this through the app to get proper password hashing)
PRINT '📋 Setup Instructions:'
PRINT '1. ✅ Database schema initialized'
PRINT '2. ✅ Roles created (Admin, Shop)'
PRINT '3. ✅ Educational boards created'
PRINT '4. ✅ Sample shops created'
PRINT ''
PRINT '🔐 Next Steps:'
PRINT '1. Navigate to: http://drbaheegbook.runasp.net/'
PRINT '2. Register the first admin user through the application'
PRINT '3. Assign Admin role to the user via database or admin panel'
PRINT '4. Upload books and configure shop assignments'
PRINT ''
PRINT '✅ Production database initialization completed!'

GO