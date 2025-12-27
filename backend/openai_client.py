\
from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Tuple


class OpenAIHTTPError(RuntimeError):
    def __init__(self, status: int, message: str, details: Optional[Dict[str, Any]] = None):
        super().__init__(message)
        self.status = status
        self.details = details or {}


def _extract_output_text(resp: Dict[str, Any]) -> str:
    parts: List[str] = []
    for item in resp.get("output", []) or []:
        if item.get("type") != "message":
            continue
        for c in item.get("content", []) or []:
            if c.get("type") == "output_text":
                parts.append(str(c.get("text", "")))
    return "\n".join([p for p in parts if p]).strip()


@dataclass
class OpenAIResponsesClient:
    api_key: str
    base_url: str = "https://api.openai.com/v1/responses"
    timeout_seconds: int = 60

    def create(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        body = json.dumps(payload).encode("utf-8")

        req = urllib.request.Request(
            self.base_url,
            data=body,
            method="POST",
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
            },
        )

        try:
            with urllib.request.urlopen(req, timeout=self.timeout_seconds) as resp:
                raw = resp.read().decode("utf-8", errors="replace")
                return json.loads(raw)
        except urllib.error.HTTPError as e:
            raw = e.read().decode("utf-8", errors="replace")
            try:
                details = json.loads(raw)
            except Exception:
                details = {"raw": raw}
            raise OpenAIHTTPError(e.code, f"OpenAI HTTP {e.code}", details=details) from e
        except urllib.error.URLError as e:
            raise OpenAIHTTPError(0, f"OpenAI connection error: {e}") from e

    def create_structured_json(
        self,
        *,
        model: str,
        input_messages: List[Dict[str, Any]],
        schema: Dict[str, Any],
        schema_name: str,
        temperature: float = 0.3,
        max_output_tokens: Optional[int] = None,
        reasoning: Optional[Dict[str, Any]] = None,
    ) -> Tuple[Dict[str, Any], Dict[str, Any], str]:
        payload: Dict[str, Any] = {
            "model": model,
            "input": input_messages,
            "temperature": temperature,
            "text": {
                "format": {
                    "type": "json_schema",
                    "name": schema_name,
                    "schema": schema,
                    "strict": True,
                }
            },
        }
        if max_output_tokens is not None:
            payload["max_output_tokens"] = int(max_output_tokens)
        if reasoning is not None:
            payload["reasoning"] = reasoning

        resp = self.create(payload)
        out_text = _extract_output_text(resp)
        try:
            parsed = json.loads(out_text) if out_text else {}
        except json.JSONDecodeError:
            parsed = {"_parse_error": True, "_raw_text": out_text}
        return parsed, resp, out_text


    def create_json_object(
        self,
        *,
        model: str,
        input_messages: List[Dict[str, Any]],
        temperature: float = 0.3,
        max_output_tokens: Optional[int] = None,
        reasoning: Optional[Dict[str, Any]] = None,
    ) -> Tuple[Dict[str, Any], Dict[str, Any], str]:
        """
        Older JSON mode: ensures the output is valid JSON, but not schema-validated.
        Useful as a fallback if json_schema is not supported by the chosen model.
        """
        payload: Dict[str, Any] = {
            "model": model,
            "input": input_messages,
            "temperature": temperature,
            "text": {"format": {"type": "json_object"}},
        }
        if max_output_tokens is not None:
            payload["max_output_tokens"] = int(max_output_tokens)
        if reasoning is not None:
            payload["reasoning"] = reasoning

        resp = self.create(payload)
        out_text = _extract_output_text(resp)
        try:
            parsed = json.loads(out_text) if out_text else {}
        except json.JSONDecodeError:
            parsed = {"_parse_error": True, "_raw_text": out_text}
        return parsed, resp, out_text
