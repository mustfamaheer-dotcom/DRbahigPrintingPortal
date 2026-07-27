# Server Migration Guide: RunASP.NET → cPanel (Physics Award)

## Overview

| Current | Target |
|---------|--------|
| **Host**: RunASP.NET (drbaheegbook.runasp.net) | **App**: Stays on RunASP.NET (drbaheegbook.runasp.net) |
| **DB**: SQL Server (db59750.databaseasp.net) | **DB**: MySQL/MariaDB on cPanel (premium250.web-hosting.com) |
| **Domain**: drbaheegbook.runasp.net | **Domain**: physicsaward.com |
| **Web Server**: IIS | **DNS**: cPanel nameservers pointing to RunASP.NET IP |

**App stays on RunASP.NET (Windows/IIS)** — cPanel does not support .NET hosting. Only the database moves.

---

## What I Need From You

Before I can complete this migration, please provide:

| # | Action | How To |
|---|--------|--------|
| 1 | **Enable Remote MySQL** | Go to cPanel → **Remote MySQL** → Add host `site79455.siteasp.net` or IP `188.40.211.1` |
| 2 | **Find MySQL hostname** | In cPanel, find the external MySQL hostname (NOT `localhost`). It's usually shown in "Remote MySQL" section or in "MySQL Database" details page. Something like `mysql.web-hosting.com` or an IP. |
| 3 | **Confirm MySQL user has remote access** | Ensure the MySQL user you create has `[user]@[any-host]` or `[user]@188.40.211.1` permissions |
| 4 | **Backup current SQL Server DB** | Use SSMS or the export below to backup before we start |

---

## Phase 1: cPanel MySQL Setup

### 1.1 Create Database

1. Login to cPanel: `https://premium250.web-hosting.com/cpanel`
2. Navigate: **Databases → MySQL Databases**
3. Create a new database (e.g., `printingbooks`)
   - Full name will be auto-prefixed: `physyjig_printingbooks`
4. Create a new MySQL user:
   - Username: `printingbooks_user`
   - Password: Generate a strong password
   - Full name: `physyjig_printingbooks_user`
5. Add user to the database with **ALL PRIVILEGES**

### 1.2 Enable Remote MySQL

1. In cPanel: **Databases → Remote MySQL**
2. Add the following host:
   - **Host**: `site79455.siteasp.net`
   - Or **IP**: `188.40.211.1`
   - Click **Add Host**

This allows RunASP.NET to connect to your MySQL database.

### 1.3 Note Connection Details

