# Database & Server Documentation

## Overview

- **Tech Stack**: ASP.NET Core 10.0 (Blazor Server), C#, .NET 10
- **ORM**: Entity Framework Core 10.0.9
- **Database**: SQL Server (production) / SQLite (development)
- **Authentication**: ASP.NET Core Identity
- **Real-time**: SignalR

---

## Database

### Production Database (SQL Server)

| Detail | Value |
|--------|-------|
| **Server** | `db59750.databaseasp.net` |
| **Database Name** | `db59750` |
| **User** | `db59750` |
| **Password** | `6Bc?o@T9_2fA` |
| **Connection String** (appsettings.Production.json) | `Server=db59750.databaseasp.net; Database=db59750; User Id=db59750; Password=6Bc?o@T9_2fA; Encrypt=False; MultipleActiveResultSets=True; Connection Timeout=30; Command Timeout=60;` |

### Development Database (SQLite)

- **File**: `PrintingBooksPortal.db` (in project root)
- **Connection String**: `Data Source=PrintingBooksPortal.db`

### Entity Models (7 tables + 7 Identity tables)

**Custom Tables**:
1. **Shops** — Bookstore branches
2. **EducationalBoards** — Curriculum boards (Cambridge IGCSE, Edexcel, IB, National)
3. **Books** — PDF books with file path, page count, etc.
4. **ShopBookAssignments** — Many-to-many: which books assigned to which shops
5. **PrintLogs** — Audit log of every print action
6. **SystemSettings** — Key/Value settings (watermark toggle, watermark text)
7. **AspNetUsers** (extended) — Identity users with ShopId and FullName

**Identity Tables** (auto-managed by ASP.NET Core Identity):
AspNetRoles, AspNetRoleClaims, AspNetUserClaims, AspNetUserLogins, AspNetUserRoles, AspNetUserTokens

### Entity Relationships

```
Shop (1) ──< ShopBookAssignment >── (1) Book
Shop (1) ──< PrintLog >── (1) Book
EducationalBoard (1) ──< Book >── (1) ShopBookAssignment
Shop (1) ──< AspNetUsers (ApplicationUser)
```

### Migrations

3 migrations applied to production:
1. `20260713225749_InitialCreate` — All base tables
2. `20260714203452_AddSystemSettings` — SystemSettings table
3. `20260714203453_AddValueStringToSystemSettings` — Added ValueString column

To generate a new migration:
```
dotnet ef migrations add MigrationName
```

To apply migrations to a database:
```
dotnet ef database update --connection "YourConnectionString"
```

### Standalone SQL Scripts

- **`CreateTables.sql`** — Full schema creation for SQL Server
- **`SeedData.sql`** — Seeds roles, admin user, and educational boards
- **`init-production-db.sql`** — Production initialization script

### Database Initialization (Program.cs)

- **Development**: Tries `MigrateAsync()`, falls back to `EnsureCreatedAsync()`
- **Production**: Tries `MigrateAsync()` (logs warning on failure)
- **Seed** (`DbSeeder.SeedAsync`): Creates Admin/Shop roles, default admin user (`admin@printingbooks.com` / `Admin@123`), and 4 educational boards

### Default Admin Credentials

- **Email**: `admin@printingbooks.com`
- **Password**: `Admin@123`

---

## Server Configuration

### Production Server (RunASP.NET)

| Detail | Value |
|--------|-------|
| **Application URL** | `https://drbaheegbook.runasp.net` |
| **Web Server** | IIS (via RunASP.NET shared hosting) |
| **App URL Config** | `https://drbaheegbook.runasp.net` |
| **Allowed Hosts** | `drbaheegbook.runasp.net` |

### Development Server

| Detail | Value |
|--------|-------|
| **URL** | `http://localhost:5035` |
| **Environment** | Development |
| **ASPNETCORE_ENVIRONMENT** | `Development` |

### Print Agent (BookShopPrintAgent)

| Detail | Value |
|--------|-------|
| **URL** | `http://localhost:8080` |
| **Framework** | ASP.NET Core 10.0 |
| **Purpose** | Polls server for print jobs, downloads & prints via SumatraPDF |

### Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Base config (shared defaults) |
| `appsettings.Development.json` | SQLite connection, local dev URL |
| `appsettings.Production.json` | SQL Server connection, production URL, secrets |
| `Properties/launchSettings.json` | Dev server port and environment |
| `BookShopPrintAgent/appsettings.json` | Print agent config (server URL, API key, owner password) |

### Key Configuration Values (Production)

```json
{
  "AppUrl": "https://drbaheegbook.runasp.net",
  "OwnerPassword__KeyVaultOrEnvVar": "P8mKx9#jL2vR$5nWq7cY",
  "AgentSettings:ApiKey": "tKwXJ5L38lEQbjygvSk7H97EVFXtlusU"
}
```

### Security Settings

- **HTTPS**: Enforced globally, HSTS in production
- **Cookies**: HttpOnly, SameSite=Strict, Secure=Always, 8hr sliding expiry
- **Authentication**: ASP.NET Core Identity with Admin/Shop roles
- **PDF Encryption**: AES-128 (iText7) with user + owner password
- **Watermarking**: Diagonal watermark on every PDF page with shop/user info
- **Print Tokens**: 5-minute time-limited single-use tokens
- **API Key**: Agent authentication via `X-Api-Key` header
- **Anti-forgery**: Enabled globally (except login endpoint)

### Middleware Pipeline Order

1. Forwarded Headers
2. Exception Handler
3. HSTS
4. HTTPS Redirection
5. Static Files
6. Routing
7. Authentication
8. Authorization
9. Antiforgery
10. Static Assets
11. Razor Components (Interactive Server)
12. Controllers
13. SignalR Hub (`/hubs/print`)

