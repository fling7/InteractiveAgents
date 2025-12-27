\
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Sequence, Tuple


def _vec3(d: Dict[str, Any]) -> Tuple[float, float, float]:
    return (float(d.get("x", 0.0)), float(d.get("y", 0.0)), float(d.get("z", 0.0)))


def _distance(a: Tuple[float, float, float], b: Tuple[float, float, float]) -> float:
    return math.sqrt((a[0]-b[0])**2 + (a[1]-b[1])**2 + (a[2]-b[2])**2)


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

    placements: Dict[str, Dict[str, Any]] = {}

    if not spawn_points:
        # Default: circle around origin
        n = max(1, len(agents))
        radius = 2.0
        for i, a in enumerate(agents):
            angle = (2 * math.pi * i) / n
            placements[a["id"]] = {
                "position": {"x": round(radius*math.cos(angle), 3), "y": 0.0, "z": round(radius*math.sin(angle), 3)},
                "forward": {"x": 0.0, "y": 0.0, "z": 1.0},
                "spawn_point_id": None,
            }
        return placements

    # Sort agents by specificity (more preferences first)
    def pref_score(a: Dict[str, Any]) -> int:
        return len(a.get("preferred_zone_ids", []) or []) + len(a.get("preferred_spawn_tags", []) or [])

    agents_sorted = sorted(agents, key=pref_score, reverse=True)
    unused = spawn_points.copy()

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

        placements[a["id"]] = {
            "position": sp.get("position", {"x": 0.0, "y": 0.0, "z": 0.0}),
            "forward": sp.get("forward", {"x": 0.0, "y": 0.0, "z": 1.0}),
            "spawn_point_id": sp.get("id"),
            "zone_id": sp.get("zone_id"),
            "tags": sp.get("tags", []),
        }

    return placements