Record these (you'll need them for the connection string):

| Field | Example Value |
|-------|--------------|
| **Server Host** | `mysql.web-hosting.com` (or IP from Remote MySQL) |
| **Database Name** | `physyjig_printingbooks` |
| **Username** | `physyjig_printingbooks_user` |
| **Password** | _[your generated password]_ |
| **Port** | `3306` |

---

## Phase 2: Export SQL Server Data

### 2.1 Using SSMS (Recommended)

1. Open **SQL Server Management Studio**
2. Connect to `db59750.databaseasp.net`:
   - Server: `db59750.databaseasp.net`
   - Login: `db59750`
   - Password: `6Bc?o@T9_2fA`
3. Right-click **db59750** → **Tasks** → **Generate Scripts**
4. In the Wizard:
   - **Select specific database objects**: Check all tables:
     - `Shops`, `EducationalBoards`, `Books`, `ShopBookAssignments`, `PrintLogs`, `SystemSettings`
     - Identity tables: `AspNetUsers`, `AspNetRoles`, `AspNetRoleClaims`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`
   - **Advanced**:
     - Types of data to script: **Schema and data**
     - Script USE DATABASE: **False**
     - Script for Server Version: **MySQL-compatible** (or just note the conversion needed)
5. Save as `exported_sqlserver.sql`

### 2.2 Using Command Line (Alternative)

```bash
sqlcmd -S db59750.databaseasp.net -U db59750 -P "6Bc?o@T9_2fA" -d db59750 -o backup.sql -Q "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'"
```

Or use your existing SQL scripts:
- `CreateTables.sql` — contains schema
- `SeedData.sql` — contains seed data (roles, admin user, educational boards)

### 2.3 Export Uploaded PDF Files

All PDF books are in `App_Data/Books/` on the RunASP.NET server.
- **Download the entire folder** via FTP
- **Re-upload** after deployment (app stays on same server, so these stay in place)

---

## Phase 3: Convert Schema to MySQL

### 3.1 Data Type Mapping

Your schema is **simple** — all `int` PKs, no GUIDs, no spatial types. Easy conversion.

| SQL Server Type | MySQL Type | Notes |
|----------------|------------|-------|
| `int IDENTITY(1,1)` | `INT AUTO_INCREMENT` | Primary keys |
| `nvarchar(n)` | `VARCHAR(n)` | Strings |
| `nvarchar(MAX)` | `LONGTEXT` | Long strings |
| `bit` | `TINYINT(1)` | Booleans |
| `datetime2` | `DATETIME` | Dates |
| `bigint` | `BIGINT` | FileSizeBytes |
| `uniqueidentifier` | `VARCHAR(36)` | Identity PKs (if any) |

### 3.2 Schema Conversion SQL

Run this in phpMyAdmin AFTER importing but BEFORE data import (or modify your SQL file):

```sql
-- Modify the exported SQL file to replace:
-- IDENTITY(1,1) → AUTO_INCREMENT
-- [dbo]. → (remove)
-- GETDATE() → NOW()
-- nvarchar(MAX) → LONGTEXT
-- bit → TINYINT(1)
-- datetime2 → DATETIME
-- Default constraint names → remove
-- No square brackets needed in MySQL
```

### 3.3 Create MySQL Schema (Alternative to Conversion)

Alternatively, let EF Core generate the schema directly. After code changes are applied:

```bash
# Connection to MySQL
dotnet ef database update --connection "Server=[HOST];Database=[DB];Uid=[USER];Pwd=[PASSWORD];SslMode=None;"
```

This will create all tables with MySQL-compatible types. Then import ONLY the data (not schema).

---

## Phase 4: Import Data into MySQL

### 4.1 Via phpMyAdmin

1. Login to cPanel → **phpMyAdmin**
2. Select database: `physyjig_printingbooks`
3. Click **Import** tab
4. Choose the MySQL-converted SQL file
5. Maximum filesize: usually 50MB (split file if needed)
6. Click **Go**

### 4.2 Via MySQL Workbench (Recommended for large files)

1. Install MySQL Workbench
2. Connect to your cPanel MySQL:
   - Hostname: `[external-mysql-host]`
   - Port: `3306`
   - Username: `physyjig_printingbooks_user`
   - Password: _[your password]_
3. **Server → Data Import**
4. Import the converted SQL file

### 4.3 Via Command Line (if SSH available)

```bash
mysql -h [HOST] -u physyjig_printingbooks_user -p physyjig_printingbooks < converted_data.sql
```

---

## Phase 5: Code Changes (Will Be Done By Me)

These changes are automatically handled. No action needed from you.

### 5.1 NuGet Package Swap

| Remove | Add |
|--------|-----|
| `Microsoft.EntityFrameworkCore.SqlServer` 10.0.9 | `MySql.EntityFrameworkCore` 10.0.7 |

### 5.2 Program.cs — Provider Change

```csharp
// BEFORE (SQL Server):
if (isProduction)
    options.UseSqlServer(connectionString);
else
    options.UseSqlite(connectionString);

// AFTER (MySQL):
if (isProduction)
    options.UseMySQL(connectionString);
else
    options.UseSqlite(connectionString);
```

### 5.3 DesignTimeDbContextFactory.cs

```csharp
// BEFORE:
optionsBuilder.UseSqlServer(connectionString);

// AFTER:
optionsBuilder.UseMySQL(connectionString);
```

### 5.4 SettingsService.cs — Add MySQL Branch

MySQL-compatible raw SQL for `SystemSettings` table creation:

```csharp
if (_db.Database.IsMySql())
{
    await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS SystemSettings (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    `Key` VARCHAR(100) NOT NULL,
    ValueBool TINYINT(1) NOT NULL DEFAULT 1,
    ValueString VARCHAR(2000) NULL,
    UNIQUE INDEX IX_SystemSettings_Key (`Key`)
)");
}
```

### 5.5 Regenerate Migrations

- Delete existing `Migrations/` folder
- Generate new MySQL migrations:
  ```bash
  dotnet ef migrations add InitialCreate
  ```

### 5.6 Updated Connection String

In `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=[MYSQL_HOST];Database=physyjig_printingbooks;Uid=physyjig_printingbooks_user;Pwd=[YOUR_PASSWORD];SslMode=None;"
  },
  "AppUrl": "https://physicsaward.com",
  "AllowedHosts": "physicsaward.com"
}
```

---

## Phase 6: Deploy to RunASP.NET

### 6.1 Publish

```bash
dotnet publish -c Release -o ./publish
```

### 6.2 Deploy via MSDeploy

Using your existing credentials:

```bash
msdeploy.exe ^
  -source:contentPath=.\publish ^
  -dest:contentPath=site79455,computerName=https://site79455.siteasp.net:8172/msdeploy.axd,userName=site79455,password=Q#r8_q3D6Nj% ^
  -verb:sync ^
  -allowUntrusted ^
  -enableRule:AppOffline
