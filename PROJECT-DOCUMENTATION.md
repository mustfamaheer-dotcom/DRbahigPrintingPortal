# DR. Bahig Books Portal — Project Documentation

Complete technical documentation for the book printing management system:
web portal, print agent, and client installer.

> **Security note:** this document contains no credentials. All secrets live in
> `appsettings.Production.json`, `deploy/linux/config.env`, and the agent's
> `appsettings.json` on each client PC.

---

## Table of Contents

1. [Overview](#1-overview)
2. [System Architecture](#2-system-architecture)
3. [Repository Layout](#3-repository-layout)
4. [Tech Stack](#4-tech-stack)
5. [Data Model](#5-data-model)
6. [Authentication & Security](#6-authentication--security)
7. [Print Job Flow (End to End)](#7-print-job-flow-end-to-end)
8. [API Reference](#8-api-reference)
9. [SignalR](#9-signalr)
10. [Configuration](#10-configuration)
11. [PDF Processing Pipeline](#11-pdf-processing-pipeline)
12. [Print Agent Details](#12-print-agent-details)
13. [Client Installer](#13-client-installer)
14. [Deployment](#14-deployment)
15. [Operations](#15-operations)
16. [Future: SaaS Roadmap](#16-future-saas-roadmap)

---

## 1. Overview

A multi-component system that lets a central administrator manage an educational
book catalog, assign books to bookshop branches, and securely print watermarked
PDF copies at each shop on demand.

**Three deployable components:**

| Component | Project | Runs on | Purpose |
|---|---|---|---|
| **Web Portal** | `PrintingBooksPortal` | Server (IIS / Linux+nginx) | Catalog, users, shops, assignments, analytics, print-job creation |
| **Print Agent** | `BookShopPrintAgent` | Each bookshop PC (Windows) | Polls server, downloads encrypted PDFs, prints via SumatraPDF |
| **Installer** | `SetupBootstrapper` | Bookshop PC setup | Installs agent + SumatraPDF + scheduled task on the shop machine |

---

## 2. System Architecture

```
┌─────────────────────────────┐        ┌──────────────────────────────┐
│  Web Portal (Blazor Server) │        │  Print Agent (each shop PC)  │
│  HTTPS on server            │◄──────►│  http://localhost:8080       │
│                             │  HTTPS │  Windows service / task      │
│  - Identity (Admin/Shop)    │        │  - Polls pending jobs (3 s)  │
│  - Books, Boards, Shops     │        │  - Claims + downloads PDF    │
│  - Watermark + encrypt PDF  │        │  - Decrypts w/ owner pass    │
│  - In-memory job queue      │        │  - Resizes + prints via      │
│  - SignalR hub /hubs/print  │        │    SumatraPDF (noscale)      │
└──────────────┬──────────────┘        └──────────────────────────────┘
               │
       ┌───────┴────────┐        ┌──────────────────────────┐
       │ SQL Server     │        │  App_Data/Books/*.pdf    │
       │ (prod)         │        │  (raw book files)        │
       │ SQLite (dev)   │        │  SecurePrints/*.pdf      │
       └────────────────┘        │  (encrypted, ephemeral)  │
                                 └──────────────────────────┘
```

**Key design decision:** print files are never left in the clear. The portal
applies a shop/user watermark, encrypts with a per-job user password + a shared
owner password (AES-128), and hands the job to the agent only via a time-limited
in-memory queue (5-minute expiry).

---

## 3. Repository Layout

```
BooksPortal/
├── PrintingBooksPortal/          # Web portal (main app)
│   ├── Components/
│   │   ├── Layout/               # MainLayout, NavMenu (sidebar)
│   │   └── Pages/
│   │       ├── Admin/            # Dashboard, Books, Shops, Boards,
│   │       │                     # Assignments, Users, Settings, Analytics
│   │       ├── Shop/             # MyBooks, Viewer, PrintHistory
│   │       └── Public/           # Login, Logout, AccessDenied
│   ├── Controllers/              # LoginController, AdminController,
│   │                             # AnalyticsController, SecurePdfController
│   ├── Data/                     # AppDbContext, DbSeeder, migrations
│   ├── Hubs/                     # PrintHub (SignalR)
│   ├── Models/                   # EF entities + PendingJobInfo etc.
│   ├── Services/                 # Watermark, PDF security, storage,
│   │                             # print tokens, logging, settings
│   └── wwwroot/
│       ├── docs/                 # static HTML docs site
│       └── (static assets)
├── BookShopPrintAgent/           # Per-shop print agent (Windows)
│   ├── Controllers/PrintJobController.cs
│   └── Services/PdfPrintService.cs
├── SetupBootstrapper/            # Client installer (self-contained EXE)
├── deploy/linux/                 # VPS deployment package (see §14)
├── SAAS-PLAN.md                  # Future multi-tenant roadmap (see §16)
├── PROJECT-DOCUMENTATION.md      # This file
```

---

## 4. Tech Stack

| Layer | Technology |
|---|---|
| Framework | .NET 10 / ASP.NET Core (Blazor Server, interactive server render mode) |
| UI | Bootstrap 5 + custom CSS, `MapStaticAssets` |
| ORM | Entity Framework Core 10.0.9 |
| Database (prod) | SQL Server (currently `db59750.databaseasp.net`, migrating to VPS SQL Server 2022) |
| Database (dev) | SQLite (`PrintingBooksPortal.db`) |
| Auth | ASP.NET Core Identity (Admin / Shop roles) |
| Real-time | SignalR hub at `/hubs/print` |
| PDF | iText7 9.7.0 (encrypt, watermark), PdfSharpCore 1.3.67 |
| Printing (agent) | SumatraPDF 3.6.1 x64 command-line |
| Hosting (current) | RunASP.NET IIS shared hosting |
| Hosting (target) | Hostinger VPS — Ubuntu 22.04 + nginx + systemd (package ready) |

---

## 5. Data Model

**Custom tables** (EF-managed):

| Table | Purpose |
|---|---|
| `Shops` | Bookshop branches (name, address, contact, active flag) |
| `EducationalBoards` | Curricula (Cambridge IGCSE, Edexcel, IB, National) |
| `Books` | PDF books (title, board FK, file path, page count, active flag) |
| `ShopBookAssignments` | Many-to-many shop ↔ book with active flag |
| `PrintLogs` | Audit of every print (shop, book, copies, user, timestamp) |
| `SystemSettings` | Key/value settings — watermark enabled/text |
| `ApplicationUser` (extends `AspNetUsers`) | Identity user + `ShopId` + `FullName` |

**Identity tables:** `AspNetRoles`, `AspNetRoleClaims`, `AspNetUserClaims`,
`AspNetUserLogins`, `AspNetUserRoles`, `AspNetUserTokens`.

**Relationships:**

```
Shop (1) ──< ShopBookAssignment >── (1) Book
Shop (1) ──< PrintLog >── (1) Book
EducationalBoard (1) ──< Book
Shop (1) ──< ApplicationUser (AspNetUsers)
```

**Migrations** (3 applied in production):
1. `20260713225749_InitialCreate`
2. `20260714203452_AddSystemSettings`
3. `20260714203453_AddValueStringToSystemSettings`

**Startup behavior (`Program.cs`):** production runs `MigrateAsync()` (warnings
on failure are expected/logged), then `DbSeeder` — idempotent, creates Admin/Shop
roles, default admin (`admin@printingbooks.com`), and 4 boards only if missing.

---

## 6. Authentication & Security

- **Roles:** `Admin` (full management) and `Shop` (view assigned books, print).
- **Cookies:** HttpOnly, `SameSite=Strict`, `Secure=Always`, 8-hour sliding expiry.
- **Antiforgery** enabled globally.
- **HTTPS:** enforced with `UseHttpsRedirection`, HSTS in production.
- **Agent auth:** `X-Api-Key` header compared against `AgentSettings:ApiKey`.
- **PDF encryption:** AES-128 via iText7 — per-job random user password +
  shared owner password (`OwnerPassword__KeyVaultOrEnvVar` / `OWNER_PASSWORD` env).
- **Watermarking:** diagonal text per page (shop name, user, timestamp),
  toggleable via SystemSettings.
- **Print tokens:** 5-minute, single-use tokens for direct printing.
- **Job ownership:** in-memory `PendingPrintJobs` maps job → ShopId; shop users
  may only download/print their own jobs; Admin may access all.
- **File uploads:** only `.pdf` allowed (extension check), stored under
  `App_Data/Books/` as GUID filenames.
- **Fail-closed PDF serving:** any watermarking error returns 500 — the raw
  unwatermarked file is never served.

---

## 7. Print Job Flow (End to End)

```
Shop user clicks "Print" in portal
        │
        ▼
POST /api/pdf/process-print   (Shop role)
  1. Validates ShopBookAssignment (access check)
  2. Loads original PDF from App_Data/Books/
  3. Applies watermark (shop + user + timestamp)
  4. Encrypts with AES-128 (user pass = PRINT-{jobId}, owner pass shared)
  5. Writes encrypted file to SecurePrints/{jobId}.pdf
  6. Adds {jobId} → job info to in-memory queue (5-min TTL)
  7. Logs to PrintLogs
        │
        ▼
Agent on shop PC (loop every 3 s)
  1. GET  /api/pdf/print-agent/pending          (X-Api-Key)
  2. POST /api/pdf/print-agent/claim/{jobId}    (removes from queue)
  3. GET  /api/pdf/download-secured/{jobId}     (X-Api-Key; encrypted PDF)
  4. Decrypts with owner password
  5. Re-lays pages onto target paper size (scale/fit/shrink/custom,
     margins, duplex options) via iText
  6. Prints via SumatraPDF "-print-to <printer> -print-settings noscale... -silent"
  7. On failure → POST /api/pdf/print-agent/release/{jobId} → job retried
```

**Fallback paths:**
- `GET /api/pdf/print/{bookId}?token=...` — direct browser print with a
  5-minute token (no agent needed).
- `GET /api/pdf/view-secure/{bookId}` — watermarked PDF as base64 in-browser.
- `GET /api/pdf/print-file/{jobId}` — one-time download, deletes the file.

---

## 8. API Reference

All routes under the web portal (base URL depends on environment).

### Authentication
| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/login` | Anonymous | Form login (anti-forgery exempted) |
| POST | `/api/logout` | Authenticated | Logout |

### Admin / Management (`AdminController`)
| Method | Route | Purpose |
|---|---|---|
| POST | `/api/admin/register-user` | Create shop/admin users |
| (various) | `/api/admin/...` | Shops, books, boards, assignments CRUD |

> Pages under `/admin/*` are Blazor Server components; most mutations happen
> through the interactive UI rather than REST.

### Analytics (`AnalyticsController`)
| Method | Route | Purpose |
|---|---|---|
| GET | `/api/analytics/print-summary` | Per-shop, daily, weekly, recent stats |
| GET | `/api/analytics/print-trends` | 30-day print trends |

### Secure PDF (`SecurePdfController`)
| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/api/pdf/view-secure/{bookId}` | Shop, Admin | Watermarked PDF (base64) |
| POST | `/api/pdf/process-print` | Shop | Create encrypted print job |
| GET | `/api/pdf/print-file/{jobId}` | Shop, Admin | One-time print file download (deletes file) |
| GET | `/api/pdf/download-secured/{jobId}` | Agent key or owner | Agent downloads encrypted PDF |
| GET | `/api/pdf/print/{bookId}?token=` | Token | Direct print (token or auth) |
| GET | `/api/pdf/print-token/{bookId}` | Shop | Generate 5-min print token |
| GET | `/api/pdf/print-agent/pending` | Agent key | List pending job IDs |
| GET | `/api/pdf/print-agent/debug` | Authenticated | Queue debug info |
| POST | `/api/pdf/print-agent/claim/{jobId}` | Agent key | Claim job (removes from queue) |
| POST | `/api/pdf/print-agent/release/{jobId}` | Agent key | Return job to queue (retry) |

### Agent local API (`BookShopPrintAgent`, port 8080)
| Method | Route | Purpose |
|---|---|---|
| GET | `/api/print-job/printers` | Physical printer list (WMI, excludes virtual printers) |
| POST | `/api/print-job` | Submit print job with settings (used by local UI) |
| GET | `/api/print-job/health` | Health check |

---

## 9. SignalR

- Hub: `/hubs/print` — `[Authorize(Roles = "Shop")]`.
- Client method: `PrintRequested` (logged server-side).
- The agent itself uses **HTTP polling** (3 s) — SignalR is for portal↔browser
  live updates. Both paths must work through the reverse proxy
  (WebSocket upgrade headers — see `deploy/linux/nginx-booksportal.conf`).

---

## 10. Configuration

| File | Environment | Purpose |
|---|---|---|
| `appsettings.json` | all | Base config, placeholder connection string |
| `appsettings.Development.json` | Development | SQLite connection, local URL |
| `appsettings.Production.json` | Production | SQL Server connection, real `AppUrl`, `AllowedHosts`, agent API key, owner password |
| `Properties/launchSettings.json` | dev | Dev port (5035) |
| `BookShopPrintAgent/appsettings.json` | agent | `ServerSettings:BaseUrl`, `ApiKey`, `OwnerPassword`, `UseSignalR`; `PrinterSettings` |

### Key production settings
```json
{
  "AppUrl": "https://<domain>",
  "OwnerPassword__KeyVaultOrEnvVar": "<owner-password>",
  "ConnectionStrings:DefaultConnection": "Server=...;Database=...;User Id=...;Password=...;",
  "AllowedHosts": "<domain>",
  "AgentSettings:ApiKey": "<shared-agent-key>"
}
```

The same agent API key and owner password must match in
`BookShopPrintAgent/appsettings.json` on every shop PC.

### Dev quick start
```bash
dotnet run                                    # portal → http://localhost:5035
dotnet run --project BookShopPrintAgent       # agent  → http://localhost:8080
dotnet ef migrations add <Name>               # new migration
dotnet ef database update                     # apply migrations
```

---

## 11. PDF Processing Pipeline

| Stage | Where | Tech |
|---|---|---|
| Upload | Portal | `FileStorageService` → `App_Data/Books/{guid}.pdf` (PDF-only check) |
| View | Portal | `WatermarkService` (iText7) — diagonal watermark per page, base64 response |
| Print job | Portal | Watermark → `PdfSecurityService.EncryptPdfWithPassword` (AES-128, user + owner passwords) → `SecurePrints/{jobId}.pdf` |
| Decrypt | Agent | iText7 `ReaderProperties.SetPassword(ownerPassword)` |
| Re-layout | Agent | iText7 — target paper (A4/Letter/A3/A5/JIS B4/B5…), scale modes (`actual`/`fit`/`shrink`/`custom`), margins in mm/cm/inch, duplex |
| Print | Agent | SumatraPDF 3.6.1: `-print-to "<printer>" -print-settings "noscale[,duplexvertical|duplexhorizontal]" -silent` |

**Orientation/size correctness relies on the agent pre-sizing pages** — the
portal passes raw page sizes; the agent re-maps them onto the target paper so
`noscale` printing matches the shop's request exactly.

---

## 12. Print Agent Details

- Windows-only, ASP.NET Core Kestrel on `http://localhost:8080` (loopback only).
- **Single instance** via named mutex `BookShopPrintAgent`.
- Frees port 8080 on startup (kills any stale owning process).
- File logging to `logs/agent_yyyyMMdd.log` next to the executable (survives
  single-file publish via `Environment.ProcessPath`).
- **Physical-printer filter:** WMI `Win32_Printer` minus virtual printers
  (OneNote, XPS, Fax, PDF writers, etc.) by name/driver/port heuristics;
  connection types USB / Network / WiFi / Bluetooth / LPT / COM.
- Optional `PrinterSettings:DefaultPrinterName` and `Copies` defaults.
- Debug artifacts: pre-print PDF saved to `logs/pre_<jobId>_*.pdf`.
- Installed as a **scheduled task** (`BookShopPrintAgent`, start at logon,
  restart on failure) by the bootstrapper.

---

## 13. Client Installer

`SetupBootstrapper` → `DR_Bahig_Books_Portal_Setup.exe` (~240 MB, win-x64,
**self-contained single-file** — no .NET runtime required on client).

Installs:
- Agent files (app, appsettings template, SumatraPDF 3.6.1 x64) to
  `C:\Program Files\BookShopPrintAgent\`
- Scheduled task `BookShopPrintAgent` (runs at logon, auto-restart)

**Post-install on each client (one-time):**
1. Edit `C:\Program Files\BookShopPrintAgent\appsettings.json` (as admin)
2. Set `ServerSettings:ApiKey` to the real agent key (the installer ships a placeholder)
3. Save, restart task:
   ```
   schtasks /end /tn BookShopPrintAgent
   schtasks /run /tn BookShopPrintAgent
   ```
4. Verify tray/health + a test print.

> Alternative: rebuild the installer with the key baked in
> (`BookShopPrintAgent/appsettings.json` → rebuild `SetupBootstrapper`).

---

## 14. Deployment

### 14.1 Current production (RunASP.NET — IIS shared hosting)

- URL: `https://drbaheegbook.runasp.net`
- Publish profile: `Properties/PublishProfiles/MonsterASP.pubxml` (MSDeploy)
- Deploy:
  ```powershell
  $env:DEPLOY_PASSWORD = "<deploy-password>"
  dotnet publish -c Release -p:PublishProfile=MonsterASP
  ```
- DB: SQL Server at `db59750.databaseasp.net` (connection string in
  `appsettings.Production.json`).
- PDF files: `App_Data/Books/` on the server (NOT in the DB).

### 14.2 Target deployment (Hostinger VPS — self-hosted)

Full ready-to-run package in **`deploy/linux/`**:

| Artifact | Purpose |
|---|---|
| `setup-server.sh` | One-shot provision: SQL Server 2022 + tools, app install, systemd, nginx |
| `config.env` / `config.env.example` | DOMAIN, SA password, generated app DB password |
| `db-create.sql` | Creates `PrintingBooksPortal` DB + `booksportal_app` login (db_owner) |
| `appsettings.Production.json` | VPS config template (DB on 127.0.0.1, same agent key) |
| `booksportal.service` | systemd unit (dedicated user, auto-restart, key persistence) |
| `nginx-booksportal.conf` | HTTPS proxy + SignalR WebSocket upgrade + 200 MB uploads |
| `DATA-MIGRATION.md` | Move rows from `db59750` (SSMS data-only script) |
| `BOOKS-MIGRATION.md` | Copy `App_Data/Books/*.pdf` from old host |
| `AGENT-CUTOVER.md` | Repoint client agents to the new domain |
| `README-DEPLOY.md` | End-to-end runbook |

Flow: fill `config.env` → upload folder + `publish/` → `sudo bash setup-server.sh`
→ start app once (EF creates schema) → import data → upload PDFs → certbot SSL
→ DNS A record → repoint agents.

**Key invariants for cutover:**
- `AppUrl`, `AllowedHosts` → new domain
- `AgentSettings:ApiKey` unchanged (agents keep working after BaseUrl change)
- DB connection string → `127.0.0.1` on the VPS
- Data import is rows-only; Identity password hashes transfer as-is

### 14.3 Building the linux publish

```bash
dotnet publish PrintingBooksPortal/PrintingBooksPortal.csproj \
  -c Release -r linux-x64 --self-contained true -o deploy/linux/publish
```
(Remove the shipped `appsettings.*.json` from the publish output — the setup
script writes the correct production file.)

---

## 15. Operations

| Task | Command |
|---|---|
| Portal status (VPS) | `sudo systemctl status booksportal` |
| Portal restart (VPS) | `sudo systemctl restart booksportal` |
| Portal logs (VPS) | `sudo journalctl -u booksportal -f` |
| SQL shell (VPS) | `/opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1 -U sa -C` |
| Agent logs (client) | `C:\Program Files\BookShopPrintAgent\logs\agent_*.log` |
| Agent restart (client) | `schtasks /end /tn BookShopPrintAgent` + `schtasks /run /tn BookShopPrintAgent` |
| Add migration | `dotnet ef migrations add <Name>` |

**Stateful data on the server:**
- `App_Data/Books/` — raw book PDFs (back up!)
- `SecurePrints/` — encrypted jobs, ephemeral (5-min TTL in queue; files cleaned when downloaded)
- In-memory job queue — lost on restart (jobs expire after 5 min anyway)

**Rollback:** the RunASP site stays live until the VPS is verified; cutting over
is a DNS/config change, fully reversible.

---

## 16. Future: SaaS Roadmap

`SAAS-PLAN.md` contains the full technical specification for turning this
single-tenant system into a multi-tenant SaaS: tenant-scoped EF query filters,
per-tenant API keys, tenant-aware SignalR groups, per-tenant job queues,
migration/backfill SQL, and a feature-flag rollback plan. **Not implemented —
reference only.**
