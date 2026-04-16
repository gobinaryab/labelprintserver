#!/usr/bin/env python3
"""Label Print Server — Flask API for Brother label printers on Raspberry Pi."""

import json
import logging
import logging.handlers
import os
import re
import subprocess
import threading
import time

from flask import Flask, jsonify, request

app = Flask(__name__)

# ---------------------------------------------------------------------------
# Logging — console + rotating file
# ---------------------------------------------------------------------------
LOG_DIR = os.path.expanduser("~/logs")
os.makedirs(LOG_DIR, exist_ok=True)
LOG_FILE = os.path.join(LOG_DIR, "print_server.log")

log_format = logging.Formatter("%(asctime)s %(levelname)s %(message)s")

console_handler = logging.StreamHandler()
console_handler.setFormatter(log_format)

file_handler = logging.handlers.RotatingFileHandler(
    LOG_FILE, maxBytes=5 * 1024 * 1024, backupCount=3  # 5 MB, keep 3 old files
)
file_handler.setFormatter(log_format)

logging.basicConfig(level=logging.INFO, handlers=[console_handler, file_handler])
log = logging.getLogger(__name__)

# Paths — adjust if your setup differs
PTOUCH_PRINT = os.path.expanduser("~/ptouch-print/build/ptouch-print")
P300BT_VENV_PYTHON = os.path.expanduser("~/PT-P300BT/venv/bin/python")
P300BT_PRINTLABEL = os.path.expanduser("~/PT-P300BT/printlabel.py")
P300BT_STATUS_SCRIPT = os.path.expanduser("~/p300bt_status.py")
P300BT_FONT = "DejaVuSans"
RFCOMM_DEVICE = "/dev/rfcomm0"

# Locks to serialize access per printer
lock_p750w = threading.Lock()
lock_p300bt = threading.Lock()

# Cache for P300BT status (Bluetooth queries are slow)
_p300bt_status_cache = None
_p300bt_status_time = 0
P300BT_CACHE_TTL = 15  # seconds


# ---------------------------------------------------------------------------
# Health check
# ---------------------------------------------------------------------------

def _p750w_connected():
    """Quick check: is a Brother USB device present?"""
    try:
        result = subprocess.run(
            ["lsusb"], capture_output=True, text=True, timeout=5
        )
        return "04f9" in result.stdout.lower()
    except Exception:
        return False


def _p300bt_connected():
    """Quick check: is the RFCOMM device bound?"""
    return os.path.exists(RFCOMM_DEVICE)


@app.route("/api/health")
def health():
    p750w = "ok" if _p750w_connected() else "offline"
    p300bt = "ok" if _p300bt_connected() else "offline"
    log.debug("Health check: p750w=%s p300bt=%s", p750w, p300bt)
    return jsonify(status="ok", p750w=p750w, p300bt=p300bt)


# ---------------------------------------------------------------------------
# Printer status
# ---------------------------------------------------------------------------

def _get_p750w_status():
    """Query PT-P750W status via ptouch-print --info."""
    log.info("Querying P750W status")
    if not _p750w_connected():
        log.warning("P750W status: USB device not found")
        return {"online": False, "errors": ["USB device not found"]}

    with lock_p750w:
        try:
            cmd = [PTOUCH_PRINT, "--info"]
            log.info("Running: %s", " ".join(cmd))
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=10)
            log.info("P750W --info exit=%d stdout=%r stderr=%r",
                     result.returncode, result.stdout.strip(), result.stderr.strip())
        except subprocess.TimeoutExpired:
            log.error("P750W status: timed out")
            return {"online": False, "errors": ["Status query timed out"]}
        except FileNotFoundError:
            log.error("P750W status: ptouch-print not found at %s", PTOUCH_PRINT)
            return {"online": False, "errors": [f"ptouch-print not found at {PTOUCH_PRINT}"]}

    output = result.stdout + result.stderr
    if result.returncode != 0 and not output.strip():
        return {"online": False, "errors": [f"ptouch-print exited with code {result.returncode}"]}

    # Parse ptouch-print --info output
    # Typical output lines:
    #   PT-P750W found on USB bus ...
    #   Media: 12 mm tape
    #   Tape color: white, text color: black
    status = {
        "online": True,
        "tape_width_mm": None,
        "tape_color": None,
        "text_color": None,
        "media_type": None,
        "errors": [],
        "raw_output": output.strip(),
    }

    for line in output.splitlines():
        line_lower = line.strip().lower()

        # Tape width: look for "NN mm"
        if "mm" in line_lower and ("media" in line_lower or "tape" in line_lower or "width" in line_lower):
            m = re.search(r"(\d+)\s*mm", line_lower)
            if m:
                status["tape_width_mm"] = int(m.group(1))

        # Tape/text color
        if "tape color" in line_lower or "tape colour" in line_lower:
            m = re.search(r"tape colou?r[:\s]+(\w+)", line_lower)
            if m:
                status["tape_color"] = m.group(1)

        if "text color" in line_lower or "text colour" in line_lower:
            m = re.search(r"text colou?r[:\s]+(\w+)", line_lower)
            if m:
                status["text_color"] = m.group(1)

        # Media type
        if "media" in line_lower and ("type" in line_lower or "laminated" in line_lower
                                       or "tape" in line_lower):
            if "laminated" in line_lower:
                status["media_type"] = "non-laminated" if "non" in line_lower else "laminated"
            elif "heat" in line_lower:
                status["media_type"] = "heat-shrink"
            elif "fabric" in line_lower:
                status["media_type"] = "fabric"
            elif "tape" in line_lower:
                status["media_type"] = "tape"

    return status


