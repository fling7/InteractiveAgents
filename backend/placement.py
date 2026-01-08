from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple


def _vec3(d: Dict[str, Any]) -> Tuple[float, float, float]:
    return (float(d.get("x", 0.0)), float(d.get("y", 0.0)), float(d.get("z", 0.0)))


def _distance(a: Tuple[float, float, float], b: Tuple[float, float, float]) -> float:
    return math.sqrt((a[0]-b[0])**2 + (a[1]-b[1])**2 + (a[2]-b[2])**2)


def _distance_xz(a: Tuple[float, float, float], b: Tuple[float, float, float]) -> float:
    return math.sqrt((a[0]-b[0])**2 + (a[2]-b[2])**2)


def _tokenize(text: str) -> List[str]:
    cleaned = "".join(ch.lower() if ch.isalnum() else " " for ch in text)
    return [tok for tok in cleaned.split() if len(tok) >= 3]


def _unique(seq: Iterable[str]) -> List[str]:
    seen = set()
    out = []
    for item in seq:
        if item and item not in seen:
            seen.add(item)
            out.append(item)
    return out


def _collect_room_objects(room_plan: Dict[str, Any]) -> List[Dict[str, Any]]:
    candidates: List[Dict[str, Any]] = []
    for key in ("objects", "furniture", "props", "fixtures", "obstacles"):
        value = room_plan.get(key)
        if isinstance(value, list):
            candidates.extend([obj for obj in value if isinstance(obj, dict)])
    scene = room_plan.get("scene")
    if isinstance(scene, dict):
        scene_objects = scene.get("objects")
        if isinstance(scene_objects, list):
            candidates.extend([obj for obj in scene_objects if isinstance(obj, dict)])
    return candidates


def _extract_obstacle_bounds(room_plan: Dict[str, Any]) -> List[Tuple[float, float, float, float]]:
    candidates = _collect_room_objects(room_plan)

    bounds: List[Tuple[float, float, float, float]] = []
    for item in candidates:
        if not isinstance(item, dict):
            continue
        pos = item.get("position") or item.get("pos") or {}
        center = _vec3(pos) if isinstance(pos, dict) else (0.0, 0.0, 0.0)
        size = item.get("size") or item.get("dimensions") or item.get("scale") or {}
        if isinstance(size, dict) and ("x" in size or "z" in size or "width" in size or "depth" in size):
            sx = float(size.get("x", size.get("width", 0.0)) or 0.0)
            sz = float(size.get("z", size.get("depth", 0.0)) or 0.0)
            hx = abs(sx) * 0.5
            hz = abs(sz) * 0.5
            bounds.append((center[0] - hx, center[0] + hx, center[2] - hz, center[2] + hz))
            continue

        bbox = item.get("bounds") or item.get("bbox") or {}
        if isinstance(bbox, dict):
            bmin = bbox.get("min") or {}
            bmax = bbox.get("max") or {}
            if isinstance(bmin, dict) and isinstance(bmax, dict):
                minx = float(bmin.get("x", center[0]))
                maxx = float(bmax.get("x", center[0]))
                minz = float(bmin.get("z", center[2]))
                maxz = float(bmax.get("z", center[2]))
                bounds.append((min(minx, maxx), max(minx, maxx), min(minz, maxz), max(minz, maxz)))

    return bounds


def _point_blocked(point: Tuple[float, float, float], obstacles: Sequence[Tuple[float, float, float, float]], padding: float = 0.2) -> bool:
    x, _, z = point
    for minx, maxx, minz, maxz in obstacles:
        if (minx - padding) <= x <= (maxx + padding) and (minz - padding) <= z <= (maxz + padding):
            return True
    return False


def _clamp_floor(position: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "x": float(position.get("x", 0.0)),
        "y": 0.0,
        "z": float(position.get("z", 0.0)),
    }


