# DR. Bahig Books Portal — VPS Deployment Runbook

Complete, ready-to-run package for deploying the current (single-tenant) system
on a fresh Ubuntu 22.04/24.04 VPS with SQL Server 2022. **This package does not touch
the VPS — everything runs only when you choose to execute it.**

> **Current VPS (deployed):** Ubuntu 24.04 LTS · `root@186.240.151.209` (SSH key `~/.ssh/opencode_deploy`) · VM 1883494 (KVM 2: 2 vCPU / 8 GB / 100 GB). SQL Server is bound to 127.0.0.1 and port 1433 is blocked by the Hostinger firewall (`booksportal-prod`, allow 22/80/443 only) — use the SSH tunnel in DATA-MIGRATION.md to reach it from SSMS.

## Contents of this folder
| File | Purpose |
|---|---|
| `publish/` | dotnet publish output — self-contained linux-x64 (no .NET runtime needed) |
| `setup-server.sh` | one-shot provisioning: SQL Server + tools + app + systemd + nginx |
| `enable-https.sh` | phase 2: attach domain + Let's Encrypt SSL (run LAST) |
| `nginx-booksportal-http.conf` | phase 1 reverse proxy — plain HTTP on the server IP |
| `nginx-booksportal.conf` | phase 2 reverse proxy — HTTPS + SignalR WebSocket upgrade headers |
| `config.env` / `config.env.example` | all variable inputs (SERVER_IP, DOMAIN, SA password, DB password) |
| `appsettings.Production.json` | production app config (real AgentSettings, DB login placeholder) |
| `db-create.sql` | creates `PrintingBooksPortal` DB + `booksportal_app` login + grants |
| `booksportal.service` | systemd unit (restart, DataProtection persistence, dedicated user) |
| `DATA-MIGRATION.md` | how to move rows from db59750 (SSMS data-only script) |
| `BOOKS-MIGRATION.md` | how to copy the PDF files from the old host |
| `AGENT-CUTOVER.md` | how to repoint client shop PCs |

## Two-phase deployment

You can go live on the **public IP over HTTP first** (no domain needed), then
attach the domain + HTTPS **as the very last step**.

- **Phase 1 (IP / HTTP):** set only `SERVER_IP` in `config.env`, leave `DOMAIN`
  empty. The portal is reachable at `http://SERVER_IP`. Security is relaxed for
  HTTP (`RequireHttps=false`, cookies `SameAsRequest`) so login works without SSL.
- **Phase 2 (domain / HTTPS, last step):** set `DOMAIN` in `config.env`, point
  its A record to `SERVER_IP`, then run `sudo bash enable-https.sh`. It issues
  the certificate, swaps the nginx config, flips the app back to HTTPS-only
  security, and restarts everything.

## Flow (overview)
1. Fill `config.env` (SERVER_IP; DOMAIN only for phase 2; SA password; DB password already generated).
2. Upload this folder + `publish/` to the VPS.
3. Run `sudo bash setup-server.sh`.
4. Import DB data (DATA-MIGRATION.md) — after the app has started once.
5. Upload book PDFs (BOOKS-MIGRATION.md).
6. Access the portal at `http://SERVER_IP` (phase 1), or run `enable-https.sh`
   and use `https://DOMAIN` (phase 2 — the last hosting step).
7. Repoint agents (AGENT-CUTOVER.md).

## 1. Fill in config.env
`SERVER_IP` = the VPS public IP (phase 1). `DOMAIN` = public hostname for the
last step — its A record must point at `SERVER_IP`. `SA_PASSWORD` = strong SQL
SA password. `APP_DB_PASSWORD` is already generated and matches the
`appsettings.Production.json` placeholder — keep them equal.

> Never commit `config.env` to Git. `.env`/`config.env` should be git-ignored.

## 2. Upload
```bash
# on the VPS:
mkdir -p /opt/deploy && cd /opt/deploy

# from your Windows machine (adjust USER/IP):
scp -r deploy\linux\* root@VPS_IP:/opt/deploy/
scp -r E:\WORK\FreeLance\ENG Baheeg\BooksPortal\deploy\linux\publish root@VPS_IP:/opt/deploy/publish
```

## 3. Provision (executes ONLY when you run it)
```bash
sudo bash setup-server.sh
```
Installs: SQL Server 2022 (Developer default till you change), nginx, the service,
the app, and nginx site. Prints the next steps.

## 4. Data import
Follow DATA-MIGRATION.md. Schema is auto-created by `MigrateAsync` on first start;
only rows are copied via SSMS "Data only" script from old `db59750`.

## 5. Book files
Follow BOOKS-MIGRATION.md: scp old PDFs into `/opt/booksportal/App_Data/Books/`.

## 6. HTTPS + DNS (phase 2 — the last hosting step)
```bash
# 1) Point an A record for DOMAIN to the VPS public IP (Hostinger DNS)
# 2) Wait for DNS propagation, then:
sudo bash enable-https.sh
```
`enable-https.sh` installs certbot, issues the certificate via webroot, swaps
nginx to the HTTPS config, restores HTTPS-only app security, and restarts the app.

## 7. Verify
- Phase 1: `curl http://SERVER_IP` → HTML
- Phase 2: `curl -k https://DOMAIN` → HTML
- `sudo systemctl status booksportal` → running
- `/hubs/print` upgrades (WebSocket) — watch browser network tab
- Login with an admin account; open Dashboard; print a test

## 8. Agent cutover
See AGENT-CUTOVER.md. Phase 1: BaseUrl = `http://SERVER_IP`. Phase 2:
BaseUrl = `https://DOMAIN`. The api key is unchanged in both phases.

## Rollback
The old RunASP site stays live until verification is done. To roll back: stop
the systemd service and keep DNS pointing at runasp — data imported via
migration is read-only after import, nothing touches the old host.

## Operational commands
```bash
sudo systemctl status booksportal        # status
sudo systemctl restart booksportal       # restart
sudo journalctl -u booksportal -f        # logs
/opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1 -U sa -P "..." -C -d PrintingBooksPortal
```