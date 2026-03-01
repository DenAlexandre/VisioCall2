#!/usr/bin/env bash
# publish-server.sh — Build the SignalR server for Raspberry Pi 3 (linux-arm) and copy via SSH
set -euo pipefail

PI_HOST="${1:-pi@raspberrypi.local}"
PI_DIR="/opt/visiocall"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PUBLISH_DIR="$SCRIPT_DIR/publish"

echo "=== Publishing VisioCall.Server for linux-arm ==="

# Build
dotnet publish "$REPO_ROOT/Visio/VisioCall.Server" \
    -r linux-arm \
    --self-contained true \
    -c Release \
    -o "$PUBLISH_DIR"

echo "Published to $PUBLISH_DIR"

# Copy to Pi
echo "Copying to $PI_HOST:$PI_DIR ..."
ssh "$PI_HOST" "sudo mkdir -p $PI_DIR && sudo chown \$(whoami) $PI_DIR"
scp -r "$PUBLISH_DIR/"* "$PI_HOST:$PI_DIR/"
scp "$SCRIPT_DIR/appsettings.Production.json" "$PI_HOST:$PI_DIR/appsettings.Production.json"
ssh "$PI_HOST" "sudo chown -R visiocall:visiocall $PI_DIR && sudo systemctl restart visiocall"

echo "=== Deploy complete ==="
echo "Check status: ssh $PI_HOST 'sudo systemctl status visiocall'"
