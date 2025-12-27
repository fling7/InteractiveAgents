from __future__ import annotations

import json
import re
import time
import uuid
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from .kb import KnowledgeBase
from .openai_client import OpenAIHTTPError, OpenAIResponsesClient
from .placement import assign_spawn_points
from .projects import ProjectManager
from .schemas import npc_action_schema


def _now_ms() -> int:
    return int(time.time() * 1000)


def _slugify(s: str) -> str:
    s = s.strip().lower()
    s = re.sub(r"[^a-z0-9äöüß_-]+", "_", s, flags=re.IGNORECASE)
    s = re.sub(r"_+", "_", s).strip("_")
    return s or "agent"


@dataclass
class AgentSpec:
    id: str
    display_name: str
    persona: str
    expertise: List[str] = field(default_factory=list)
    knowledge_tags: List[str] = field(default_factory=list)
    preferred_zone_ids: List[str] = field(default_factory=list)
    preferred_spawn_tags: List[str] = field(default_factory=list)

    @staticmethod
    def from_dict(d: Dict[str, Any], idx: int) -> "AgentSpec":
        display = str(d.get("display_name") or d.get("name") or f"Agent {idx+1}")
        agent_id = str(d.get("id") or _slugify(display) or f"agent_{idx+1}")
        persona = str(d.get("persona") or "").strip()
        expertise = d.get("expertise") or []
        if isinstance(expertise, str):
            expertise = [expertise]
        knowledge_tags = d.get("knowledge_tags") or []
        if isinstance(knowledge_tags, str):
            knowledge_tags = [knowledge_tags]

        preferred_zone_ids = d.get("preferred_zone_ids") or []
        if isinstance(preferred_zone_ids, str):
            preferred_zone_ids = [preferred_zone_ids]

        preferred_spawn_tags = d.get("preferred_spawn_tags") or []
        if isinstance(preferred_spawn_tags, str):
            preferred_spawn_tags = [preferred_spawn_tags]

        return AgentSpec(
            id=agent_id,
            display_name=display,
            persona=persona,
            expertise=[str(x) for x in expertise],
            knowledge_tags=[str(x) for x in knowledge_tags],
            preferred_zone_ids=[str(x) for x in preferred_zone_ids],
            preferred_spawn_tags=[str(x) for x in preferred_spawn_tags],
        )

    def short_profile(self) -> str:
        exp = ", ".join(self.expertise) if self.expertise else "—"
        return f"{self.id} ({self.display_name}): Expertise: {exp}"


@dataclass
class SessionState:
    session_id: str
    agents: Dict[str, AgentSpec]
    placements: Dict[str, Dict[str, Any]]
    kb: KnowledgeBase
    history: List[Dict[str, str]] = field(default_factory=list)  # role=user|assistant, content=str
    created_ms: int = field(default_factory=_now_ms)
    updated_ms: int = field(default_factory=_now_ms)
    project_id: Optional[str] = None

    def touch(self) -> None:
        self.updated_ms = _now_ms()