def summarize_room_objects(
    room_plan: Dict[str, Any],
    *,
    floor_only: bool = True,
    floor_y_threshold: float = 0.3,
    floor_slice_offset: float = 0.05,
) -> List[Dict[str, Any]]:
    objects = _collect_room_objects(room_plan)

    floor_y = 0.0
    for obj in objects:
        obj_type = obj.get("objectType") or obj.get("type") or obj.get("object_type")
        obj_id = obj.get("objectId") or obj.get("id")
        group = obj.get("group")
        if str(obj_type).lower() == "floor" or str(obj_id).lower() == "floor" or str(group).lower() == "floor":
            pos = obj.get("position") or obj.get("pos") or {}
            size = obj.get("size") or obj.get("dimensions") or obj.get("scale") or {}
            if isinstance(pos, dict):
                y = float(pos.get("y", 0.0))
            else:
                y = 0.0
            height = 0.0
            if isinstance(size, dict):
                height = float(size.get("y", size.get("height", 0.0)) or 0.0)
            floor_y = max(floor_y, y + 0.5 * abs(height))
    slice_height = floor_y + floor_slice_offset

    summaries = []
    for idx, obj in enumerate(objects):
        if not isinstance(obj, dict):
            continue
        pos = obj.get("position") or obj.get("pos") or {}
        if not isinstance(pos, dict):
            continue
        position = {"x": float(pos.get("x", 0.0)), "y": float(pos.get("y", 0.0)), "z": float(pos.get("z", 0.0))}
        size = obj.get("size") or obj.get("dimensions") or obj.get("scale") or {}
        radius = 0.4
        height = 0.0
        if isinstance(size, dict):
            sx = float(size.get("x", size.get("width", 0.0)) or 0.0)
            sz = float(size.get("z", size.get("depth", 0.0)) or 0.0)
            height = float(size.get("y", size.get("height", 0.0)) or 0.0)
            radius = max(0.2, 0.5 * max(abs(sx), abs(sz)))
        if floor_only and height:
            min_y = position["y"] - 0.5 * abs(height)
            max_y = position["y"] + 0.5 * abs(height)
            if not (min_y <= slice_height <= max_y):
                continue
        elif floor_only and abs(position["y"]) > floor_y_threshold:
            continue
        obj_id = obj.get("id") or obj.get("objectId") or f"obj_{idx+1}"
        name = obj.get("name") or obj.get("objectType") or obj_id or f"Objekt {idx+1}"
        summaries.append(
            {
                "id": str(obj_id),
                "name": str(name),
                "object_type": obj.get("objectType") or obj.get("type") or obj.get("object_type"),
                "group": obj.get("group"),
                "slice_height": slice_height,
                "position": position,
                "radius": radius,
            }
        )

    return summaries


def _room_objects_from_preview(preview_objects: Any) -> List[Dict[str, Any]]:
    if not isinstance(preview_objects, list):
        return []
    summaries = []
    for idx, obj in enumerate(preview_objects):
        if not isinstance(obj, dict):
            continue
        pos = obj.get("position") or {}
        if not isinstance(pos, dict):
            continue
        position = {
            "x": float(pos.get("x", 0.0)),
            "y": 0.0,
            "z": float(pos.get("z", 0.0)),
        }
        radius = float(obj.get("radius", 0.4) or 0.4)
        if radius <= 0:
            radius = 0.4
        summaries.append(
            {
                "id": str(obj.get("id") or f"obj_{idx+1}"),
                "name": str(obj.get("name") or f"Objekt {idx+1}"),
                "object_type": obj.get("object_type"),
                "group": obj.get("group"),
                "slice_height": obj.get("slice_height"),
                "position": position,
                "radius": radius,
            }
        )
    return summaries


def _agent_candidates_from_preview(preview_agents: Any) -> Dict[str, Dict[str, Any]]:
    if not isinstance(preview_agents, list):
        return {}
    candidates: Dict[str, Dict[str, Any]] = {}
    for entry in preview_agents:
        if not isinstance(entry, dict):
            continue
        agent_id = entry.get("id")
        if not isinstance(agent_id, str) or not agent_id:
            continue
        pos = entry.get("position") or {}
        if not isinstance(pos, dict):
            continue
        position = {
            "x": float(pos.get("x", 0.0)),
            "y": 0.0,
            "z": float(pos.get("z", 0.0)),
        }
        candidates[agent_id] = {
            "id": agent_id,
            "display_name": entry.get("display_name"),
            "position": position,
        }
    return candidates


