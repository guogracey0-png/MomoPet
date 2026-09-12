"""Persistent Wind market bridge for MomoPet.

One WindPy session polls the two configured indices and atomically publishes a
small JSON snapshot.  The pet and the MCP server both read that snapshot, so
they never create competing Wind sessions.
"""

from __future__ import annotations

import json
import math
import os
import signal
import sys
import time
from datetime import datetime, time as clock_time
from pathlib import Path

from wind_loader import load_wind


CODES = ["000001.SH", "000680.SH"]
NAMES = {"000001.SH": "上证综指", "000680.SH": "科创综指"}
FIELDS = ["rt_last", "rt_pct_chg", "rt_open", "rt_high", "rt_low", "rt_amt"]
DATA_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "MomoPet"
SNAPSHOT = DATA_DIR / "market.json"
PID_FILE = DATA_DIR / "wind-monitor.pid"
RUNNING = True


def stop(*_args):
    global RUNNING
    RUNNING = False


def clean(value):
    try:
        number = float(value)
        return number if math.isfinite(number) else None
    except (TypeError, ValueError):
        return None


def phase(now: datetime, is_trading_day=True) -> str:
    current = now.time()
    if not is_trading_day or now.weekday() >= 5:
        return "closed"
    if current < clock_time(9, 15):
        return "preopen"
    if current < clock_time(11, 30):
        return "trading"
    if current < clock_time(13, 0):
        return "lunch"
    if current < clock_time(15, 0):
        return "trading"
    return "closed"


def publish(payload: dict):
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    temporary = SNAPSHOT.with_suffix(".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    os.replace(temporary, SNAPSHOT)


def error_snapshot(message: str, code=None):
    now = datetime.now().astimezone()
    publish({
        "schema": 1,
        "source": "Wind",
        "status": "error",
        "error": message,
        "error_code": code,
        "timestamp": now.isoformat(timespec="seconds"),
        "trading_date": now.date().isoformat(),
        "phase": phase(now),
        "is_trading_day": None,
        "indices": [],
    })


def map_quote(quote, now: datetime, is_trading_day: bool) -> dict:
    field_names = [str(field).lower() for field in quote.Fields]
    indices = []
    for code_index, code in enumerate(quote.Codes):
        values = {}
        for field_index, field in enumerate(field_names):
            values[field] = clean(quote.Data[field_index][code_index])
        indices.append({
            "code": str(code),
            "name": NAMES.get(str(code), str(code)),
            "last": values.get("rt_last"),
            "pct_change": values.get("rt_pct_chg"),
            "open": values.get("rt_open"),
            "high": values.get("rt_high"),
            "low": values.get("rt_low"),
            "amount": values.get("rt_amt"),
        })
    return {
        "schema": 1,
        "source": "Wind",
        "status": "ok",
        "timestamp": now.isoformat(timespec="seconds"),
        "trading_date": now.date().isoformat(),
        "phase": phase(now, is_trading_day),
        "is_trading_day": is_trading_day,
        "close_snapshot": is_trading_day and now.time() >= clock_time(15, 0),
        "indices": indices,
    }


def sleep_seconds(now: datetime, is_trading_day=True) -> float:
    current = now.time()
    if now.weekday() < 5 and clock_time(14, 59, 40) <= current <= clock_time(15, 0, 20):
        return 1.0
    if phase(now, is_trading_day) == "trading":
        return 10.0
    if phase(now, is_trading_day) == "lunch":
        return 30.0
    return 60.0


def main() -> int:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    PID_FILE.write_text(str(os.getpid()), encoding="ascii")
    signal.signal(signal.SIGTERM, stop)
    if hasattr(signal, "SIGINT"):
        signal.signal(signal.SIGINT, stop)
    wind = None
    last_error = None
    calendar_date = None
    is_trading_day = False
    try:
        while RUNNING:
            now = datetime.now().astimezone()
            try:
                if wind is None or not wind.isconnected():
                    wind = load_wind()
                    started = wind.start(waitTime=8, showmenu=False)
                    if int(started.ErrorCode) != 0 or not wind.isconnected():
                        raise RuntimeError("Wind 终端未登录或量化接口未授权 (start %s)" % started.ErrorCode)
                if calendar_date != now.date():
                    days = wind.tdays(now.date().isoformat(), now.date().isoformat(), "")
                    if int(days.ErrorCode) != 0:
                        raise RuntimeError("Wind 交易日历请求失败 (%s)" % days.ErrorCode)
                    is_trading_day = bool(days.Data and days.Data[0])
                    calendar_date = now.date()
                quote = wind.wsq(",".join(CODES), ",".join(FIELDS))
                if int(quote.ErrorCode) != 0:
                    raise RuntimeError("Wind 行情请求失败 (%s)" % quote.ErrorCode)
                publish(map_quote(quote, now, is_trading_day))
                last_error = None
            except Exception as exc:
                message = "%s: %s" % (type(exc).__name__, exc)
                if message != last_error:
                    error_snapshot(message)
                    last_error = message
                wind = None
            deadline = time.monotonic() + sleep_seconds(now, is_trading_day)
            while RUNNING and time.monotonic() < deadline:
                time.sleep(min(.5, deadline - time.monotonic()))
    finally:
        try:
            if wind is not None and wind.isconnected():
                wind.stop()
        except Exception:
            pass
        try:
            PID_FILE.unlink()
        except OSError:
            pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
