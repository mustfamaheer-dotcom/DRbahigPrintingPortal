#!/usr/bin/env bash
# ============================================================================
# PHASE 2 - attach domain + HTTPS (run as the LAST hosting step)
# Prereqs: 1) phase-1 (setup-server.sh) already deployed on the VPS
#          2) config.env has DOMAIN set, and the domain's A record points
#             to this server's public IP (DNS propagation done)
#
# This script:
#   1. Issues a Let's Encrypt certificate (webroot method)
#   2. Swaps the HTTP nginx config for the HTTPS one
#   3. Flips the app back to HTTPS-only security
#   4. Reloads nginx + restarts the app
#
# Run: sudo bash enable-https.sh
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="$SCRIPT_DIR/config.env"

if [[ ! -f "$CONFIG" ]]; then
    echo "ERROR: $CONFIG not found."
    exit 1
fi

# shellcheck disable=SC1090
source "$CONFIG"

if [[ -z "${DOMAIN:-}" ]]; then
    echo "ERROR: DOMAIN is empty in config.env. Set your domain and point its A record at this server first."
    exit 1
fi

echo "==> [1/5] Check DNS + issue certificate"
if ! command -v certbot > /dev/null; then
    apt-get update -y
    apt-get install -y certbot
fi

certbot certonly --webroot -w /var/www/certbot -d "$DOMAIN" --non-interactive --agree-tos --register-unsafely-without-email

echo "==> [2/5] Install HTTPS nginx config"
sed "s/DOMAIN/$DOMAIN/g" "$SCRIPT_DIR/nginx-booksportal.conf" > /etc/nginx/sites-available/booksportal.conf
nginx -t && systemctl reload nginx

echo "==> [3/5] Flip app security back to HTTPS-only"
sed -i 's/"RequireHttps": false/"RequireHttps": true/' /opt/booksportal/appsettings.Production.json
sed -i 's/"CookieSecurePolicy": "SameAsRequest"/"CookieSecurePolicy": "Always"/' /opt/booksportal/appsettings.Production.json

echo "==> [4/5] Update AppUrl + AllowedHosts in the app config"
sed -i "s|http://SERVER_IP|https://$DOMAIN|g" /opt/booksportal/appsettings.Production.json
sed -i "s/SERVER_IP/$DOMAIN/g" /opt/booksportal/appsettings.Production.json

echo "==> [5/5] Restart the app"
systemctl restart booksportal

echo ""
echo "============================================================================"
echo " Domain attached: https://$DOMAIN"
echo " Verify: curl -k https://$DOMAIN ; login and test a print job."
echo " Remaining: repoint shop agents to https://$DOMAIN (see AGENT-CUTOVER.md)."
echo "============================================================================"
