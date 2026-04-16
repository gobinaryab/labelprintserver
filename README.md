# Label Print Server

A Raspberry Pi-based network print server for Brother label printers, exposing a Flask HTTP API for easy label printing from any device on the network.

## Hardware

- **Raspberry Pi 4** (2GB+), running Raspberry Pi OS Lite
  - Hostname: `murenprintserver`
  - User: `kalle`
- **Brother PT-P750W** — connected via USB
- **Brother PT-P300BT** — connected via Bluetooth (RFCOMM)

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/print/p750w` | Print via PT-P750W (USB) |
| `POST` | `/api/print/p300bt` | Print via PT-P300BT (Bluetooth) |
| `GET`  | `/api/health` | Health check |

The API runs on port **8080**.

## Quick Start

```bash
# Print on the USB printer
curl -X POST http://murenprintserver:8080/api/print/p750w \
  -H "Content-Type: application/json" \
  -d '{"text": "Hello World"}'

# Print on the Bluetooth printer
curl -X POST http://murenprintserver:8080/api/print/p300bt \
  -H "Content-Type: application/json" \
  -d '{"text": "Hello from BT"}'
```

## Project Structure

See [PROJECT.md](PROJECT.md) for full setup instructions, troubleshooting, and development plans.
