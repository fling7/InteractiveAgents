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
                            "GET /projects": "Projekte auflisten",
                            "POST /projects/create": "Projekt erstellen",
                            "GET /projects/{id}": "Projekt-Details laden",
                            "POST /projects/{id}/metadata": "Projekt-Metadaten speichern",
                            "POST /projects/{id}/agents": "Agenten speichern",
                            "POST /projects/{id}/room-plan": "Room-Plan speichern",
                            "GET /projects/{id}/knowledge": "Wissensliste",
                            "POST /projects/{id}/knowledge": "Wissen erstellen/aktualisieren/löschen",
                            "POST /projects/{id}/knowledge/read": "Wissen laden",
                        },
                        "examples": {
                            "room_plan_path": "examples/room_plan.example.json",
                            "agents_path": "examples/agents.example.json",
                        },
                    },
                )
            if path == "/health":
                return _json_response(self, 200, {"status": "ok"})
            if path == "/projects":
                projects = store.project_manager.list_projects()
                return _json_response(self, 200, {"projects": projects})
            parts = [p for p in path.split("/") if p]
            if len(parts) >= 2 and parts[0] == "projects":
                project_id = parts[1]
                if len(parts) == 2:
                    details = store.project_manager.get_project_details(project_id)
                    return _json_response(self, 200, details)
                if len(parts) == 3 and parts[2] == "knowledge":
                    knowledge = store.project_manager.list_knowledge(project_id)
                    return _json_response(self, 200, {"knowledge": knowledge})
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
                if path == "/projects/create":
                    display_name = str(payload.get("display_name") or "").strip()
                    if not display_name:
                        raise ValueError("display_name fehlt.")
                    project_id = str(payload.get("project_id") or "").strip() or None
                    description = str(payload.get("description") or "").strip()
                    out = store.project_manager.create_project(
                        display_name=display_name,
                        project_id=project_id,
                        description=description,
                    )
                    return _json_response(self, 200, {"project": out})
                parts = [p for p in path.split("/") if p]
                if len(parts) >= 2 and parts[0] == "projects":
                    project_id = parts[1]
                    if len(parts) == 3 and parts[2] == "metadata":
                        display_name = payload.get("display_name")
                        description = payload.get("description")
                        out = store.project_manager.update_metadata(project_id, display_name=display_name, description=description)
                        return _json_response(self, 200, {"project": out})
                    if len(parts) == 3 and parts[2] == "agents":
                        agents = payload.get("agents") or []
                        if not isinstance(agents, list):
                            raise ValueError("agents muss eine Liste sein.")
                        store.project_manager.save_agents(project_id, agents)
                        return _json_response(self, 200, {"status": "ok"})
                    if len(parts) == 3 and parts[2] == "room-plan":
                        room_plan = payload.get("room_plan") or {}
                        if not isinstance(room_plan, dict):
                            raise ValueError("room_plan muss ein Objekt sein.")
                        store.project_manager.save_room_plan(project_id, room_plan)
                        return _json_response(self, 200, {"status": "ok"})
                    if len(parts) == 3 and parts[2] == "knowledge":
                        action = str(payload.get("action") or "upsert").strip().lower()
                        tag = str(payload.get("tag") or "").strip()
                        name = str(payload.get("name") or "").strip()
                        if action == "delete":
                            store.project_manager.delete_knowledge(project_id, tag=tag, name=name)
                            return _json_response(self, 200, {"status": "ok"})
                        text = str(payload.get("text") or "")
                        overwrite = bool(payload.get("overwrite", True))
                        entry = store.project_manager.upsert_knowledge(
                            project_id=project_id,
                            tag=tag,
                            name=name,
                            text=text,
                            overwrite=overwrite,
                        )
                        return _json_response(self, 200, {"entry": entry})
                    if len(parts) == 4 and parts[2] == "knowledge" and parts[3] == "read":
                        tag = str(payload.get("tag") or "").strip()
                        name = str(payload.get("name") or "").strip()
                        entry = store.project_manager.read_knowledge(project_id, tag=tag, name=name)
                        return _json_response(self, 200, entry)
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