def _get_p300bt_status():
    """Query PT-P300BT status via helper script in BT venv."""
    global _p300bt_status_cache, _p300bt_status_time

    # Return cached result if fresh
    if _p300bt_status_cache and (time.time() - _p300bt_status_time) < P300BT_CACHE_TTL:
        log.debug("P300BT status: returning cached result")
        return _p300bt_status_cache

    log.info("Querying P300BT status")
    if not _p300bt_connected():
        log.warning("P300BT status: RFCOMM device not found")
        return {"online": False, "errors": ["RFCOMM device not found"]}

    with lock_p300bt:
        try:
            cmd = [P300BT_VENV_PYTHON, P300BT_STATUS_SCRIPT]
            log.info("Running: %s", " ".join(cmd))
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=15)
            log.info("P300BT status exit=%d stdout=%r stderr=%r",
                     result.returncode, result.stdout.strip(), result.stderr.strip())
        except subprocess.TimeoutExpired:
            log.error("P300BT status: timed out")
            return {"online": False, "errors": ["Bluetooth status query timed out"]}
        except FileNotFoundError:
            log.error("P300BT status: script or venv not found")
            return {"online": False, "errors": ["Status script or venv not found"]}

    if result.returncode != 0:
        return {"online": False, "errors": [result.stderr.strip() or f"Exit code {result.returncode}"]}

    try:
        status = json.loads(result.stdout)
        _p300bt_status_cache = status
        _p300bt_status_time = time.time()
        log.info("P300BT status: %s", status)
        return status
    except json.JSONDecodeError:
        log.error("P300BT status: invalid JSON: %s", result.stdout[:200])
        return {"online": False, "errors": [f"Invalid JSON: {result.stdout[:200]}"]}


@app.route("/api/status/<printer>")
def printer_status(printer):
    if printer == "p750w":
        return jsonify(_get_p750w_status())
    elif printer == "p300bt":
        return jsonify(_get_p300bt_status())
    else:
        return jsonify(error=f"Unknown printer: {printer}"), 404


# ---------------------------------------------------------------------------
# Print
# ---------------------------------------------------------------------------

@app.route("/api/print/p750w", methods=["POST"])
def print_p750w():
    data = request.get_json(silent=True) or {}
    text = data.get("text", "").strip()
    if not text:
        return jsonify(error="No text provided"), 400

    log.info("P750W print request: %r", text)
    with lock_p750w:
        try:
            cmd = [PTOUCH_PRINT, "--text", text]
            log.info("Running: %s", " ".join(cmd))
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            log.info("P750W print exit=%d stdout=%r stderr=%r",
                     result.returncode, result.stdout.strip(), result.stderr.strip())
        except subprocess.TimeoutExpired:
            log.error("P750W print timed out for: %r", text)
            return jsonify(error="Print timed out"), 504
        except FileNotFoundError:
            log.error("P750W: ptouch-print not found at %s", PTOUCH_PRINT)
            return jsonify(error=f"ptouch-print not found at {PTOUCH_PRINT}"), 500

    if result.returncode != 0:
        error_msg = result.stderr.strip() or result.stdout.strip() or f"Exit code {result.returncode}"
        log.error("P750W print failed: %s", error_msg)
        return jsonify(error=error_msg), 500

    log.info("P750W printed OK: %r", text)
    return jsonify(success=True)


@app.route("/api/print/p300bt", methods=["POST"])
def print_p300bt():
    data = request.get_json(silent=True) or {}
    text = data.get("text", "").strip()
    if not text:
        return jsonify(error="No text provided"), 400

    log.info("P300BT print request: %r", text)
    with lock_p300bt:
        try:
            cmd = [P300BT_VENV_PYTHON, P300BT_PRINTLABEL, RFCOMM_DEVICE, P300BT_FONT, text]
            log.info("Running: %s", " ".join(cmd))
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            log.info("P300BT print exit=%d stdout=%r stderr=%r",
                     result.returncode, result.stdout.strip(), result.stderr.strip())
        except subprocess.TimeoutExpired:
            log.error("P300BT print timed out for: %r", text)
            return jsonify(error="Print timed out"), 504
        except FileNotFoundError:
            log.error("P300BT: venv or printlabel.py not found")
            return jsonify(error="PT-P300BT venv or printlabel.py not found"), 500

    if result.returncode != 0:
        error_msg = result.stderr.strip() or result.stdout.strip() or f"Exit code {result.returncode}"
        log.error("P300BT print failed: %s", error_msg)
        return jsonify(error=error_msg), 500

    log.info("P300BT printed OK: %r", text)
    return jsonify(success=True)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    log.info("Starting Label Print Server on port 8080")
    app.run(host="0.0.0.0", port=8080)
