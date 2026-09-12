"""Dependency-free read-only MCP server for MomoPet's Wind snapshot."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path


SNAPSHOT = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "MomoPet" / "market.json"


def read_snapshot():
    if not SNAPSHOT.exists():
        return {"status": "unavailable", "source": "Wind", "error": "行情桥尚未生成快照"}
    try:
        return json.loads(SNAPSHOT.read_text(encoding="utf-8-sig"))
    except Exception as exc:
        return {"status": "error", "source": "Wind", "error": str(exc)}


def close_summary(data):
    rows = []
    for item in data.get("indices", []):
        rows.append({
            "指数": item.get("name"),
            "代码": item.get("code"),
            "涨跌幅": item.get("pct_change"),
            "收盘点位": item.get("last"),
            "开盘": item.get("open"),
            "最高": item.get("high"),
            "最低": item.get("low"),
            "成交额": item.get("amount"),
        })
    return {
        "source": "Wind",
        "timestamp": data.get("timestamp"),
        "trading_date": data.get("trading_date"),
        "is_close_snapshot": data.get("close_snapshot", False),
        "indices": rows,
    }


TOOLS = [
    {"name": "get_market_snapshot", "description": "读取 Wind 上证综指与科创综指最新行情快照", "inputSchema": {"type": "object", "properties": {}}},
    {"name": "get_market_status", "description": "检查 Wind 行情桥连接状态、更新时间与交易阶段", "inputSchema": {"type": "object", "properties": {}}},
    {"name": "get_close_summary", "description": "读取最近一次两项指数的收盘汇总", "inputSchema": {"type": "object", "properties": {}}},
]


def response(request):
    request_id = request.get("id")
    method = request.get("method")
    if method == "initialize":
        result = {"protocolVersion": "2025-03-26", "capabilities": {"tools": {}}, "serverInfo": {"name": "momopet-wind", "version": "1.0.0"}}
    elif method == "ping":
        result = {}
    elif method == "tools/list":
        result = {"tools": TOOLS}
    elif method == "tools/call":
        name = request.get("params", {}).get("name")
        data = read_snapshot()
        if name == "get_market_status":
            value = {key: data.get(key) for key in ("source", "status", "error", "timestamp", "trading_date", "phase", "close_snapshot")}
        elif name == "get_close_summary":
            value = close_summary(data)
        elif name == "get_market_snapshot":
            value = data
        else:
            return {"jsonrpc": "2.0", "id": request_id, "error": {"code": -32602, "message": "Unknown tool"}}
        result = {"content": [{"type": "text", "text": json.dumps(value, ensure_ascii=False)}], "structuredContent": value}
    elif method == "notifications/initialized":
        return None
    else:
        return {"jsonrpc": "2.0", "id": request_id, "error": {"code": -32601, "message": "Method not found"}}
    return {"jsonrpc": "2.0", "id": request_id, "result": result}


def main():
    for line in sys.stdin:
        try:
            request = json.loads(line)
            answer = response(request)
            if answer is not None:
                sys.stdout.write(json.dumps(answer, ensure_ascii=False) + "\n")
                sys.stdout.flush()
        except Exception as exc:
            sys.stdout.write(json.dumps({"jsonrpc": "2.0", "id": None, "error": {"code": -32603, "message": str(exc)}}) + "\n")
            sys.stdout.flush()


if __name__ == "__main__":
    main()
