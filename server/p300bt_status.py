#!/usr/bin/env python3
"""Query PT-P300BT printer status over Bluetooth and output JSON.

Run with the PT-P300BT venv:
    ~/PT-P300BT/venv/bin/python ~/p300bt_status.py
"""

import json
import os
import serial
import sys
import time

RFCOMM_DEVICE = "/dev/rfcomm0"
TIMEOUT = 5  # seconds

# Brother status request command: ESC i S
STATUS_REQUEST = b"\x1biS"

# Lookup tables from Brother protocol documentation
TAPE_WIDTHS = {
    0: None, 4: 3.5, 6: 6, 9: 9, 12: 12, 18: 18, 24: 24, 36: 36,
}

TAPE_COLORS = {
    0x01: "white", 0x02: "other", 0x03: "clear", 0x04: "red",
    0x05: "blue", 0x06: "yellow", 0x07: "green", 0x08: "black",
    0x09: "clear_white", 0x20: "matte_white", 0x21: "matte_clear",
    0x22: "matte_silver", 0x23: "satin_gold", 0x24: "satin_silver",
    0x30: "blue_d", 0x31: "red_d", 0x40: "fluorescent_orange",
    0x41: "fluorescent_yellow", 0x50: "berry_pink", 0x51: "light_gray",
    0x52: "lime_green", 0x60: "yellow_f", 0x61: "pink_f",
    0x62: "blue_f", 0x70: "white_heat_shrink", 0x90: "white_flex",
    0x91: "yellow_flex", 0xF0: "cleaning", 0xF1: "stencil",
    0xFF: "incompatible",
}

TEXT_COLORS = {
    0x01: "white", 0x04: "red", 0x05: "blue", 0x08: "black",
    0x0A: "gold", 0x62: "blue_f", 0xF1: "stencil",
    0x02: "other",
}

MEDIA_TYPES = {
    0x00: "none", 0x01: "laminated", 0x03: "non-laminated",
    0x11: "heat-shrink", 0x17: "incompatible",
}

BATTERY_LEVELS = {
    0x00: "full", 0x01: "half", 0x02: "low", 0x03: "critical",
    0x04: "change_batteries",
}

ERROR_FLAGS_1 = {
    0: "no_media", 1: "end_of_media", 2: "cutter_jam", 3: "unused",
    4: "printer_in_use", 5: "printer_off", 6: "high_voltage",
    7: "fan_motor_error",
}

ERROR_FLAGS_2 = {
    0: "replace_media", 1: "expansion_buffer_full", 2: "communication_error",
    3: "buffer_full", 4: "cover_open", 5: "cancel_key", 6: "media_not_detected",
    7: "system_error",
}


def query_status():
    """Send status request to printer and parse the 32-byte response."""
    try:
        ser = serial.Serial(RFCOMM_DEVICE, timeout=TIMEOUT)
    except (serial.SerialException, OSError) as e:
        return {"online": False, "errors": [f"Cannot open {RFCOMM_DEVICE}: {e}"]}

    try:
        ser.write(STATUS_REQUEST)
        time.sleep(0.5)
        data = ser.read(32)
    except (serial.SerialException, OSError) as e:
        return {"online": False, "errors": [f"Communication error: {e}"]}
    finally:
        ser.close()

    if len(data) < 32:
        return {"online": False, "errors": [f"Short response: {len(data)} bytes"]}

    # Validate header
    if data[0] != 0x80 or data[1] != 0x20:
        return {"online": False, "errors": ["Invalid response header"]}

    # Parse fields
    errors = []
    err1 = data[8]
    err2 = data[9]
    for bit, name in ERROR_FLAGS_1.items():
        if err1 & (1 << bit):
            errors.append(name)
    for bit, name in ERROR_FLAGS_2.items():
        if err2 & (1 << bit):
            errors.append(name)

    tape_width_raw = data[10]
    tape_width = TAPE_WIDTHS.get(tape_width_raw, tape_width_raw)

    media_type_raw = data[11]
    media_type = MEDIA_TYPES.get(media_type_raw, f"unknown_{media_type_raw:#x}")

    tape_color_raw = data[24] if len(data) > 24 else 0
    tape_color = TAPE_COLORS.get(tape_color_raw, f"unknown_{tape_color_raw:#x}")

    text_color_raw = data[25] if len(data) > 25 else 0
    text_color = TEXT_COLORS.get(text_color_raw, f"unknown_{text_color_raw:#x}")

    battery_raw = data[6]
    battery = BATTERY_LEVELS.get(battery_raw, f"unknown_{battery_raw:#x}")

    return {
        "online": True,
        "tape_width_mm": tape_width,
        "tape_color": tape_color,
        "text_color": text_color,
        "media_type": media_type,
        "battery": battery,
        "errors": errors,
    }


if __name__ == "__main__":
    status = query_status()
    print(json.dumps(status))
    sys.exit(0 if status.get("online") else 1)
