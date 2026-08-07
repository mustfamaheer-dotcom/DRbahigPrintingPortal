# Agent Cutover — point client machines to the new server

The installed agent on each bookshop PC reads `ServerSettings:BaseUrl` from its
local config file. When the new VPS goes live you must repoint every agent.

Config file on each client PC:
`C:\Program Files\BookShopPrintAgent\appsettings.json`

## Two-phase URL
- **Phase 1 (no domain yet):** `"BaseUrl": "http://SERVER_IP"`
- **Phase 2 (after enable-https.sh):** `"BaseUrl": "https://YOUR_DOMAIN"`

You can repoint agents during phase 1 and keep them on the IP; just remember to
switch to `https://YOUR_DOMAIN` once phase 2 is done.

## Option A — rebuild the installer (preferred, zero manual edits)

In `BookShopPrintAgent/appsettings.json` change:

    "BaseUrl": "https://drbaheegbook.runasp.net"  →  "BaseUrl": "https://YOUR_DOMAIN"

Rebuild the self-contained installer (`SetupBootstrapper`) and re-install on each
shop PC. The api key stays the same (`tKwXJ5L...`) — no per-client edits.

## Option B — edit in place (fast, for one machine)

1. On the client: open `C:\Program Files\BookShopPrintAgent\appsettings.json`
   (as admin, e.g. Notepad with admin rights).
2. Set `BaseUrl` to the new domain; keep `ApiKey` unchanged.
3. Save, then restart the agent:
   ```
   schtasks /end /tn BookShopPrintAgent
   schtasks /run /tn BookShopPrintAgent
   ```
4. Verify the tray icon and that the dashboard shows the shop online.

## Cutover checklist
- [ ] Phase 1: `curl http://SERVER_IP` responds
- [ ] Phase 2 (last step): HTTPS certificate installed (`https://DOMAIN`, no browser warning)
- [ ] DB imported + PDFs uploaded
- [ ] Admin login tested on the live URL
- [ ] All shop agents repointed (BaseUrl = IP in phase 1, domain in phase 2)
- [ ] Old RunASP site kept standing one day for rollback; shut down after verification