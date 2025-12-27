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
- `QuickAgentManager.cs`: **alles‑in‑einem** Script für ein leeres Unity‑Projekt (GUI, Setup, Spawning, Chat)
- `AgentManagerExample.cs`: Beispiel‑Controller, der `BackendClient` nutzt und Agenten spawnt
- `ProjectManagerUI.cs`: Editor‑Fenster für Projekte/Agenten/Wissen (Unity‑Menüpunkt)
- `RoomPlanExporter.cs`: exportiert Spawnpoints aus der Szene als `room_plan.json`

Du kannst diese Dateien 1:1 in dein Unity‑Projekt kopieren und anpassen.

### Quick Start (leeres Unity‑Projekt)
1. **Backend starten** (siehe Abschnitt „Start in PyCharm“). Server läuft auf `http://127.0.0.1:8787`.
2. Neues Unity‑Projekt erstellen (3D).
3. In Unity unter `Assets/` ein neues C#‑Script anlegen und **Inhalt von** `unity_scripts/QuickAgentManager.cs` **einfügen**.
4. Script auf ein leeres GameObject ziehen (z.B. `AgentManager`).
5. **Play** drücken.

**Was passiert dann?**
- Das Script ruft automatisch `POST /setup` auf, um die Agentenanzahl zu ermitteln.
- Für jeden Agenten wird eine **zufällige Box** in der Szene erstellt.
- Im **Play‑Modus** kannst du die Agenten auswählen (Linksklick auf Box oder per UI‑Liste).
- Über das UI kannst du Chat‑Nachrichten senden.

**Wichtige Felder im Inspector**
- `Backend Base Url`: URL des Backends (Standard `http://127.0.0.1:8787`).
- `Room Plan Path` / `Agents Path`: Pfade, die der Server kennt (Standard nutzt die Beispiel‑Dateien aus diesem Repo).
- `Spawn Area`: Bereich, in dem die Boxen zufällig platziert werden.

### Nutzung der zusätzlichen Skripte
Die folgenden Schritte zeigen, wie du die **neuen Skripte** in ein bestehendes Unity‑Projekt einbaust:

#### A) Projekte/Agenten/Wissen im Unity‑Editor pflegen (`ProjectManagerUI.cs`)
1. Kopiere `ProjectManagerUI.cs` nach `Assets/Scripts/` (oder `Assets/Editor/`).
2. Backend starten.
3. In Unity den Menüpunkt **Tools → Project Manager** öffnen.
4. Projekte, Agenten und Wissenseinträge im Editor anlegen/ändern, bevor du in den Play‑Modus gehst.

#### B) Spawnpoints aus der Szene exportieren (`AgentSpawnPoint.cs` + `RoomPlanExporter.cs`)
1. Platziere leere GameObjects als Spawnpoints in deiner Szene.
2. Hänge **`AgentSpawnPoint`** an jedes Spawnpoint‑Objekt (damit es erkannt wird).
3. Hänge **`RoomPlanExporter`** an ein leeres GameObject (z.B. `RoomPlanExporter`).
4. Im Inspector den Export‑Pfad setzen (z.B. `Assets/room_plan.json`) und auf **Export** klicken.
5. Die erzeugte `room_plan.json` kannst du beim Backend‑Setup verwenden:
   ```json
   { "room_plan_path": "Assets/room_plan.json" }
   ```

---

## Konfiguration

`config.json` (Default):
- `model`: Standard ist `gpt-4.1` (kannst du ändern)
- `temperature`: Standard 0.3
- `server_port`: Standard 8787

---

## Sicherheitsnotiz

Bitte `config.json` **nicht** in öffentliche Repos committen, wenn dort ein API‑Key drin steht.
