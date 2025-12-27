\
from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


_WORD_RE = re.compile(r"[A-Za-zÄÖÜäöüß0-9_]+")


def _tokenize(text: str) -> List[str]:
    return [m.group(0).lower() for m in _WORD_RE.finditer(text)]


def _chunk_text(text: str, chunk_chars: int) -> List[str]:
    # Simple chunking: split by blank lines, then re-pack to target size.
    paragraphs = [p.strip() for p in re.split(r"\n\s*\n", text) if p.strip()]
    chunks: List[str] = []
    buf: List[str] = []
    buf_len = 0

    for p in paragraphs:
        if buf_len + len(p) + 2 <= chunk_chars:
            buf.append(p)
            buf_len += len(p) + 2
        else:
            if buf:
                chunks.append("\n\n".join(buf).strip())
            buf = [p]
            buf_len = len(p)

    if buf:
        chunks.append("\n\n".join(buf).strip())

    # Fallback if file has no paragraphs
    if not chunks and text.strip():
        chunks = [text.strip()[i:i + chunk_chars] for i in range(0, len(text.strip()), chunk_chars)]

    return chunks


@dataclass(frozen=True)
class KnowledgeChunk:
    tag: str
    file_path: str
    chunk_index: int
    text: str
    tokens: frozenset[str]


class KnowledgeBase:
    """
    Tiny local KB:
    - Files live under kb_root/<tag>/*.txt|*.md
    - At runtime we do simple keyword-overlap retrieval (no external deps).
    """

    def __init__(self, kb_root: Path, chunk_chars: int = 900):
        self.kb_root = kb_root
        self.chunk_chars = chunk_chars
        self._chunks: List[KnowledgeChunk] = []
        self._load()

    def summary(self) -> str:
        tags = sorted({c.tag for c in self._chunks})
        return f"{len(self._chunks)} chunks, {len(tags)} tags: {tags}"

    def _iter_files(self) -> Iterable[Tuple[str, Path]]:
        if not self.kb_root.exists():
            return
        for tag_dir in sorted([p for p in self.kb_root.iterdir() if p.is_dir()]):
            tag = tag_dir.name
            for fp in sorted(tag_dir.rglob("*")):
                if fp.is_file() and fp.suffix.lower() in {".txt", ".md"}:
                    yield tag, fp

    def _load(self) -> None:
        self._chunks.clear()
        if not self.kb_root.exists():
            return

        for tag, fp in self._iter_files():
            try:
                text = fp.read_text(encoding="utf-8", errors="ignore")
            except Exception:
                continue

            for i, chunk in enumerate(_chunk_text(text, self.chunk_chars)):
                toks = frozenset(_tokenize(chunk))
                if not toks:
                    continue
                self._chunks.append(
                    KnowledgeChunk(
                        tag=tag,
                        file_path=str(fp.relative_to(self.kb_root)),
                        chunk_index=i,
                        text=chunk,
                        tokens=toks,
                    )
                )

    def search(self, query: str, tags: Sequence[str], k: int = 4) -> List[Dict]:
        q_tokens = set(_tokenize(query))
        if not q_tokens:
            return []

        tag_set = set([t.strip() for t in tags if t.strip()])
        candidates = [c for c in self._chunks if (not tag_set or c.tag in tag_set)]
        if not candidates:
            return []

        scored: List[Tuple[float, KnowledgeChunk]] = []
        for c in candidates:
            inter = len(q_tokens & c.tokens)
            if inter == 0:
                continue
            # A tiny heuristic: overlap + small bonus for coverage
            score = inter + 0.25 * (inter / max(1, len(q_tokens)))
            scored.append((score, c))

        scored.sort(key=lambda x: x[0], reverse=True)
        top = scored[: max(0, k)]

        out: List[Dict] = []
        for score, c in top:
            out.append(
                {
                    "tag": c.tag,
                    "file": c.file_path,
                    "chunk_index": c.chunk_index,
                    "score": round(score, 4),
                    "text": c.text,
                }
            )
        return out
