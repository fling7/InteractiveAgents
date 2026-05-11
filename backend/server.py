from __future__ import annotations

import json
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, Optional, Tuple
from urllib.parse import urlparse

from .state import SessionStore


def _set_cors_headers(handler: BaseHTTPRequestHandler) -> None:
    handler.send_header("Access-Control-Allow-Origin", "*")
    handler.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
    handler.send_header(
        "Access-Control-Allow-Headers",
        "Content-Type, Accept, X-Requested-With, X-Unity-Version",
    )
    handler.send_header("Access-Control-Allow-Private-Network", "true")


def _json_response(handler: BaseHTTPRequestHandler, status: int, payload: Dict[str, Any]) -> None:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json; charset=utf-8")
    handler.send_header("Content-Length", str(len(data)))
    _set_cors_headers(handler)
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


def _read_multipart(handler: BaseHTTPRequestHandler) -> Tuple[Dict[str, Any], Dict[str, Dict[str, Any]]]:
    content_type = handler.headers.get("Content-Type", "")
    if "multipart/form-data" not in content_type.lower():
        raise ValueError("Content-Type muss multipart/form-data sein.")

    length = int(handler.headers.get("Content-Length", "0"))
    if length <= 0:
        raise ValueError("Multipart-Body fehlt.")

    raw = handler.rfile.read(length)
    msg = BytesParser(policy=policy.default).parsebytes(
        b"Content-Type: " + content_type.encode("utf-8") + b"\r\nMIME-Version: 1.0\r\n\r\n" + raw
    )
    if not msg.is_multipart():
        raise ValueError("Multipart-Body konnte nicht gelesen werden.")

    fields: Dict[str, Any] = {}
    files: Dict[str, Dict[str, Any]] = {}
    for part in msg.iter_parts():
        disposition = part.get("Content-Disposition", "")
        if "form-data" not in disposition:
            continue
        name = part.get_param("name", header="content-disposition")
        if not name:
            continue
        data = part.get_payload(decode=True) or b""
        filename = part.get_filename()
        if filename is None:
            fields[name] = data.decode(part.get_content_charset("utf-8"), errors="replace")
        else:
            files[name] = {
                "filename": filename,
                "content_type": part.get_content_type(),
                "content": data,
            }
    return fields, files


def _binary_response(handler: BaseHTTPRequestHandler, status: int, payload: bytes, content_type: str) -> None:
    handler.send_response(status)
    handler.send_header("Content-Type", content_type)
    handler.send_header("Content-Length", str(len(payload)))
    _set_cors_headers(handler)
    handler.end_headers()
    handler.wfile.write(payload)


