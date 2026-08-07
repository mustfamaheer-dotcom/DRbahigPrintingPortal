# Book PDF File Migration — from old server to new VPS

The app stores book PDFs as files under `App_Data/Books/` (NOT in the database).
Database rows only store the GUID filename (`Book.FileName`). So after restoring
the DB you must also copy the actual PDF files, or book downloads/printing will fail.

## Source locations

Old host (RunASP.NET, Windows/IIS shared hosting):
- `D:\home\site\wwwroot\App_Data\Books\*.pdf` (accessible via RunASP file manager / FTP)

New VPS:
- `/opt/booksportal/App_Data/Books/` (must exist; created on first run by
  `FileStorageService`)

## Steps on your Windows machine

1. Download all PDFs from the old server:
   - RunASP control panel → File Manager → navigate to `App_Data/Books`,
     or connect via FTP with the FTP credentials from your RunASP hosting.
   - Download the whole `Books` folder.

2. Make sure the folder exists on the server:
   ```bash
   sudo mkdir -p /opt/booksportal/App_Data/Books
   sudo chown -R booksportal:booksportal /opt/booksportal/App_Data
   ```

3. Upload: `scp -r Books/*.pdf root@VPS_IP:/opt/booksportal/App_Data/Books/`

## Verify
- Row count `Books` in DB == number of `.pdf` files in `App_Data/Books`.
  (Not necessarily 1:1 — some rows may reference deleted files — but if the
  count is wildly different, something was left behind.)

## When doing this during migration
Do the DB import FIRST (DATA-MIGRATION.md), then copy files. If you copy files
first, nothing breaks — the folder ordering does not matter, only that both
exist before the site goes live to agents.

## After cutover
- Old host keeps its data; you may shut down the RunASP site once the new server
  verifies, to avoid agents pointing at a stale instance.