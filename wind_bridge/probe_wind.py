import json
from wind_loader import load_wind

result = {"imported": False, "connected": False}
try:
    w = load_wind()
    result["imported"] = True
    started = w.start(waitTime=8, showmenu=False)
    result["start_error"] = int(started.ErrorCode)
    result["start_message"] = str(getattr(started, "Data", ""))
    result["connected"] = bool(w.isconnected())
    if result["connected"]:
        quote = w.wsq("000001.SH,000680.SH", "rt_last,rt_pct_chg,rt_open,rt_high,rt_low,rt_amt")
        result["quote_error"] = int(quote.ErrorCode)
        result["codes"] = list(quote.Codes)
        result["fields"] = list(quote.Fields)
        result["data"] = quote.Data
        w.stop()
except Exception as exc:
    result["exception"] = type(exc).__name__ + ": " + str(exc)

print(json.dumps(result, ensure_ascii=False, default=str))