def start_http_server(host: str, port: int, store: SessionStore) -> None:
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, format: str, *args) -> None:  # noqa: N802
            # Slightly quieter default logging
            print("[HTTP]", format % args, flush=True)

        def _log_action(self, message: str) -> None:
            print(f"[ProjectAPI] {message}", flush=True)

        def _log_request(self) -> None:
            print(f"[HTTP] -> {self.command} {self.path}", flush=True)

        def _send_json(self, status: int, payload: Dict[str, Any]) -> None:
            print(f"[HTTP] <- {self.command} {self.path} {status}", flush=True)
            _json_response(self, status, payload)

        def _send_binary(self, status: int, payload: bytes, content_type: str) -> None:
            print(f"[HTTP] <- {self.command} {self.path} {status}", flush=True)
            _binary_response(self, status, payload, content_type)

        def do_OPTIONS(self) -> None:  # noqa: N802
            self._log_request()
            self.send_response(204)
            _set_cors_headers(self)
            self.end_headers()
            print(f"[HTTP] <- {self.command} {self.path} 204", flush=True)

        def do_GET(self) -> None:  # noqa: N802
            self._log_request()
            path = urlparse(self.path).path
            try:
                if path == "/":
                    return self._send_json(
                        200,
                        {
                            "message": "Backend läuft.",
                            "endpoints": {
                                "GET /health": "Status-Check",
                                "POST /setup": "Session/Agenten Setup",
                                "POST /chat": "Chat mit Agent",
                                "GET /projects": "Projekte auflisten",
                                "POST /projects/create": "Projekt erstellen",
                                "POST /projects/arrow/analyze": "MLDSI analysieren",
                                "POST /projects/arrow/chat": "MLDSI-Chat fortsetzen",
                                "POST /projects/arrow/commit": "Projekt aus MLDSI erstellen",
                                "GET /projects/{id}": "Projekt-Details laden",
                                "POST /projects/{id}/metadata": "Projekt-Metadaten speichern",
                                "POST /projects/{id}/agents": "Agenten speichern",
                                "POST /projects/{id}/room-plan": "Room-Plan speichern",
                                "GET /projects/{id}/knowledge": "Wissensliste",
                                "POST /projects/{id}/knowledge": "Wissen erstellen/aktualisieren/löschen",
                                "POST /projects/{id}/knowledge/read": "Wissen laden",
                                "POST /stt": "Speech-to-Text transkribieren",
                                "POST /tts": "Text-to-Speech erzeugen",
                            },
                            "examples": {
                                "room_plan_path": "examples/room_plan.example.json",
                                "agents_path": "examples/agents.example.json",
                            },
                        },
                    )
                if path == "/health":
                    return self._send_json(200, {"status": "ok"})
                if path == "/projects":
                    self._log_action("Liste Projekte abrufen")
                    projects = store.project_manager.list_projects()
                    return self._send_json(200, {"projects": projects})
                parts = [p for p in path.split("/") if p]
                if len(parts) >= 2 and parts[0] == "projects":
                    project_id = parts[1]
                    if len(parts) == 2:
                        self._log_action(f"Projekt laden: {project_id}")
                        details = store.project_manager.get_project_details(project_id)
                        return self._send_json(200, details)
                    if len(parts) == 3 and parts[2] == "knowledge":
                        self._log_action(f"Wissenliste laden: {project_id}")
                        knowledge = store.project_manager.list_knowledge(project_id)
                        return self._send_json(200, {"knowledge": knowledge})
                return self._send_json(404, {"error": "Not found", "path": path})
            except ValueError as exc:
                self._log_action(f"Fehler GET {path}: {exc}")
                return self._send_json(400, {"error": str(exc)})
            except Exception as exc:
                self._log_action(f"Fehler GET {path}: {exc}")
                return self._send_json(500, {"error": "Server error", "details": str(exc)})

        def do_POST(self) -> None:  # noqa: N802
            self._log_request()
            path = urlparse(self.path).path
            if path == "/stt":
                try:
                    fields, files = _read_multipart(self)
                    audio_info = files.get("audio") or files.get("file") or {}
                    self._log_action(
                        "STT anfordern: "
                        f"filename={audio_info.get('filename')}, bytes={len(audio_info.get('content') or b'')}"
                    )
                    out = store.stt(fields, files)
                    return self._send_json(200, out)
                except ValueError as exc:
                    self._log_action(f"Fehler POST {path}: {exc}")
                    return self._send_json(400, {"error": str(exc)})
                except Exception as exc:
                    self._log_action(f"Fehler POST {path}: {exc}")
                    return self._send_json(500, {"error": "Server error", "details": str(exc)})

            try:
                payload = _read_json(self)
            except json.JSONDecodeError as e:
                return self._send_json(400, {"error": "Invalid JSON", "details": str(e)})

            try:
                if path == "/setup":
                    out = store.setup_from_request(payload)
                    return self._send_json(200, out)
                if path == "/chat":
                    out = store.chat(payload)
                    return self._send_json(200, out)
                if path == "/tts":
                    text_preview = str(payload.get("text") or "")
                    text_len = len(text_preview.strip())
                    voice = str(payload.get("voice") or "").strip() or "alloy"
                    tts_model = str(payload.get("tts_model") or "").strip() or "gpt-4o-mini-tts"
                    response_format = str(payload.get("response_format") or "mp3").strip() or "mp3"
                    self._log_action(
                        "TTS anfordern: "
                        f"text_len={text_len}, voice={voice}, model={tts_model}, format={response_format}"
                    )
                    audio, content_type = store.tts(payload)
                    self._log_action(
                        "TTS bereitgestellt: "
                        f"bytes={len(audio)}, content_type={content_type}"
                    )
                    return self._send_binary(200, audio, content_type)
                if path == "/projects/create":
                    display_name = str(payload.get("display_name") or "").strip()
                    if not display_name:
                        raise ValueError("display_name fehlt.")
                    project_id = str(payload.get("project_id") or "").strip() or None
                    description = str(payload.get("description") or "").strip()
                    self._log_action(f"Projekt erstellen: name='{display_name}', id='{project_id or ''}'")
                    out = store.project_manager.create_project(
                        display_name=display_name,
                        project_id=project_id,
                        description=description,
                    )
                    return self._send_json(200, {"project": out})
                if path == "/projects/arrow/analyze":
                    self._log_action("MLDSI analysieren")
                    out = store.analyze_arrow(payload)
                    return self._send_json(200, out)
                if path == "/projects/arrow/chat":
                    self._log_action("MLDSI-Chat fortsetzen")
                    out = store.arrow_chat(payload)
                    return self._send_json(200, out)
                if path == "/projects/arrow/commit":
                    self._log_action("Projekt aus MLDSI erstellen")
                    out = store.commit_arrow_project(payload)
                    return self._send_json(200, out)
                parts = [p for p in path.split("/") if p]
                if len(parts) >= 2 and parts[0] == "projects":
                    project_id = parts[1]
                    if len(parts) == 3 and parts[2] == "metadata":
                        display_name = payload.get("display_name")
                        description = payload.get("description")
                        self._log_action(f"Metadaten speichern: {project_id}")
                        out = store.project_manager.update_metadata(project_id, display_name=display_name, description=description)
                        return self._send_json(200, {"project": out})
                    if len(parts) == 3 and parts[2] == "agents":
                        agents = payload.get("agents") or []
                        if not isinstance(agents, list):
                            raise ValueError("agents muss eine Liste sein.")
                        self._log_action(f"Agenten speichern: {project_id} ({len(agents)})")
                        store.project_manager.save_agents(project_id, agents)
                        return self._send_json(200, {"status": "ok"})
                    if len(parts) == 3 and parts[2] == "room-plan":
                        room_plan = payload.get("room_plan") or {}
                        if not isinstance(room_plan, dict):
                            raise ValueError("room_plan muss ein Objekt sein.")
                        self._log_action(f"Room-Plan speichern: {project_id}")
                        store.project_manager.save_room_plan(project_id, room_plan)
                        return self._send_json(200, {"status": "ok"})
                    if len(parts) == 3 and parts[2] == "knowledge":
                        action = str(payload.get("action") or "upsert").strip().lower()
                        tag = str(payload.get("tag") or "").strip()
                        name = str(payload.get("name") or "").strip()
                        if action == "delete":
                            self._log_action(f"Wissen löschen: {project_id} {tag}/{name}")
                            store.project_manager.delete_knowledge(project_id, tag=tag, name=name)
                            store.refresh_project_kb(project_id)
                            return self._send_json(200, {"status": "ok"})
                        text = str(payload.get("text") or "")
                        overwrite = bool(payload.get("overwrite", True))
                        self._log_action(f"Wissen speichern: {project_id} {tag}/{name}")
                        entry = store.project_manager.upsert_knowledge(
                            project_id=project_id,
                            tag=tag,
                            name=name,
                            text=text,
                            overwrite=overwrite,
                        )
                        store.refresh_project_kb(project_id)
                        return self._send_json(200, {"entry": entry})
                    if len(parts) == 4 and parts[2] == "knowledge" and parts[3] == "read":
                        tag = str(payload.get("tag") or "").strip()
                        name = str(payload.get("name") or "").strip()
                        self._log_action(f"Wissen laden: {project_id} {tag}/{name}")
                        entry = store.project_manager.read_knowledge(project_id, tag=tag, name=name)
                        return self._send_json(200, entry)
                return self._send_json(404, {"error": "Not found", "path": path})
            except ValueError as e:
                self._log_action(f"Fehler POST {path}: {e}")
                return self._send_json(400, {"error": str(e)})
            except Exception as e:
                self._log_action(f"Fehler POST {path}: {e}")
                return self._send_json(500, {"error": "Server error", "details": str(e)})

    httpd = ThreadingHTTPServer((host, port), Handler)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[Backend] Beende Server…")
    finally:
        httpd.server_close()
