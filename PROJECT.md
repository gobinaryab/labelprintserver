# Label Print Server — Project Documentation

## Overview

This project turns a Raspberry Pi 4 (`murenprintserver`) into a network print server for two Brother label printers. A Flask HTTP API allows any device on the network to trigger label prints. The next phase is a Windows taskbar application for quick label printing.

---

## Hardware Setup

| Device | Model | Connection |
|--------|-------|------------|
| Print server | Raspberry Pi 4 (2GB+), Pi OS Lite | — |
| Label printer | Brother PT-P750W | USB |
| Label printer | Brother PT-P300BT | Bluetooth (RFCOMM) |

---

## Software Stack

### PT-P750W (USB) — ptouch-print

- **Tool:** [ptouch-print](https://git.familie-radermacher.ch/linux/ptouch-print.git) (built from source)
- **Build dependencies:**
  ```bash
  sudo apt install -y cmake build-essential libusb-1.0-0-dev libgd-dev pkg-config gettext
  ```
  > `gettext` is easy to miss — the build will fail without it.
- **USB access:** udev rule for Brother vendor ID `04f9`, user added to `lp` group
- **Usage:**
  ```bash
  ptouch-print --text "Label text"
  ptouch-print --image file.png
  ```

### PT-P300BT (Bluetooth) — Ircama/PT-P300BT

- **Tool:** [Ircama/PT-P300BT](https://github.com/Ircama/PT-P300BT)
- **Venv:** `~/PT-P300BT/venv`
- **Bluetooth setup:**
  ```bash
  bluetoothctl pair <MAC>
  bluetoothctl trust <MAC>
  sudo rfcomm bind rfcomm0 <MAC>
  ```
- **Persistence:** systemd service `rfcomm-ptp300bt.service`
- **Usage (important — positional args, not flags):**
  ```bash
  python printlabel.py /dev/rfcomm0 DejaVuSans "Hello from Pi BT"
  #                     ^COM_PORT    ^FONT_NAME  ^TEXT
  ```
  > The font name is a **required positional argument**. Omitting it causes a "Null image generated" error because the text gets interpreted as the font name.

### Flask API + Mobile Webapp

- **Port:** 8080
- **WSGI server:** gunicorn (2 sync workers) — the Flask dev server was replaced because it logs a "not for production use" warning and isn't suitable for unattended deployment.
- **Service:** `label-printserver.service` (systemd, hardened with `ProtectSystem=strict`, `ProtectHome=read-only`, `NoNewPrivileges`)
- **Venv:** `~/printserver-env/` — must contain `flask` and `gunicorn` (`pip install flask gunicorn`)
- **Script:** `~/print_server.py`
- **Webapp files:** `~/webapp/templates/`, `~/webapp/static/` (served by Flask at `/` and `/static/`)
- **Mobile UI:** `http://murenprintserver:8080/` — iOS users add to Home Screen for a standalone app experience. Shows printer status dots, textarea for label text, printer selector, Print button.

---

## Key Files on the Pi

| Path | Purpose |
|------|---------|
| `~/ptouch-print/` | ptouch-print source |
| `~/PT-P300BT/` | Python Bluetooth printer scripts |
| `~/print_server.py` | Flask API + webapp routes |
| `~/webapp/` | Mobile webapp templates and static assets |
| `~/printserver-env/` | Flask + gunicorn virtualenv |
| `/etc/systemd/system/rfcomm-ptp300bt.service` | Bluetooth RFCOMM persistence |
| `/etc/systemd/system/label-printserver.service` | Flask API service |
| `/etc/udev/rules.d/99-ptouch.rules` | USB printer access rule |

---

## Network Access

The server has **no application-layer authentication** — access control is enforced at the network layer. It is reachable from:

- The home LAN (trusted devices on the local Wi-Fi / Ethernet).
- The Tailscale overlay network (for remote access from phones on mobile data, etc.).

All other inbound traffic is blocked by `ufw`. One-time setup on the Pi:

```bash
# Replace 192.168.0.0/16 with your actual home subnet — check with: ip -4 addr
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow in on eth0 to any port 22 proto tcp
sudo ufw allow in on wlan0 to any port 22 proto tcp
sudo ufw allow from 192.168.0.0/16 to any port 8080 proto tcp
sudo ufw allow in on tailscale0
sudo ufw enable
sudo ufw status verbose
```

If the server is ever exposed to the public internet, add TLS (via Caddy or similar) and a real auth layer — the current setup assumes a trusted network.

## Deploying the Webapp Files

When updating the Pi:

```bash
# From the repo on your workstation:
scp server/print_server.py kalle@murenprintserver:~/
scp -r server/webapp kalle@murenprintserver:~/
scp server/label-printserver.service kalle@murenprintserver:/tmp/
ssh kalle@murenprintserver 'sudo mv /tmp/label-printserver.service /etc/systemd/system/ && sudo systemctl daemon-reload && sudo systemctl restart label-printserver'
```

First-time install additionally requires:

```bash
source ~/printserver-env/bin/activate
pip install gunicorn
deactivate
```

## Troubleshooting

### cmake fails during ptouch-print build
Install the missing `gettext` package:
```bash
sudo apt install -y gettext
```

### bluetoothctl "org.bluez.Error.NotReady"
```bash
sudo rfkill unblock bluetooth
sudo systemctl restart bluetooth
# Then inside bluetoothctl:
power on
```
May also require:
```bash
sudo apt install -y pi-bluetooth bluez-firmware
sudo reboot
```

### "Null image generated" from printlabel.py
You forgot the font name argument. The syntax is:
```
python printlabel.py COM_PORT FONT_NAME TEXT...
```
Not `python printlabel.py /dev/rfcomm0 "some text"`.

---

## Development Roadmap

### Phase 1 — Fix & Stabilize (current)
- [x] Raspberry Pi setup with both printers working
- [x] Flask API scaffold
- [x] Replace Flask dev server with gunicorn (production WSGI)
- [x] Restrict network access to LAN + Tailscale via ufw
- [x] Mobile-first webapp at `/` with iOS Add-to-Home-Screen support
- [ ] Fix Flask API to pass font name to `printlabel.py` (e.g., `DejaVuSans`)
- [ ] Verify both API endpoints work end-to-end

### Phase 2 — Windows System Tray Application

A minimal system tray app for instant label printing.

**Tech stack:**
- C# / WPF on .NET 8+
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) for system tray
- `HttpClient` for API calls
- Published as a single self-contained `.exe`

**Core behavior:**
- Lives in the Windows system tray as a small icon
- **Left-click** the icon → opens a minimal quick-print dialog (text field + Print button). Dialog closes automatically after printing or cancelling
- **Right-click** the icon → context menu to select default printer (PT-P750W / PT-P300BT), configure server address, and exit
- **Tray icon color** indicates print server status:
  - **Green** — server is reachable
  - **Red** — server is unreachable
  - Polled via `GET /api/health` every **10 seconds**

**Server configuration:**
- First-run setup dialog prompts for server address (e.g., `murenprintserver:8080`)
- Saved to a local config file, editable later via right-click menu

**Configuration & data storage:**
- All config stored in `%APPDATA%\LabelPrintClient\` (e.g., `settings.json`)
- Follows Windows conventions — no files left in Program Files or beside the exe
- Config includes: server address, default printer, window position, etc.

**Versioning & distribution:**
- Semantic versioning (e.g., `1.0.0`) set in the `.csproj`
- Version displayed in the right-click context menu (About / tooltip)
- No auto-update — new versions installed manually by re-running the installer
- Installer built with [Inno Setup](https://jrsoftware.org/isinfo.php) — lightweight, single `.exe` installer
  - Installs to `%LOCALAPPDATA%\Programs\LabelPrintClient\`
  - Start Menu shortcut
  - **Run at Windows startup enabled by default** (Registry entry, removable via right-click menu)
  - Uninstaller with config cleanup option
- GitHub Releases used for distributing new versions

**UX behavior:**
- Escape / X button on the print dialog → closes and discards any text (no confirmation)
- Print errors → shown as Windows toast notifications from the tray icon

**Tasks:**
- [ ] Scaffold .NET 8 WPF project with Hardcodet.NotifyIcon.Wpf
- [ ] First-run setup dialog for server address (persist to config)
- [ ] System tray icon with left-click and right-click handlers
- [ ] Quick-print dialog: text input, Print button, auto-close on completion
- [ ] Right-click context menu: printer selection, server settings, startup toggle, exit
- [ ] Health-check polling (10s interval) with green/red icon switching
- [ ] Toast notifications for print errors and server status
- [ ] Store all config in `%APPDATA%\LabelPrintClient\`
- [ ] App versioning in `.csproj`, display version in UI
- [ ] Run-at-startup registry entry (enabled by default, toggleable)
- [ ] Single-file self-contained publish configuration
- [ ] Inno Setup installer script (shortcuts, startup entry, uninstaller)
- [ ] Document release process (build → publish → package → GitHub Release)

### Phase 3 — Future Enhancements
- [ ] Label templates (asset tags, cable labels, storage bins, etc.)
- [ ] QR code / barcode generation in the webapp
- [ ] Print history and logging
- [ ] Docker-compose the Pi services
- [ ] TLS via Caddy reverse proxy (if ever exposed beyond LAN + Tailscale)