def _find_open_position(
    start: Tuple[float, float, float],
    room_objects: Sequence[Dict[str, Any]],
    placed: Sequence[Tuple[float, float, float]],
    *,
    min_spacing: float = 0.7,
    object_padding: float = 0.4,
    max_radius: float = 6.0,
    step: float = 0.35,
) -> Tuple[float, float, float]:
    def blocked(candidate: Tuple[float, float, float]) -> bool:
        for obj in room_objects:
            pos = obj.get("position") or {}
            if not isinstance(pos, dict):
                continue
            center = (float(pos.get("x", 0.0)), 0.0, float(pos.get("z", 0.0)))
            radius = float(obj.get("radius", 0.4) or 0.4)
            if _distance_xz(candidate, center) < (radius + object_padding):
                return True
        for other in placed:
            if _distance_xz(candidate, other) < min_spacing:
                return True
        return False

    candidate = (start[0], 0.0, start[2])
    if not blocked(candidate):
        return candidate

    radius = step
    while radius <= max_radius:
        for i in range(16):
            angle = (2 * math.pi * i) / 16
            candidate = (start[0] + radius * math.cos(angle), 0.0, start[2] + radius * math.sin(angle))
            if not blocked(candidate):
                return candidate
        radius += step
    return (start[0], 0.0, start[2])


def normalize_placement_preview(
    room_plan: Dict[str, Any],
    agents: List[Dict[str, Any]],
    preview: Optional[Dict[str, Any]],
) -> Dict[str, Any]:
    room_objects = summarize_room_objects(room_plan, floor_only=True)
    if not room_objects:
        room_objects = _room_objects_from_preview(preview.get("room_objects") if isinstance(preview, dict) else None)

    candidates = _agent_candidates_from_preview(preview.get("agent_placements") if isinstance(preview, dict) else None)
    placed_positions: List[Tuple[float, float, float]] = []
    agent_placements = []

    fallback_agents = []
    for agent in agents:
        agent_id = agent.get("id") if isinstance(agent, dict) else None
        if not agent_id:
            continue
        candidate = candidates.get(agent_id)
        if candidate and candidate.get("position"):
            pos = candidate["position"]
            start = (float(pos.get("x", 0.0)), 0.0, float(pos.get("z", 0.0)))
            resolved = _find_open_position(start, room_objects, placed_positions)
            placed_positions.append(resolved)
            agent_placements.append(
                {
                    "id": agent_id,
                    "display_name": agent.get("display_name") or candidate.get("display_name") or agent_id,
                    "position": {"x": round(resolved[0], 3), "y": 0.0, "z": round(resolved[2], 3)},
                }
            )
        else:
            fallback_agents.append(agent)

    if fallback_agents:
        fallback = assign_spawn_points(room_plan, fallback_agents)
        for agent in fallback_agents:
            agent_id = agent.get("id")
            placement = fallback.get(agent_id) if agent_id else None
            if not placement or not placement.get("position"):
                continue
            pos = placement["position"]
            start = (float(pos.get("x", 0.0)), 0.0, float(pos.get("z", 0.0)))
            resolved = _find_open_position(start, room_objects, placed_positions)
            placed_positions.append(resolved)
            agent_placements.append(
                {
                    "id": agent_id,
                    "display_name": agent.get("display_name") or agent_id,
                    "position": {"x": round(resolved[0], 3), "y": 0.0, "z": round(resolved[2], 3)},
                }
            )

    return {"room_objects": room_objects, "agent_placements": agent_placements}


