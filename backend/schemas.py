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


def arrow_project_schema() -> Dict:
    return {
        "type": "object",
        "properties": {
            "assistant_message": {"type": "string"},
            "analysis": {"type": "string"},
            "project": {
                "type": "object",
                "properties": {
                    "display_name": {"type": "string"},
                    "description": {"type": "string"},
                },
                "required": ["display_name", "description"],
                "additionalProperties": False,
            },
            "agents": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "id": {"type": "string"},
                        "display_name": {"type": "string"},
                        "persona": {"type": "string"},
                        "voice": {"type": "string"},
                        "voice_gender": {"type": "string"},
                        "voice_style": {"type": "string"},
                        "tts_model": {"type": "string"},
                        "expertise": {
                            "type": "array",
                            "items": {"type": "string"},
                        },
                        "knowledge_tags": {
                            "type": "array",
                            "items": {"type": "string"},
                        },
                    },
                    "required": [
                        "id",
                        "display_name",
                        "persona",
                        "voice",
                        "voice_gender",
                        "voice_style",
                        "tts_model",
                        "expertise",
                        "knowledge_tags",
                    ],
                    "additionalProperties": False,
                },
            },
            "knowledge": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "tag": {"type": "string"},
                        "name": {"type": "string"},
                        "text": {"type": "string"},
                    },
                    "required": ["tag", "name", "text"],
                    "additionalProperties": False,
                },
            },
            "placement_preview": {
                "type": "object",
                "properties": {
                    "room_objects": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "id": {"type": "string"},
                                "name": {"type": "string"},
                                "object_type": {"type": ["string", "null"]},
                                "group": {"type": ["string", "null"]},
                                "slice_height": {"type": ["number", "null"]},
                                "position": {
                                    "type": "object",
                                    "properties": {
                                        "x": {"type": "number"},
                                        "y": {"type": "number"},
                                        "z": {"type": "number"},
                                    },
                                    "required": ["x", "y", "z"],
                                    "additionalProperties": False,
                                },
                                "radius": {"type": "number"},
                            },
                            "required": ["id", "name", "position", "radius"],
                            "additionalProperties": False,
                        },
                    },
                    "agent_placements": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "id": {"type": "string"},
                                "display_name": {"type": "string"},
                                "position": {
                                    "type": "object",
                                    "properties": {
                                        "x": {"type": "number"},
                                        "y": {"type": "number"},
                                        "z": {"type": "number"},
                                    },
                                    "required": ["x", "y", "z"],
                                    "additionalProperties": False,
                                },
                            },
                            "required": ["id", "display_name", "position"],
                            "additionalProperties": False,
                        },
                    },
                },
                "required": ["room_objects", "agent_placements"],
                "additionalProperties": False,
            },
        },
        "required": ["assistant_message", "analysis", "project", "agents", "knowledge", "placement_preview"],
        "additionalProperties": False,
    }
