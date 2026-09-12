"""Load the locally installed WindPy without modifying the Wind installation."""

from __future__ import annotations

import builtins
import io
import os
import sys
from pathlib import Path


WIND_DIR = Path(r"C:\Wind\Wind.NET.Client\WindNET\x64")
LOCAL_SITE_PACKAGES = Path(__file__).resolve().parent / "site-packages"


def load_wind():
    if hasattr(os, "add_dll_directory"):
        os.add_dll_directory(str(WIND_DIR))
    for path in (str(LOCAL_SITE_PACKAGES), str(WIND_DIR)):
        if path not in sys.path:
            sys.path.insert(0, path)

    original_open = builtins.open

    def open_without_pth_newline(file, *args, **kwargs):
        try:
            if os.fspath(file).lower().endswith("windpy.pth"):
                return io.StringIO(str(WIND_DIR))
        except TypeError:
            pass
        return original_open(file, *args, **kwargs)

    builtins.open = open_without_pth_newline
    try:
        from WindPy import w
    finally:
        builtins.open = original_open
    return w
