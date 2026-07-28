-- ============================================================
-- Data Migration: Old Shops/ShopBookAssignments → New Schema
-- ============================================================
-- Run this AFTER deploying the new migration and verifying
-- that the new tables (Bookshops, Teachers, etc.) exist.
-- ============================================================

-- Step 1: Copy existing Shops to Bookshops
INSERT INTO [dbo].[Bookshops] ([Name], [Address], [Phone], [IsActive], [CreatedAt])
SELECT [Name], [Address], [Phone], [IsActive], [CreatedAt]
FROM [dbo].[Shops];

PRINT 'Step 1 done: Shops copied to Bookshops';

-- Step 2: Create a default Teacher for admin context
-- (Skip this step — create Teachers through the Admin UI after deployment)
PRINT 'Step 2: Create Teachers via Admin UI at /admin/create-teacher';

-- Step 3: Link Teachers to Bookshops via TeacherBookshopLinks
-- (Use the Teacher Bookshop Management page in the Teacher dashboard)
PRINT 'Step 3: Link Teachers to Bookshops via Teacher dashbaord > Bookshops';

-- Step 4: Migrate Books — set TeacherId for existing books
-- (All existing books belong to the first created Teacher)
-- UPDATE [dbo].[Books] SET [TeacherId] = <TEACHER_ID> WHERE [TeacherId] IS NULL;
PRINT 'Step 4: Update Book TeacherId via SQL or admin UI';

-- Step 5: Old tables can be dropped after verifying data
-- DROP TABLE [dbo].[ShopBookAssignments];
-- DROP TABLE [dbo].[Shops];
PRINT 'Step 5: Drop old tables after verification';

PRINT '============================================';
PRINT 'Data migration complete.';
PRINT '============================================';
