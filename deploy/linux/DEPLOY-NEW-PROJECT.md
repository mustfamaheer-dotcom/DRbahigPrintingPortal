# Deploying Another Project on the VPS

This guide covers deploying any web application alongside the existing
PrintingBooksPortal on the same Hostinger VPS.

> **VPS:** `186.240.151.209` · Ubuntu 24.04 LTS · 2 vCPU / 8 GB RAM / 100 GB disk
> **SSH key:** `~/.ssh/opencode_deploy`
> **Firewall:** `booksportal-prod` (id 341934) — allow 22/80/443, block everything else

---

## 1. Prerequisites

- SSH access to the VPS
- A domain name (recommended) or use the IP directly
- Your project files (published output or source code)
- The VPS has ~6 GB RAM free after the current portal + SQL Server

---

## 2. Connect to the VPS

```powershell
ssh -i "$env:USERPROFILE\.ssh\opencode_deploy" root@186.240.151.209
```

---

## 3. Create a dedicated user (recommended)

Each project should run as its own user for security isolation.

```bash
# Replace 'myproject' with your project name
useradd -r -m -d /opt/myproject -s /usr/sbin/nologin myproject
```

---

## 4. Upload and install the project

### Option A: Self-contained .NET app (like PrintingBooksPortal)

```bash
# From your local machine (Windows PowerShell):
scp -i "$env:USERPROFILE\.ssh\opencode_deploy" -r publish/ root@186.240.151.209:/opt/myproject/

# On the VPS:
chown -R myproject:myproject /opt/myproject
chmod +x /opt/myproject/YourAppName  # Linux binary needs exec permission
```

### Option B: Node.js / Python / PHP app

Upload your source code, then install dependencies:

```bash
# Node.js
cd /opt/myproject && npm install && npm run build

# Python
cd /opt/myproject && pip install -r requirements.txt

# PHP (just copy files to nginx web root)
cp -r /path/to/source/* /var/www/myproject/
```

---

## 5. Create a systemd service

Create `/etc/systemd/system/myproject.service`:

```ini
[Unit]
Description=My Project (ASP.NET Core / Node / etc.)
After=network.target

[Service]
WorkingDirectory=/opt/myproject
ExecStart=/opt/myproject/YourAppName --urls http://127.0.0.1:5001
Restart=always
RestartSec=10
SyslogIdentifier=myproject
User=myproject
Group=myproject
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

[Install]
WantedBy=multi-user.target
```

**Notes:**
- Change `--urls http://127.0.0.1:5001` to a different port than 5000 (port 5000 is used by PrintingBooksPortal)
- For Node.js: `ExecStart=/usr/bin/node /opt/myproject/server.js`
- For Python: `ExecStart=/usr/bin/python3 /opt/myproject/app.py`

Enable and start:

```bash
systemctl daemon-reload
systemctl enable myproject
systemctl start myproject
systemctl status myproject
```

---

## 6. Configure Nginx

Create `/etc/nginx/sites-available/myproject`:

```nginx
server {
    listen 80;
    server_name myproject.com;  # or use _ for IP-only, or subdomain

    client_max_body_size 200m;

    location / {
        proxy_pass http://127.0.0.1:5001;  # must match --urls port
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}
```

Enable and test:

```bash
ln -s /etc/nginx/sites-available/myproject /etc/nginx/sites-enabled/
nginx -t
systemctl reload nginx
```

---

## 7. Open firewall port (if needed)

The Hostinger firewall `booksportal-prod` already allows port 80 and 443.
No action needed unless your app uses a custom port.

If you need a new firewall rule, use the Hostinger MCP API or hPanel.

---

## 8. Set up SSL (recommended)

```bash
# Install certbot (one time)
apt-get install -y certbot python3-certbot-nginx

# Get certificate (replace domain)
certbot --nginx -d myproject.com -d www.myproject.com

# Auto-renewal is set up automatically. Verify:
certbot renew --dry-run
```

**Note:** Let's Encrypt requires a real domain. You cannot get a cert for a bare IP.

---

## 9. Verify

```bash
# Check service
systemctl status myproject

# Check logs
journalctl -u myproject -f

# Test locally
curl -s -o /dev/null -w "HTTP %{http_code}\n" http://127.0.0.1:5001/

# Test via nginx
curl -s -o /dev/null -w "HTTP %{http_code}\n" http://myproject.com/
```

---

## Quick Reference: Port Allocation

| Port | Project |
|------|---------|
| 5000 | PrintingBooksPortal (current) |
| 5001 | Next project |
| 5002 | Another project |
| 1433 | SQL Server (localhost only) |
| 80 | Nginx (HTTP) |
| 443 | Nginx (HTTPS) |
| 22 | SSH |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `502 Bad Gateway` | App not running — check `systemctl status myproject` |
| `403 Forbidden` | Wrong file permissions — check `chown` and `chmod` |
| `Connection refused` | Port mismatch — verify `--urls` matches nginx `proxy_pass` |
| App crashes on start | Check logs: `journalctl -u myproject -n 50` |
| Nginx won't reload | Run `nginx -t` to see the syntax error |

---

## Security Checklist

- [ ] Each project runs as its own user (not root)
- [ ] SQL Server stays on localhost (127.0.0.1) — never expose 1433
- [ ] SSL enabled for production domains
- [ ] Firewall allows only 22/80/443
- [ ] App secrets in environment variables or `appsettings.Production.json` (not in code)
- [ ] Regular backups: `mysqldump` for databases, file copies for uploads
