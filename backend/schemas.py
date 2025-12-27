\
from __future__ import annotations

from typing import Dict, List


def npc_action_schema(allowed_handoff_ids: List[str]) -> Dict:
    """
    Structured output schema for an NPC response + optional handoff.

    Note: Structured Outputs requires:
    - additionalProperties: false for objects
    - strict: true in request
    """
    # Deduplicate + keep order
    seen = set()
    ids = []
    for _id in allowed_handoff_ids:
        if _id and _id not in seen:
            seen.add(_id)
            ids.append(_id)

    return {
        "type": "object",
        "properties": {
            "say": {"type": "string"},
            "handoff_to": {
                "oneOf": [
                    {"type": "string", "enum": ids},
                    {"type": "null"},
                ]
            },
            "handoff_reason": {
                "oneOf": [
                    {"type": "string"},
                    {"type": "null"},
                ]
            },
            "confidence": {"type": "number", "minimum": 0, "maximum": 1},
        },
        "required": ["say", "handoff_to", "handoff_reason", "confidence"],
        "additionalProperties": False,
    }