@dataclass
class SessionStore:
    max_history_turns: int
    max_handoffs: int
    kb: KnowledgeBase
    kb_max_snippets: int
    model: str
    temperature: float
    openai: OpenAIResponsesClient
    project_manager: ProjectManager
    default_room_plan_path: str = "examples/room_plan.example.json"
    default_agents_path: str = "examples/agents.example.json"
    sessions: Dict[str, SessionState] = field(default_factory=dict)
    kb_cache: Dict[str, KnowledgeBase] = field(default_factory=dict)

    def _project_root(self) -> Path:
        return Path(__file__).resolve().parents[1]

    def _load_json_file(self, rel_path: str) -> Dict[str, Any]:
        path = (self._project_root() / rel_path).resolve()
        # Safety: ensure file is inside project
        if self._project_root() not in path.parents and path != self._project_root():
            raise ValueError("Ungültiger Pfad (außerhalb Projekt).")
        return json.loads(path.read_text(encoding="utf-8"))

    def create_session(
        self,
        room_plan: Dict[str, Any],
        agent_dicts: List[Dict[str, Any]],
        session_id: Optional[str] = None,
        kb: Optional[KnowledgeBase] = None,
        project_id: Optional[str] = None,
    ) -> SessionState:
        if not session_id:
            session_id = str(uuid.uuid4())

        agents_list: List[AgentSpec] = [AgentSpec.from_dict(d, i) for i, d in enumerate(agent_dicts)]
        agents_map = {a.id: a for a in agents_list}

        placements = assign_spawn_points(
            room_plan=room_plan,
            agents=[{
                "id": a.id,
                "preferred_zone_ids": a.preferred_zone_ids,
                "preferred_spawn_tags": a.preferred_spawn_tags,
            } for a in agents_list],
        )

        st = SessionState(
            session_id=session_id,
            agents=agents_map,
            placements=placements,
            kb=kb or self.kb,
            history=[],
            project_id=project_id,
        )
        self.sessions[session_id] = st
        return st

    def setup_from_request(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        """
        Supports:
        - direct: {"room_plan": {...}, "agents": [{"..."}]}
        - via paths: {"room_plan_path": "examples/room_plan.example.json", "agents_path": "examples/agents.example.json"}
        - via project: {"project_id": "demo_project"}
        """
        project_id = str(payload.get("project_id") or "").strip() or None
        project_room_plan = None
        project_agents = None
        project_kb = None
        if project_id:
            project_room_plan = self.project_manager.load_room_plan(project_id)
            project_agents = self.project_manager.load_agents(project_id).get("agents", [])
            project_kb = self._get_project_kb(project_id)

        room_plan_path = payload.get("room_plan_path")
        if room_plan_path:
            room_plan = self._load_json_file(str(room_plan_path))
        elif project_room_plan is not None:
            room_plan = project_room_plan
        else:
            room_plan = payload.get("room_plan") or {}
            if not room_plan:
                room_plan = self._load_json_file(self.default_room_plan_path)

        agents_path = payload.get("agents_path")
        if agents_path:
            agents_doc = self._load_json_file(str(agents_path))
            agent_dicts = agents_doc.get("agents") or []
        elif project_agents is not None:
            agent_dicts = project_agents
        else:
            agent_dicts = payload.get("agents") or payload.get("agent_specs") or []
            if not agent_dicts:
                agents_doc = self._load_json_file(self.default_agents_path)
                agent_dicts = agents_doc.get("agents") or []

        session_id = payload.get("session_id")
        st = self.create_session(
            room_plan=room_plan,
            agent_dicts=agent_dicts,
            session_id=session_id,
            kb=project_kb,
            project_id=project_id,
        )

        agents_out = []
        for aid, agent in st.agents.items():
            pl = st.placements.get(aid, {})
            agents_out.append(
                {
                    "id": agent.id,
                    "display_name": agent.display_name,
                    "position": pl.get("position", {"x": 0, "y": 0, "z": 0}),
                    "forward": pl.get("forward", {"x": 0, "y": 0, "z": 1}),
                    "spawn_point_id": pl.get("spawn_point_id"),
                    "zone_id": pl.get("zone_id"),
                    "tags": pl.get("tags", []),
                }
            )

        return {"session_id": st.session_id, "agents": agents_out}

    def _get_project_kb(self, project_id: str) -> KnowledgeBase:
        if project_id in self.kb_cache:
            return self.kb_cache[project_id]
        kb_root = self.project_manager.project_kb_root(project_id)
        kb = KnowledgeBase(kb_root, chunk_chars=self.kb.chunk_chars)
        self.kb_cache[project_id] = kb
        return kb

    # -------------------- Chat orchestration --------------------

    def _trim_history(self, history: List[Dict[str, str]]) -> List[Dict[str, str]]:
        # keep last N turns (user+assistant pairs). A turn is a user message.
        max_user_msgs = max(1, int(self.max_history_turns))
        # find last max_user_msgs user messages and keep everything after the earliest of those.
        user_indices = [i for i, m in enumerate(history) if m.get("role") == "user"]
        if len(user_indices) <= max_user_msgs:
            return history
        cutoff_user_idx = user_indices[-max_user_msgs]
        return history[cutoff_user_idx:]

    def _build_developer_prompt(self, agent: AgentSpec, others: List[AgentSpec], kb_snippets: List[Dict[str, Any]], allow_handoff: bool) -> str:
        lines: List[str] = []
        lines.append(f"Du bist ein virtueller Gesprächspartner (NPC) in Unity.")
        lines.append(f"Name: {agent.display_name} (id: {agent.id})")
        if agent.persona:
            lines.append(f"Persona:\n{agent.persona}")
        if agent.expertise:
            lines.append("Expertise (Schwerpunkte): " + ", ".join(agent.expertise))
        lines.append("")
        lines.append("Kommunikationsstil:")
        lines.append("- Antworte auf Deutsch.")
        lines.append("- Kurz, natürlich, hilfreich (Messestand/Showroom-Stil).")
        lines.append("- Wenn Informationen fehlen, stelle 1 kurze Rückfrage (statt zu raten), sofern es in deinem Bereich liegt.")
        lines.append("")
        if allow_handoff and others:
            lines.append("Handoff-Regel:")
            lines.append("- Wenn die Nutzerfrage deutlich außerhalb deiner Expertise liegt oder du unsicher bist (confidence < 0.55), leite an den am besten passenden anderen Agenten weiter.")
            lines.append("- Setze dann 'handoff_to' auf dessen id, und 'say' ist nur eine kurze Weiterleitungsformulierung (ohne ausführliche Antwort).")
            lines.append("")
            lines.append("Verfügbare andere Agenten:")
            for o in others:
                lines.append(f"- {o.id}: {o.display_name} | Expertise: {', '.join(o.expertise) if o.expertise else '—'}")
        else:
            lines.append("Handoff: deaktiviert. Antworte selbst so gut wie möglich oder bitte um Klärung.")
        lines.append("")
        if kb_snippets:
            lines.append("Lokale Wissensauszüge (nur nutzen, wenn relevant; nicht erfinden):")
            for s in kb_snippets:
                meta = f"[{s.get('tag')}/{s.get('file')}#{s.get('chunk_index')}]"
                lines.append(f"- {meta} {s.get('text')}")
            lines.append("")
        lines.append("WICHTIG: Du MUSST deine Antwort als JSON ausgeben und genau das Schema erfüllen (Structured Output).")
        return "\n".join(lines).strip()

    def _call_agent(self, st: SessionState, agent: AgentSpec, history_with_user: List[Dict[str, str]], allow_handoff: bool, forwarded_from: Optional[AgentSpec] = None, forwarded_reason: Optional[str] = None) -> Dict[str, Any]:
        # Determine allowed handoff ids (excluding self)
        allowed = [a.id for a in st.agents.values() if a.id != agent.id] if allow_handoff else []
        schema = npc_action_schema(allowed_handoff_ids=allowed)

        others = []
        for aid, a in st.agents.items():
            if aid != agent.id:
                others.append(a)

        # KB retrieval
        kb_snips = st.kb.search(query=history_with_user[-1]["content"], tags=agent.knowledge_tags, k=self.kb_max_snippets)

        dev_prompt = self._build_developer_prompt(agent, others, kb_snips, allow_handoff=allow_handoff)

        input_msgs: List[Dict[str, Any]] = [{"role": "developer", "content": dev_prompt}]

        if forwarded_from is not None:
            input_msgs.append(
                {
                    "role": "developer",
                    "content": f"Du wurdest gerade von {forwarded_from.display_name} (id: {forwarded_from.id}) an den Nutzer weitergeleitet."
                               + (f" Grund: {forwarded_reason}" if forwarded_reason else "")
                               + " Antworte direkt auf die Nutzerfrage.",
                }
            )

        # Add trimmed history
        trimmed = self._trim_history(history_with_user)
        for m in trimmed:
            input_msgs.append({"role": m["role"], "content": m["content"]})

        try:
            parsed, resp, out_text = self.openai.create_structured_json(
                model=self.model,
                input_messages=input_msgs,
                schema=schema,
                schema_name="npc_action",
                temperature=self.temperature,
            )
        except OpenAIHTTPError as e:
            # Fallback: older JSON mode (valid JSON, but not schema-validated)
            # This helps if the chosen model does not support json_schema.
            if e.status != 400:
                raise
            parsed, resp, out_text = self.openai.create_json_object(
                model=self.model,
                input_messages=input_msgs,
                temperature=self.temperature,
            )

        # Normalise
        result = {
            "say": str(parsed.get("say", "")).strip(),
            "handoff_to": parsed.get("handoff_to", None),
            "handoff_reason": parsed.get("handoff_reason", None),
            "confidence": parsed.get("confidence", 0.5),
            "_raw_text": out_text,
            "_response_id": resp.get("id"),
        }

        if not result["say"]:
            # fallback: use raw text
            result["say"] = out_text.strip() or "…"

        return result

    def chat(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        session_id = str(payload.get("session_id") or "").strip()
        if not session_id:
            raise ValueError("session_id fehlt. Bitte zuerst /setup aufrufen.")
        st = self.sessions.get(session_id)
        if not st:
            raise ValueError("Unbekannte session_id. Bitte /setup erneut aufrufen.")

        active_agent_id = str(payload.get("active_agent_id") or "").strip()
        if not active_agent_id or active_agent_id not in st.agents:
            # fallback to first agent
            active_agent_id = next(iter(st.agents.keys()))

        user_text = str(payload.get("user_text") or "").strip()
        if not user_text:
            raise ValueError("user_text ist leer.")

        history_with_user = st.history + [{"role": "user", "content": user_text}]

        agent_a = st.agents[active_agent_id]
        # Call current agent for either answer or handoff decision
        try:
            res_a = self._call_agent(st, agent_a, history_with_user, allow_handoff=True)
        except OpenAIHTTPError as e:
            return {
                "session_id": session_id,
                "active_agent_id": active_agent_id,
                "events": [
                    {"type": "say", "agent_id": active_agent_id, "text": f"[Backend] OpenAI Fehler: {e}"},
                ],
                "error": {"status": e.status, "details": e.details},
            }

        events = [{"type": "say", "agent_id": agent_a.id, "text": res_a["say"]}]
        new_active = active_agent_id
        handoff = None

        handoff_to = res_a.get("handoff_to", None)
        if handoff_to in st.agents and handoff_to != agent_a.id:
            # perform at most max_handoffs
            if self.max_handoffs > 0:
                agent_b = st.agents[handoff_to]
                try:
                    res_b = self._call_agent(st, agent_b, history_with_user, allow_handoff=False, forwarded_from=agent_a, forwarded_reason=str(res_a.get("handoff_reason") or ""))
                except OpenAIHTTPError as e:
                    res_b = {"say": f"[Backend] OpenAI Fehler beim Handoff: {e}"}
                events.append({"type": "say", "agent_id": agent_b.id, "text": res_b["say"]})
                new_active = agent_b.id
                handoff = {"from": agent_a.id, "to": agent_b.id, "reason": res_a.get("handoff_reason")}
                # Commit history: user msg + a say + b say
                st.history = history_with_user + [
                    {"role": "assistant", "content": res_a["say"]},
                    {"role": "assistant", "content": res_b["say"]},
                ]
            else:
                # no handoffs allowed: just answer from A
                st.history = history_with_user + [{"role": "assistant", "content": res_a["say"]}]
        else:
            # no handoff
            st.history = history_with_user + [{"role": "assistant", "content": res_a["say"]}]

        st.history = self._trim_history(st.history)
        st.touch()

        return {
            "session_id": session_id,
            "active_agent_id": new_active,
            "handoff": handoff,
            "events": events,
        }
