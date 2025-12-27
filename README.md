# OpenAI Expert-NPCs (PyCharm „Play“-friendly)

Dieses Projekt ist ein **minimalistisches Python-Backend (nur Standardbibliothek)**, das in Unity als „NPC‑Agenten‑Server“ dienen kann.

Ziele:
- Du definierst **beliebige** Agenten (Name/Persona/Expertise/Wissens‑Tags) – **keine fest verdrahteten Rollen** wie „Marketing“.
- Das Backend platziert Agenten an **Spawnpoints** (oder erzeugt Default‑Positionen).
- Während des Chats kann ein Agent – wenn sein Wissen nicht reicht – den Nutzer **an einen besseren Agenten weiterleiten** (Handoff).

> ⚠️ Hinweis: Ohne OpenAI‑API‑Key kann natürlich keine Modell‑Antwort generiert werden.  
> Du musst den Key **einmalig** in `config.json` eintragen oder beim ersten Start in die Konsole einfügen.

---

## 1) Start in PyCharm

1. Projektordner in PyCharm öffnen.
2. Datei **`main.py`** starten (Play).
3. Beim ersten Start:
   - Wenn `openai_api_key` in `config.json` leer ist, fragt das Backend im Terminal nach dem Key und speichert ihn.
4. Beim Start kannst du auswählen, ob du **Beispiel-Daten** oder **eigene Dateien** laden möchtest.

Server läuft dann standardmäßig auf:
- `http://127.0.0.1:8787`

Teste mit:
- `GET /health`
- `POST /setup` ohne Payload nutzt die beim Start gewählten Pfade (oder die Beispiel-Daten).

---

## 2) Agenten frei definieren

Siehe `examples/agents.example.json`:

```json
{
  "agents": [
    {
      "id": "agent_sales",
      "display_name": "Sven",
      "persona": "Du bist Sven, Sales Lead ...",
      "expertise": ["Preise", "Rabatte"],
      "knowledge_tags": ["common", "pricing"]
    }
  ]
}
```

Wichtig:
- `id` muss eindeutig sein.
- `persona` ist die Charakter‑/Rollenanweisung.
- `expertise` ist frei (Liste oder du nutzt einfach Freitext).
- `knowledge_tags` referenziert Ordner in `kb/<tag>/...`.

---

## 3) Wissensbasis (lokal, ohne Extras)

Lege Dateien in `kb/` ab, z.B.
- `kb/pricing/preise.md`
- `kb/tech/integration.txt`

Dann in Agenten:
```json
"knowledge_tags": ["pricing"]
```

Das Backend macht eine **einfache Keyword‑Suche** in den lokalen Texten und gibt relevante Snippets an das Modell.

---

## 4) API-Endpunkte (Unity → Python)

### POST `/setup`
Du kannst entweder direkt JSON senden oder per Pfad die Beispiel-Dateien laden:

Beispiel (wie im PyCharm HTTP Client `examples/demo_requests.http`):
```json
{
  "room_plan_path": "examples/room_plan.example.json",
  "agents_path": "examples/agents.example.json"
}
```

Antwort:
- `session_id`
- Liste der Agenten mit Position/Forward

### POST `/chat`
```json
{
  "session_id": "...",
  "active_agent_id": "agent_tech",
  "user_text": "Was kostet euer Produkt?"
}
```

Antwort enthält eine `events`‑Liste, z.B. bei Handoff:
```json
{
  "active_agent_id": "agent_sales",
  "events": [
    {"type":"say","agent_id":"agent_tech","text":"Dazu ist Sven unser Sales‑Lead ..."},
    {"type":"say","agent_id":"agent_sales","text":"Die Preise sind ..."}
  ]
}
```

---

## 5) Unity-Skripte

Unter `unity_scripts/` liegen **Beispiel‑C#‑Skripte** (kein komplettes Unity‑Projekt).
- `BackendClient.cs`: REST‑Calls zu `/setup` und `/chat`
- `AgentSpawnPoint.cs`: markiert Spawnpoints im Raum (optional)

Du kannst diese Dateien 1:1 in dein Unity‑Projekt kopieren und anpassen.

---

## Konfiguration

`config.json` (Default):
- `model`: Standard ist `gpt-4.1` (kannst du ändern)
- `temperature`: Standard 0.3
- `server_port`: Standard 8787

---

## Sicherheitsnotiz

Bitte `config.json` **nicht** in öffentliche Repos committen, wenn dort ein API‑Key drin steht.
