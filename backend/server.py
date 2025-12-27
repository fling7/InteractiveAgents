\
from __future__ import annotations

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, Optional, Tuple
from urllib.parse import urlparse

from .state import SessionStore


def _json_response(handler: BaseHTTPRequestHandler, status: int, payload: Dict[str, Any]) -> None:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json; charset=utf-8")
    handler.send_header("Content-Length", str(len(data)))
    # CORS (useful for WebGL or external tools)
    handler.send_header("Access-Control-Allow-Origin", "*")
    handler.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
    handler.send_header("Access-Control-Allow-Headers", "Content-Type")
    handler.end_headers()
    handler.wfile.write(data)


def _read_json(handler: BaseHTTPRequestHandler) -> Dict[str, Any]:
    length = int(handler.headers.get("Content-Length", "0"))
    if length <= 0:
        return {}
    raw = handler.rfile.read(length).decode("utf-8", errors="replace")
    if not raw.strip():
        return {}
    return json.loads(raw)


def start_http_server(host: str, port: int, store: SessionStore) -> None:
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, format: str, *args) -> None:  # noqa: N802
            # Slightly quieter default logging
            print("[HTTP]", format % args)

        def do_OPTIONS(self) -> None:  # noqa: N802
            self.send_response(204)
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")
            self.end_headers()

        def do_GET(self) -> None:  # noqa: N802
            path = urlparse(self.path).path
            if path == "/":
                return _json_response(
                    self,
                    200,
                    {
                        "message": "Backend läuft.",
                        "endpoints": {
                            "GET /health": "Status-Check",
                            "POST /setup": "Session/Agenten Setup",
                            "POST /chat": "Chat mit Agent",
                        },
                        "examples": {
                            "room_plan_path": "examples/room_plan.example.json",
                            "agents_path": "examples/agents.example.json",
                        },
                    },
                )
            if path == "/health":
                return _json_response(self, 200, {"status": "ok"})
            return _json_response(self, 404, {"error": "Not found", "path": path})

        def do_POST(self) -> None:  # noqa: N802
            path = urlparse(self.path).path
            try:
                payload = _read_json(self)
            except json.JSONDecodeError as e:
                return _json_response(self, 400, {"error": "Invalid JSON", "details": str(e)})

            try:
                if path == "/setup":
                    out = store.setup_from_request(payload)
                    return _json_response(self, 200, out)
                if path == "/chat":
                    out = store.chat(payload)
                    return _json_response(self, 200, out)
                return _json_response(self, 404, {"error": "Not found", "path": path})
            except ValueError as e:
                return _json_response(self, 400, {"error": str(e)})
            except Exception as e:
                return _json_response(self, 500, {"error": "Server error", "details": str(e)})

    httpd = ThreadingHTTPServer((host, port), Handler)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[Backend] Beende Server…")
    finally:
        httpd.server_close()