def _infer_preferences(room_plan: Dict[str, Any], agent: Dict[str, Any]) -> Tuple[List[str], List[str]]:
    tokens: List[str] = []
    for field in ("display_name", "persona", "id"):
        value = agent.get(field)
        if isinstance(value, str):
            tokens.extend(_tokenize(value))
    for field in ("expertise", "knowledge_tags"):
        value = agent.get(field)
        if isinstance(value, list):
            for entry in value:
                if isinstance(entry, str):
                    tokens.extend(_tokenize(entry))
    tokens = _unique(tokens)

    zone_ids = []
    zones = room_plan.get("zones") or []
    if isinstance(zones, list):
        for zone in zones:
            if not isinstance(zone, dict):
                continue
            zone_tokens: List[str] = []
            for field in ("id", "name"):
                value = zone.get(field)
                if isinstance(value, str):
                    zone_tokens.extend(_tokenize(value))
            tags = zone.get("tags") or []
            if isinstance(tags, list):
                for tag in tags:
                    if isinstance(tag, str):
                        zone_tokens.extend(_tokenize(tag))
            if set(zone_tokens) & set(tokens):
                zone_id = zone.get("id")
                if isinstance(zone_id, str):
                    zone_ids.append(zone_id)

    spawn_tags = []
    spawn_points = room_plan.get("spawn_points") or []
    if isinstance(spawn_points, list):
        for sp in spawn_points:
            if not isinstance(sp, dict):
                continue
            tags = sp.get("tags") or []
            if not isinstance(tags, list):
                continue
            for tag in tags:
                if isinstance(tag, str) and set(_tokenize(tag)) & set(tokens):
                    spawn_tags.append(tag)

    return _unique(zone_ids), _unique(spawn_tags)


def suggest_agent_placements(
    room_plan: Dict[str, Any],
    agents: List[Dict[str, Any]],
) -> Tuple[Dict[str, Dict[str, Any]], List[Dict[str, Any]]]:
    enriched = []
    for agent in agents:
        agent_copy = dict(agent)
        inferred_zones, inferred_tags = _infer_preferences(room_plan, agent_copy)
        existing_zones = agent_copy.get("preferred_zone_ids") or []
        existing_tags = agent_copy.get("preferred_spawn_tags") or []
        if isinstance(existing_zones, str):
            existing_zones = [existing_zones]
        if isinstance(existing_tags, str):
            existing_tags = [existing_tags]
        agent_copy["preferred_zone_ids"] = _unique(list(existing_zones) + inferred_zones)
        agent_copy["preferred_spawn_tags"] = _unique(list(existing_tags) + inferred_tags)
        enriched.append(agent_copy)

    placements = assign_spawn_points(room_plan, enriched)
    return placements, enriched


