#!/usr/bin/env bash
# setup-pi.sh — One-shot setup for VisioCall on Raspberry Pi 3 (32-bit armhf)
# Assumes WireGuard VPN is already active (wg0 interface with IP 10.200.15.1)
# Run as root: sudo bash setup-pi.sh
set -euo pipefail

WG_IP="10.200.15.1"
APP_USER="visiocall"
APP_DIR="/opt/visiocall"

echo "=== VisioCall Pi 3 Setup (WireGuard) ==="

# Verify WireGuard is up
if ! ip addr show wg0 &>/dev/null; then
    echo "ERROR: wg0 interface not found. WireGuard must be active."
    exit 1
fi
echo "WireGuard OK — wg0 detected"

# --- 1. Install coturn ---
echo "[1/4] Installing coturn..."
apt-get update
apt-get install -y coturn

# --- 2. coturn (TURN server) ---
echo "[2/4] Configuring coturn..."
cp coturn.conf /etc/turnserver.conf
sed -i 's/^#TURNSERVER_ENABLED=1$/TURNSERVER_ENABLED=1/' /etc/default/coturn 2>/dev/null || \
    echo "TURNSERVER_ENABLED=1" > /etc/default/coturn
systemctl enable coturn
systemctl restart coturn

# --- 3. App user & directory ---
echo "[3/4] Creating app user and directory..."
id "$APP_USER" &>/dev/null || useradd --system --no-create-home --shell /usr/sbin/nologin "$APP_USER"
mkdir -p "$APP_DIR"
if [ -d publish ]; then
    cp -r publish/* "$APP_DIR/"
fi
cp appsettings.Production.json "$APP_DIR/appsettings.Production.json"
chown -R "$APP_USER":"$APP_USER" "$APP_DIR"

# --- 4. systemd service ---
echo "[4/4] Installing systemd service..."
cp visiocall.service /etc/systemd/system/visiocall.service
systemctl daemon-reload
systemctl enable visiocall
systemctl start visiocall

echo ""
echo "=== Setup complete ==="
echo ""
echo "All traffic goes through the WireGuard tunnel ($WG_IP)."
echo "No port forwarding needed on the router."
echo ""
echo "Verify:"
echo "  systemctl status visiocall"
echo "  systemctl status coturn"
echo "  curl http://$WG_IP:5000"
