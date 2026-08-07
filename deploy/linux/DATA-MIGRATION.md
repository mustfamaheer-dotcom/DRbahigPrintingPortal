# Data Migration — from RunASP (db59750) to the new VPS SQL Server

Goal: copy all production data (accounts, roles, shops, books records, assignments,
print history, settings) from `db59750.databaseasp.net` into `PrintingBooksPortal`
on the new VPS, while keeping Identity password hashes intact.

## What gets copied
Tables (schema is created automatically by EF MigrateAsync on first start;
you only copy ROWS, e.g. via SQL Server "Generate Scripts"):
- AspNetUsers, AspNetRoles, AspNetUserRoles, AspNetUserClaims, AspNetUserLogins, AspNetUserTokens, AspNetRoleClaims
- EducationalBoards, Shops, Books, ShopBookAssignments, PrintLogs, SystemSettings

## Recommended: PowerShell + OLE/DataMigration (no third-party tools)

Do everything from a Windows machine with SqlServer module:

```powershell
# 1) Install module (one time)
Install-Module -Name SqlServer -Scope CurrentUser -Force

# 2) Read rows from old DB, write flat files
$old = "Server=db59750.databaseasp.net;Database=db59750;User Id=db59750;Password=6Bc?o@T9_2fA;Encrypt=False"
$new = "Server=127.0.0.1,1433;Database=PrintingBooksPortal;User Id=sa;Password=YOUR_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"

$tables = @(
  'AspNetRoles','AspNetUsers','AspNetUserRoles','AspNetUserClaims',
  'AspNetUserLogins','AspNetUserTokens','AspNetRoleClaims',
  'EducationalBoards','Shops','Books','ShopBookAssignments','PrintLogs','SystemSettings'
)
```

> **Simpler, no-code option:** install **SSMS 19+** locally. Connect to old DB,
> right-click `db59750` → Tasks → Generate Scripts → select all objects →
> Advanced → "Types of data to script" = **Data only** → save `.sql`.
> Then connect SSMS to the VPS instance, run the script on `PrintingBooksPortal`.
> Identity tables keep their hashes — users keep their passwords.

## Connecting to the VPS SQL Server securely (SSH tunnel)

SQL Server on the VPS is bound to `127.0.0.1` only and port 1433 is blocked at the
firewall — this is intentional. To manage it from your PC, tunnel through SSH:

```powershell
# From any terminal on your PC (keep this window open while working):
ssh -i "$env:USERPROFILE\.ssh\opencode_deploy" -L 1433:127.0.0.1:1433 root@186.240.151.209

# Then in SSMS / Azure Data Studio connect to server:
#   Server name:   127.0.0.1  (the tunnel forwards to the VPS)
#   SQL auth:      booksportal_app / <APP_DB_PASSWORD>   (normal work)
#   or             sa / <SA_PASSWORD>                    (server admin)
```

## Order of operations (must match this sequence)

1. Run `setup-server.sh` on the VPS (creates DB + login + empty schema).
2. Start the app once so EF creates all tables:
   `sudo systemctl start booksportal` then optionally `sudo journalctl -u booksportal -f`.
3. Import the data-only script into `PrintingBooksPortal`.
4. Verify row counts (`db-create.sql` has the check queries).
5. Upload book PDFs (see BOOKS-MIGRATION.md).

## What NOT to copy
- The old `.sqlite` local file.
- `AspNetUsers` `SecurityStamp` etc. — everything string → just copy raw rows,
  Identity hashes are portable.

## Danger signs
- Login fails for existing users → password hash was mangled (don't re-run seeder
  after import; the seeder only creates the default admin if the table is empty,
  so it will not touch your copied users).
- EF startup migrations fail → normally expected, the app logs a warning and
  continues; schema is created on first start before data import.