def assign_spawn_points(room_plan: Dict[str, Any], agents: List[Dict[str, Any]]) -> Dict[str, Dict[str, Any]]:
    """
    Greedy placement:
    - Prefer matching zone_id and tags
    - Fall back to unused spawnpoints
    - If no spawnpoints present, generate default positions in a circle
    Returns: {agent_id: {"position":{x,y,z},"forward":{x,y,z},"spawn_point_id":...}}
    """
    spawn_points = list(room_plan.get("spawn_points", []) or [])
    zones = {z.get("id"): z for z in (room_plan.get("zones", []) or [])}
    obstacles = _extract_obstacle_bounds(room_plan)

    placements: Dict[str, Dict[str, Any]] = {}
    used_spawn_ids = set()

    def spawn_available(sp: Dict[str, Any]) -> bool:
        sp_id = sp.get("id")
        if sp_id and sp_id in used_spawn_ids:
            return False
        pos = _vec3(sp.get("position", {}))
        return not _point_blocked(pos, obstacles)

    unused = [sp for sp in spawn_points if spawn_available(sp)]

    preassigned_agents = []
    remaining_agents = []
    for agent in agents:
        if not isinstance(agent, dict):
            continue
        if agent.get("position") is not None or agent.get("spawn_point_id") is not None:
            preassigned_agents.append(agent)
        else:
            remaining_agents.append(agent)

    for agent in preassigned_agents:
        agent_id = agent.get("id")
        if not agent_id:
            continue
        sp_id = agent.get("spawn_point_id")
        chosen_sp = None
        if sp_id:
            for sp in spawn_points:
                if sp.get("id") == sp_id and spawn_available(sp):
                    chosen_sp = sp
                    break
        if chosen_sp is not None:
            used_spawn_ids.add(chosen_sp.get("id"))
            if chosen_sp in unused:
                unused.remove(chosen_sp)
            placements[agent_id] = {
                "position": _clamp_floor(chosen_sp.get("position", {})),
                "forward": chosen_sp.get("forward", {"x": 0.0, "y": 0.0, "z": 1.0}),
                "spawn_point_id": chosen_sp.get("id"),
                "zone_id": chosen_sp.get("zone_id"),
                "tags": chosen_sp.get("tags", []),
            }
            continue

        raw_pos = agent.get("position")
        if isinstance(raw_pos, dict):
            pos = _vec3(raw_pos)
            if not _point_blocked(pos, obstacles):
                forward = agent.get("forward") if isinstance(agent.get("forward"), dict) else {"x": 0.0, "y": 0.0, "z": 1.0}
                placements[agent_id] = {
                    "position": _clamp_floor(raw_pos),
                    "forward": forward,
                    "spawn_point_id": None,
                }
                continue

        remaining_agents.append(agent)

    if not spawn_points:
        # Default: circle around origin
        n = max(1, len(remaining_agents))
        radius = 2.0
        min_spacing = 0.6
        for i, a in enumerate(remaining_agents):
            angle = (2 * math.pi * i) / n
            attempt = 0
            while True:
                candidate = (radius * math.cos(angle), 0.0, radius * math.sin(angle))
                if not _point_blocked(candidate, obstacles):
                    if all(_distance(candidate, _vec3(p["position"])) >= min_spacing for p in placements.values()):
                        break
                radius += 0.4
                attempt += 1
                if attempt > 12:
                    break
            placements[a["id"]] = {
                "position": {"x": round(candidate[0], 3), "y": 0.0, "z": round(candidate[2], 3)},
                "forward": {"x": 0.0, "y": 0.0, "z": 1.0},
                "spawn_point_id": None,
            }
        return placements

    if not unused:
        n = max(1, len(remaining_agents))
        radius = 2.0
        min_spacing = 0.6
        for i, a in enumerate(remaining_agents):
            angle = (2 * math.pi * i) / n
            attempt = 0
            while True:
                candidate = (radius * math.cos(angle), 0.0, radius * math.sin(angle))
                if not _point_blocked(candidate, obstacles):
                    if all(_distance(candidate, _vec3(p["position"])) >= min_spacing for p in placements.values()):
                        break
                radius += 0.4
                attempt += 1
                if attempt > 12:
                    break
            placements[a["id"]] = {
                "position": {"x": round(candidate[0], 3), "y": 0.0, "z": round(candidate[2], 3)},
                "forward": {"x": 0.0, "y": 0.0, "z": 1.0},
                "spawn_point_id": None,
            }
        return placements

    # Sort agents by specificity (more preferences first)
    def pref_score(a: Dict[str, Any]) -> int:
        return len(a.get("preferred_zone_ids", []) or []) + len(a.get("preferred_spawn_tags", []) or [])

    agents_sorted = sorted(remaining_agents, key=pref_score, reverse=True)

    for a in agents_sorted:
        best_idx = None
        best_score = -1e9

        pref_zones = set(a.get("preferred_zone_ids", []) or [])
        pref_tags = set([t for t in (a.get("preferred_spawn_tags", []) or []) if t])

        for idx, sp in enumerate(unused):
            score = 0.0
            sp_zone = sp.get("zone_id")
            sp_tags = set(sp.get("tags", []) or [])

            if sp_zone and sp_zone in pref_zones:
                score += 10.0
            score += 3.0 * len(sp_tags & pref_tags)

            # Slight preference for "entrance" if no prefs at all
            if not pref_zones and not pref_tags and sp_zone == "entrance":
                score += 0.5

            # Small penalty for far away from origin (keeps things tight by default)
            pos = _vec3(sp.get("position", {}))
            score -= 0.05 * _distance(pos, (0.0, 0.0, 0.0))

            if score > best_score:
                best_score = score
                best_idx = idx

        if best_idx is None:
            sp = unused.pop(0)
        else:
            sp = unused.pop(best_idx)
        if sp.get("id"):
            used_spawn_ids.add(sp.get("id"))

        placements[a["id"]] = {
            "position": _clamp_floor(sp.get("position", {"x": 0.0, "y": 0.0, "z": 0.0})),
            "forward": sp.get("forward", {"x": 0.0, "y": 0.0, "z": 1.0}),
            "spawn_point_id": sp.get("id"),
            "zone_id": sp.get("zone_id"),
            "tags": sp.get("tags", []),
        }

    return placements