```

Or use the deploy script:

```bash
$env:DEPLOY_PASSWORD = "Q#r8_q3D6Nj%"
.\deploy.ps1
```

### 6.3 Or Deploy via RunASP.NET Dashboard

1. Login to `https://monsterasp.net`
2. Go to your site: `drbaheegbook.runasp.net`
3. Use the **File Manager** or **WebDeploy** option
4. Upload the published files

---

## Phase 7: DNS Configuration

After deployment is verified working:

### Option A: A Record (Recommended)

| Record | Type | Value |
|--------|------|-------|
| `physicsaward.com` | A | `188.40.211.1` |
| `www.physicsaward.com` | CNAME | `physicsaward.com` |

### Option B: CNAME

| Record | Type | Value |
|--------|------|-------|
| `physicsaward.com` | CNAME | `drbaheegbook.runasp.net` |

Update DNS in cPanel:
1. **Domains → Zone Editor**
2. Select `physicsaward.com`
3. Add/update the records
4. **Wait 24-48 hours** for propagation

During propagation, test using:
- Local `hosts` file edit
- Or use the cPanel temporary URL

---

## Phase 8: Verification Checklist

| # | Test | Expected |
|---|------|----------|
| 1 | `https://physicsaward.com` | Site loads |
| 2 | Login as admin | `admin@printingbooks.com` / `Admin@123` |
| 3 | Dashboard loads | Shops, books, boards visible |
| 4 | Click a book | PDF viewer opens |
| 5 | Test printing | Print job created |
| 6 | Watermark appears | Diagonal watermark on PDF |
| 7 | Print agent connection | Agent polls and processes jobs |
| 8 | System Settings page | Settings load and save |

---

## Phase 9: Rollback Plan (If Things Go Wrong)

If the migration fails:

### Database Rollback

1. Keep the **old SQL Server database** untouched (`db59750.databaseasp.net`)
2. Revert `appsettings.Production.json` to old SQL Server connection string
3. Revert `Program.cs` to `UseSqlServer()`
4. Redeploy old code to RunASP.NET

### Full Rollback

1. Keep the **deploy.bat** and **deploy.ps1** scripts (unchanged)
2. The old SQL Server database is still functional
3. No data will be lost — the migration is additive (we don't delete SQL Server)

**Downtime should be < 30 minutes** for a rollback.

---

## Migration Checklist

```
□ cPanel MySQL database created
□ MySQL user created with ALL PRIVILEGES
□ Remote MySQL enabled (188.40.211.1 added)
□ SQL Server data exported
□ Data converted & imported to MySQL
□ NuGet packages updated (me)
□ Program.cs updated (me)
□ DesignTimeDbContextFactory.cs updated (me)
□ SettingsService.cs updated (me)
□ Migrations regenerated (me)
□ appsettings.Production.json updated
□ Build verified (me)
□ Deployed to RunASP.NET
□ DNS updated
□ Tests passed
□ Old database kept as backup for 7 days
```

---

## Quick Reference

```bash
# Add MySQL EF Core provider
dotnet add package MySql.EntityFrameworkCore --version 10.0.7

# Remove SQL Server provider
dotnet remove package Microsoft.EntityFrameworkCore.SqlServer

# Generate MySQL migration
dotnet ef migrations add InitialCreate

# Apply migration to MySQL
dotnet ef database update --connection "Server=[HOST];Database=[DB];Uid=[USER];Pwd=[PWD];SslMode=None;"

# Publish app
dotnet publish -c Release -o ./publish

# Deploy to RunASP.NET
$env:DEPLOY_PASSWORD = "Q#r8_q3D6Nj%"
.\deploy.ps1

# Find RunASP.NET IP
nslookup drbaheegbook.runasp.net
```
