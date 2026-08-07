#!/usr/bin/env bash
# ============================================================================
# DR. Bahig Books Portal - One-shot server provisioning script
# Ubuntu 22.04/24.04 LTS + SQL Server 2022 + .NET 10 (self-contained) + Nginx + systemd
#
# IMPORTANT: All variables come from config.env in the same folder.
# The publish/ folder (dotnet publish output) must be beside this script.
# Run: sudo bash setup-server.sh
#
# Security: SQL Server is bound to 127.0.0.1 (never exposed). Port 1433 is
# blocked by the Hostinger firewall - manage the DB via SSH tunnel only.
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="$SCRIPT_DIR/config.env"

if [[ ! -f "$CONFIG" ]]; then
    echo "ERROR: $CONFIG not found. Copy config.env.example to config.env and fill it in."
    exit 1
fi

# shellcheck disable=SC1090
source "$CONFIG"

echo "==> [1/7] System update + prerequisites"
apt-get update -y
DEBIAN_FRONTEND=noninteractive apt-get upgrade -y
DEBIAN_FRONTEND=noninteractive apt-get install -y curl wget gnupg2 software-properties-common ca-certificates nginx

echo "==> [2/7] SQL Server 2022 (Microsoft repo)"
if ! dpkg -l | grep -q mssql-server; then
    # SQL Server 2022 officially supports Ubuntu 22.04. On Ubuntu 24.04 the
    # official 22.04 repo is compatible but needs libldap-2.5-0 (not shipped
    # with 24.04). Install it from the Ubuntu 22.04 (jammy) archive first.
    . /etc/os-release
    if [[ "$VERSION_ID" == "24.04" ]]; then
        echo "    Ubuntu 24.04 detected - installing libldap-2.5-0 from jammy archive (SQL Server 2022 dependency)"
        LIBLDAP_DEB="libldap-2.5-0_2.5.20+dfsg-0ubuntu0.22.04.1_amd64.deb"
        wget -q "http://archive.ubuntu.com/ubuntu/pool/main/o/openldap/$LIBLDAP_DEB" -O "/tmp/$LIBLDAP_DEB"
        dpkg -i "/tmp/$LIBLDAP_DEB" || apt-get install -f -y
    fi

    if [[ ! -f /usr/share/keyrings/microsoft-prod.gpg ]]; then
        curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --batch --dearmor -o /usr/share/keyrings/microsoft-prod.gpg
    fi
    # The .list files from packages.microsoft.com do not carry a signed-by=
    # directive, so apt only reads trusted.gpg.d/ — mirror the keyring there.
    cp -f /usr/share/keyrings/microsoft-prod.gpg /etc/apt/trusted.gpg.d/microsoft-prod.gpg
    chmod 644 /etc/apt/trusted.gpg.d/microsoft-prod.gpg
    curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list \
        | tee /etc/apt/sources.list.d/mssql-server-2022.list > /dev/null
    curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/prod.list \
        | tee /etc/apt/sources.list.d/mssql-prod.list > /dev/null
    rm -rf /var/lib/apt/lists/*   # avoid stale/bad headers on 24.04
    apt-get update -y

    MSSQL_SA_PASSWORD="$SA_PASSWORD" ACCEPT_EULA=Y MSSQL_PID="$MSSQL_PID" \
        apt-get install -y mssql-server

    MSSQL_SA_PASSWORD="$SA_PASSWORD" MSSQL_PID="$MSSQL_PID" \
        /opt/mssql/bin/mssql-conf -n setup accept-eula
fi

echo "==> [3/7] SQL Server tools (sqlcmd)"
if ! command -v sqlcmd > /dev/null; then
    ACCEPT_EULA=Y DEBIAN_FRONTEND=noninteractive apt-get install -y mssql-tools18 unixodbc-dev
    echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> /etc/profile.d/mssql-tools.sh
    # shellcheck disable=SC1091
    source /etc/profile.d/mssql-tools.sh
fi
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"

# Wait for SQL Server to accept connections
echo "==> [4/7] Waiting for SQL Server..."
for i in {1..60}; do
    if /opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1 -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; then
        echo "    SQL Server is up."
        break
    fi
    sleep 2
    if [[ $i -eq 60 ]]; then
        echo "ERROR: SQL Server did not start in time."; exit 1
    fi
done

echo "==> [5/7] Create database + login"
if [[ -f "$SCRIPT_DIR/db-create.sql" ]]; then
    # Inject the real generated password into db-create.sql (placeholder -> value)
    sed "s/REPLACE_WITH_GENERATED_DB_PASSWORD/$APP_DB_PASSWORD/g" "$SCRIPT_DIR/db-create.sql" > /tmp/db-create.sql
    /opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1 -U sa -P "$SA_PASSWORD" -C -i /tmp/db-create.sql
else
    echo "WARNING: db-create.sql not found - skipping DB creation."
fi

echo "==> [6/7] Install application"
id -u booksportal > /dev/null 2>&1 || useradd -r -m -d /opt/booksportal -s /usr/sbin/nologin booksportal
mkdir -p /opt/booksportal
cp -r "$SCRIPT_DIR/publish/." /opt/booksportal/
chmod +x /opt/booksportal/PrintingBooksPortal   # exec bit lost in Windows->Linux copy
chown -R booksportal:booksportal /opt/booksportal
chmod -R u+rwX /opt/booksportal

# Production appsettings with real values injected.
# Phase 1 (no domain): serve on the public IP over HTTP; security relaxed
# (RequireHttps=false, cookie SameAsRequest) so login works without SSL.
# enable-https.sh restores HTTPS-only security when the domain is attached.
if [[ -f "$SCRIPT_DIR/appsettings.Production.json" ]]; then
    sed -e "s/SERVER_IP/${SERVER_IP:-}/g" \
        -e "s/REPLACE_WITH_GENERATED_DB_PASSWORD/$APP_DB_PASSWORD/g" \
        "$SCRIPT_DIR/appsettings.Production.json" > /opt/booksportal/appsettings.Production.json
    chown booksportal:booksportal /opt/booksportal/appsettings.Production.json
fi

echo "==> [7/7] systemd service + nginx"
cp "$SCRIPT_DIR/booksportal.service" /etc/systemd/system/booksportal.service
systemctl daemon-reload
systemctl enable --now booksportal

mkdir -p /var/www/certbot
if [[ -z "${DOMAIN:-}" ]]; then
    echo "    No DOMAIN set - serving on IP ${SERVER_IP:-} over HTTP (phase 1)."
    echo "    When the domain + SSL are ready, run: sudo bash enable-https.sh"
    if [[ -f "$SCRIPT_DIR/nginx-booksportal-http.conf" ]]; then
        sed "s/SERVER_IP/${SERVER_IP:-}/g" "$SCRIPT_DIR/nginx-booksportal-http.conf" > /etc/nginx/sites-available/booksportal.conf
        ln -sf /etc/nginx/sites-available/booksportal.conf /etc/nginx/sites-enabled/booksportal.conf
        rm -f /etc/nginx/sites-enabled/default
        nginx -t && systemctl reload nginx
    fi
else
    if [[ -f "$SCRIPT_DIR/nginx-booksportal.conf" ]]; then
        sed "s/DOMAIN/$DOMAIN/g" "$SCRIPT_DIR/nginx-booksportal.conf" > /etc/nginx/sites-available/booksportal.conf
        ln -sf /etc/nginx/sites-available/booksportal.conf /etc/nginx/sites-enabled/booksportal.conf
        rm -f /etc/nginx/sites-enabled/default
        nginx -t && systemctl reload nginx
    fi
fi

echo ""
echo "============================================================================"
echo " Server setup complete."
echo " Next (phase 1, no domain):"
echo "   1) Import data from old db59750 (see DATA-MIGRATION.md - SSMS Generate Scripts)"
echo "   2) Upload book PDFs to /opt/booksportal/App_Data/Books"
echo "   3) Access the portal at: http://SERVER_IP"
echo "   4) Point agents to http://SERVER_IP (ServerSettings:BaseUrl in agent appsettings.json)"
echo " LAST step (domain + SSL):"
echo "   Set DOMAIN in config.env, point its A record to this server, run:"
echo "     sudo bash enable-https.sh"
echo "   then repoint agents to https://DOMAIN (AGENT-CUTOVER.md)."
echo "============================================================================"
