from __future__ import annotations

import json
import re
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List, Optional


def _now_ms() -> int:
    return int(time.time() * 1000)


def _slugify(s: str) -> str:
    s = s.strip().lower()
    s = re.sub(r"[^a-z0-9äöüß_-]+", "_", s, flags=re.IGNORECASE)
    s = re.sub(r"_+", "_", s).strip("_")
    return s or "project"


@dataclass
class ProjectSummary:
    id: str
    display_name: str
    description: str
    created_ms: int
    updated_ms: int


class ProjectManager:
    def __init__(self, root: Path, template_room_plan: Path, template_agents: Path) -> None:
        self.root = root
        self.root.mkdir(parents=True, exist_ok=True)
        self.template_room_plan = template_room_plan
        self.template_agents = template_agents
        self._log("Initialisiert: " + str(self.root))

    def _log(self, message: str) -> None:
        print(f"[ProjectManager] {message}", flush=True)

    def list_projects(self) -> List[Dict[str, Any]]:
        projects: List[Dict[str, Any]] = []
        if not self.root.exists():
            return projects
        for entry in sorted([p for p in self.root.iterdir() if p.is_dir()]):
            try:
                meta = self._load_project_meta(entry.name)
                projects.append(meta)
            except Exception as exc:
                self._log(f"Warnung: Projekt '{entry.name}' konnte nicht geladen werden: {exc}")
        self._log(f"Projektliste geladen: {len(projects)} Projekte")
        return projects

    def _project_dir(self, project_id: str) -> Path:
        slug = _slugify(project_id)
        path = (self.root / slug).resolve()
        if self.root not in path.parents and path != self.root:
            raise ValueError("Ungültiger Projektpfad.")
        return path

    def _project_meta_path(self, project_id: str) -> Path:
        return self._project_dir(project_id) / "project.json"

    def _agents_path(self, project_id: str) -> Path:
        return self._project_dir(project_id) / "agents.json"

    def _room_plan_path(self, project_id: str) -> Path:
        return self._project_dir(project_id) / "room_plan.json"

    def _kb_root(self, project_id: str) -> Path:
        return self._project_dir(project_id) / "kb"

    def _require_project(self, project_id: str) -> Path:
        project_dir = self._project_dir(project_id)
        if not project_dir.exists():
            raise ValueError("Projekt nicht gefunden.")
        return project_dir

    def _load_project_meta(self, project_id: str) -> Dict[str, Any]:
        path = self._project_meta_path(project_id)
        if not path.exists():
            return {
                "id": _slugify(project_id),
                "display_name": project_id,
                "description": "",
                "created_ms": _now_ms(),
                "updated_ms": _now_ms(),
            }
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            raise ValueError(f"project.json ungültig: {exc}") from exc

    def create_project(self, display_name: str, project_id: Optional[str] = None, description: str = "") -> Dict[str, Any]:
        slug = _slugify(project_id or display_name)
        project_dir = self._project_dir(slug)
        if project_dir.exists():
            raise ValueError("Projekt existiert bereits.")
        project_dir.mkdir(parents=True, exist_ok=True)
        self._kb_root(slug).mkdir(parents=True, exist_ok=True)

        meta = {
            "id": slug,
            "display_name": display_name or slug,
            "description": description or "",
            "created_ms": _now_ms(),
            "updated_ms": _now_ms(),
        }
        self._write_json(self._project_meta_path(slug), meta)

        agents = json.loads(self.template_agents.read_text(encoding="utf-8")) if self.template_agents.exists() else {"agents": []}
        room_plan = json.loads(self.template_room_plan.read_text(encoding="utf-8")) if self.template_room_plan.exists() else {}
        self._write_json(self._agents_path(slug), agents)
        self._write_json(self._room_plan_path(slug), room_plan)
        self._log(f"Projekt erstellt: {slug}")
        return meta

    def update_metadata(self, project_id: str, display_name: Optional[str] = None, description: Optional[str] = None) -> Dict[str, Any]:
        self._require_project(project_id)
        meta = self._load_project_meta(project_id)
        if display_name is not None:
            meta["display_name"] = display_name
        if description is not None:
            meta["description"] = description
        meta["updated_ms"] = _now_ms()
        self._write_json(self._project_meta_path(project_id), meta)
        self._log(f"Metadaten gespeichert: {project_id}")
        return meta

    def load_agents(self, project_id: str) -> Dict[str, Any]:
        self._require_project(project_id)
        path = self._agents_path(project_id)
        if not path.exists():
            return {"agents": []}
        return json.loads(path.read_text(encoding="utf-8"))

    def save_agents(self, project_id: str, agents: List[Dict[str, Any]]) -> None:
        self._require_project(project_id)
        payload = {"agents": agents}
        self._write_json(self._agents_path(project_id), payload)
        self._touch_project(project_id, "Agenten gespeichert")

    def load_room_plan(self, project_id: str) -> Dict[str, Any]:
        self._require_project(project_id)
        path = self._room_plan_path(project_id)
        if not path.exists():
            return {}
        return json.loads(path.read_text(encoding="utf-8"))

    def save_room_plan(self, project_id: str, room_plan: Dict[str, Any]) -> None:
        self._require_project(project_id)
        self._write_json(self._room_plan_path(project_id), room_plan)
        self._touch_project(project_id, "Room-Plan gespeichert")

    def list_knowledge(self, project_id: str) -> List[Dict[str, Any]]:
        self._require_project(project_id)
        kb_root = self._kb_root(project_id)
        items: List[Dict[str, Any]] = []
        if not kb_root.exists():
            return items
        for tag_dir in sorted([p for p in kb_root.iterdir() if p.is_dir()]):
            tag = tag_dir.name
            for fp in sorted(tag_dir.rglob("*")):
                if fp.is_file() and fp.suffix.lower() in {".txt", ".md"}:
                    name = fp.stem
                    items.append({"tag": tag, "name": name, "file": str(fp.relative_to(kb_root))})
        return items

    def read_knowledge(self, project_id: str, tag: str, name: str) -> Dict[str, Any]:
        self._require_project(project_id)
        kb_root = self._kb_root(project_id)
        safe_tag = _slugify(tag)
        safe_name = _slugify(name)
        for ext in (".txt", ".md"):
            fp = kb_root / safe_tag / f"{safe_name}{ext}"
            if fp.exists():
                return {"tag": safe_tag, "name": safe_name, "text": fp.read_text(encoding="utf-8")}
        raise ValueError("Wissenseintrag nicht gefunden.")

    def upsert_knowledge(self, project_id: str, tag: str, name: str, text: str, overwrite: bool = True) -> Dict[str, Any]:
        self._require_project(project_id)
        kb_root = self._kb_root(project_id)
        kb_root.mkdir(parents=True, exist_ok=True)
        safe_tag = _slugify(tag)
        safe_name = _slugify(name)
        if not safe_tag or not safe_name:
            raise ValueError("Tag und Name sind erforderlich.")
        tag_dir = kb_root / safe_tag
        tag_dir.mkdir(parents=True, exist_ok=True)
        fp = tag_dir / f"{safe_name}.txt"
        if fp.exists() and not overwrite:
            raise ValueError("Eintrag existiert bereits.")
        fp.write_text(text or "", encoding="utf-8")
        self._touch_project(project_id, f"Wissen gespeichert: {safe_tag}/{safe_name}")
        return {"tag": safe_tag, "name": safe_name, "file": str(fp.relative_to(kb_root))}

    def delete_knowledge(self, project_id: str, tag: str, name: str) -> None:
        self._require_project(project_id)
        kb_root = self._kb_root(project_id)
        safe_tag = _slugify(tag)
        safe_name = _slugify(name)
        for ext in (".txt", ".md"):
            fp = kb_root / safe_tag / f"{safe_name}{ext}"
            if fp.exists():
                fp.unlink()
                self._touch_project(project_id, f"Wissen gelöscht: {safe_tag}/{safe_name}")
                return
        raise ValueError("Wissenseintrag nicht gefunden.")

    def _touch_project(self, project_id: str, reason: str) -> None:
        meta = self._load_project_meta(project_id)
        meta["updated_ms"] = _now_ms()
        self._write_json(self._project_meta_path(project_id), meta)
        self._log(f"{reason} ({project_id})")

    def _write_json(self, path: Path, payload: Dict[str, Any]) -> None:
        path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")

    def get_project_details(self, project_id: str) -> Dict[str, Any]:
        self._require_project(project_id)
        meta = self._load_project_meta(project_id)
        agents = self.load_agents(project_id).get("agents", [])
        room_plan = self.load_room_plan(project_id)
        knowledge = self.list_knowledge(project_id)
        return {
            "project": meta,
            "agents": agents,
            "room_plan": room_plan,
            "knowledge": knowledge,
        }

    def project_kb_root(self, project_id: str) -> Path:
        self._require_project(project_id)
        return self._kb_root(project_id)

    def project_agents_path(self, project_id: str) -> Path:
        self._require_project(project_id)
        return self._agents_path(project_id)

    def project_room_plan_path(self, project_id: str) -> Path:
        self._require_project(project_id)
        return self._room_plan_path(project_id)