### API Endpoints

#### Authentication
| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| POST | `/api/login` | Anonymous | Form login |

#### Analytics (Admin)
| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/analytics/print-summary` | Per-shop, daily, weekly, recent stats |
| GET | `/api/analytics/print-trends` | 30-day print trends |

#### Secure PDF
| Method | Route | Auth | Purpose |
|--------|-------|------|---------|
| GET | `/api/pdf/view-secure/{bookId}` | Shop, Admin | View watermarked PDF (base64) |
| POST | `/api/pdf/process-print` | Shop | Create encrypted print job |
| GET | `/api/pdf/print-file/{jobId}` | Shop, Admin | Download print file (one-time) |
| GET | `/api/pdf/download-secured/{jobId}` | API Key | Agent downloads encrypted PDF |
| GET | `/api/pdf/print/{bookId}?token=` | Token | Direct print with token |
| GET | `/api/pdf/print-token/{bookId}` | Shop | Generate 5-min print token |
| GET | `/api/pdf/print-agent/pending` | API Key | List pending job IDs |
| POST | `/api/pdf/print-agent/claim/{jobId}` | API Key | Claim a pending job |
| POST | `/api/pdf/print-agent/release/{jobId}` | API Key | Release job back to queue |

#### SignalR Hub
| Route | Auth |
|-------|------|
| `/hubs/print` | Shop role |

---

## How to Move to Another Server

### Step 1: Backup the Database

**Option A — SQL Server Backup**:
```sql
BACKUP DATABASE db59750 TO DISK = 'C:\backup\printingbooks.bak'
```

**Option B — Generate Script** (for full migration):
Use SSMS: Right-click database → Tasks → Generate Scripts → Include schema + data

**Option C — Using the SQL scripts in the project**:
1. Run `CreateTables.sql` on the new database
2. Run `SeedData.sql` to populate default data

### Step 2: Transfer Uploaded Files

- All uploaded PDF books are stored in: **`App_Data/Books/`**
- Copy the entire `App_Data/Books/` directory to the new server

### Step 3: Update Configuration Files

In `appsettings.Production.json`, update:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=NEW_SERVER; Database=NEW_DB; User Id=NEW_USER; Password=NEW_PASSWORD; Encrypt=True; MultipleActiveResultSets=True; Connection Timeout=30; Command Timeout=60;"
  },
  "AppUrl": "https://new-domain.com",
  "AllowedHosts": "new-domain.com",
  "OwnerPassword__KeyVaultOrEnvVar": "NEW_OWNER_PASSWORD",
  "AgentSettings": {
    "ApiKey": "NEW_API_KEY"
  }
}
```

Also update `appsettings.json`:
```json
{
  "AllowedHosts": "new-domain.com",
  "AppUrl": "https://new-domain.com"
}
```

### Step 4: Update Print Agent Config

In `BookShopPrintAgent/appsettings.json`:

```json
{
  "ServerSettings": {
    "BaseUrl": "https://new-domain.com",
    "ApiKey": "NEW_API_KEY",
    "OwnerPassword": "NEW_OWNER_PASSWORD"
  }
}
```

### Step 5: Deploy the Application

**Option A — MSDeploy** (like current setup):
1. Set environment variable: `$env:DEPLOY_PASSWORD = "NewDeployPassword"`
2. Run: `deploy.bat` or `deploy.ps1`

**Option B — Manual Publish**:
```
dotnet publish -c Release -o ./publish
```
Then copy all files from `./publish/` to the new server's web root (e.g., `wwwroot` folder in IIS).

### Step 6: Update Database Connection in Code

The design-time factory (`Data/DesignTimeDbContextFactory.cs`) has a default fallback:
```
Server=localhost;Database=PrintingBooksPortal;Trusted_Connection=True;TrustServerCertificate=True;
```
Update this if you plan to run EF Core commands against the new database:
```
dotnet ef database update --connection "NEW_CONNECTION_STRING"
```

### Step 7: Verify Deployment

1. Navigate to `https://new-domain.com`
2. Log in with admin credentials (`admin@printingbooks.com` / `Admin@123`)
3. Verify books, shops, and boards are visible
4. Test PDF viewing and printing
5. Update DNS to point your domain to the new server

### Step 8: Update WPF Desktop UI (if needed)

The WPF launcher (`BookShopPortalUI`) hardcodes the agent to `http://localhost:8080`. If the agent URL changes, update `BookShopPortalUI` source code and rebuild.

---

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Development` or `Production` |
| `DEPLOY_PASSWORD` | MSDeploy password for deployment scripts |
| `OWNER_PASSWORD` | Fallback PDF owner password (env var override) |

---

## Deployment Scripts

| Script | Command | Purpose |
|--------|---------|---------|
| `deploy.bat` | `deploy.bat` | Simple MSDeploy via publish profile |
| `deploy.ps1` | `.\deploy.ps1` | Full pipeline: clean → restore → build → publish → MSDeploy |

Both require `DEPLOY_PASSWORD` environment variable to be set.

---

## NuGet Dependencies (Main Project)

| Package | Version |
|---------|---------|
| iText7 | 9.7.0 |
| iText.BouncyCastleAdapter | 9.7.0 |
| PdfSharpCore | 1.3.67 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.9 |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.9 |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.9 |
| Microsoft.EntityFrameworkCore.Design | 10.0.9 |

---

## Quick Commands

```bash
# Run locally
dotnet run

# Add a migration
dotnet ef migrations add MigrationName

# Apply migrations to a specific database
dotnet ef database update --connection "CONNECTION_STRING"

# Publish for production
dotnet publish -c Release -o ./publish

# Deploy to RunASP.NET
$env:DEPLOY_PASSWORD = "YOUR_PASSWORD"
.\deploy.ps1
```
