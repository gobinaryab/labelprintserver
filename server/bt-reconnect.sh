#!/bin/bash
# Bluetooth reconnect watchdog for PT-P300BT
# Run periodically via systemd timer to ensure RFCOMM stays bound.
# Discovers the printer by name — no hardcoded MAC needed.
# Only logs when there's something actionable — silent when device is asleep.

set -euo pipefail

BT_NAME_PATTERN="PT-P300BT"
RFCOMM_DEV="/dev/rfcomm0"
RFCOMM_CHANNEL=1
LOG_TAG="bt-reconnect"

log() { logger -t "$LOG_TAG" "$*"; echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

# Check if RFCOMM is bound AND the channel is actually open (not "closed")
if [ -e "$RFCOMM_DEV" ]; then
    RFCOMM_STATE=$(rfcomm show rfcomm0 2>/dev/null || true)
    if echo "$RFCOMM_STATE" | grep -q "connected"; then
        # Genuinely connected — nothing to do
        exit 0
    fi
    # Device file exists but channel is closed/stale — needs rebinding
fi

# Make sure bluetooth is up
if rfkill list bluetooth | grep -q "Soft blocked: yes"; then
    log "Bluetooth soft-blocked, unblocking"
    rfkill unblock bluetooth
    sleep 2
fi
bluetoothctl power on 2>/dev/null || true
sleep 1

# Find the MAC by searching paired/known devices for the name pattern
BT_MAC=$(bluetoothctl devices 2>/dev/null \
    | grep -i "$BT_NAME_PATTERN" \
    | head -1 \
    | awk '{print $2}')

if [ -z "$BT_MAC" ]; then
    # Not even paired — nothing to do
    exit 0
fi

# Try to connect — this wakes the device
bluetoothctl connect "$BT_MAC" 2>/dev/null || true
sleep 2

# Check if device actually responded
BT_INFO=$(bluetoothctl info "$BT_MAC" 2>/dev/null || true)
if ! echo "$BT_INFO" | grep -q "Connected: yes"; then
    # Device is asleep or out of range — stay silent
    exit 0
fi

# Device is connected! Now rebind RFCOMM.
log "PT-P300BT ($BT_MAC) is online — rebinding RFCOMM"
bluetoothctl trust "$BT_MAC" 2>/dev/null || true
rfcomm release rfcomm0 2>/dev/null || true
rfcomm bind rfcomm0 "$BT_MAC" "$RFCOMM_CHANNEL"

if [ -e "$RFCOMM_DEV" ]; then
    log "Reconnect successful — $RFCOMM_DEV is back"
else
    log "Reconnect FAILED — bind succeeded but $RFCOMM_DEV missing"
    exit 1
fi
