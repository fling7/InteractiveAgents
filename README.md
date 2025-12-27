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

**QuickAgentManager: alle Einstellungen & Auswirkungen**

**Backend**
- **Backend Base Url**: Ziel-URL des Backends für `/setup`, `/projects`, `/chat`. Muss erreichbar sein.
- **Room Plan Path**: Pfad zum Room-Plan JSON (nur genutzt, wenn „Pfade“ gewählt ist).
- **Agents Path**: Pfad zur Agenten-JSON (nur genutzt, wenn „Pfade“ gewählt ist).

**Spawn**
- **Spawn Area**: Größe der Spawn-Box (X/Z) für zufällige Platzierung der Agentenwürfel; größere Werte verteilen die Agenten weiter.
- **Spawn Height**: Y-Position der Würfel.
- **Box Scale Range**: Zufälliger Größenbereich der Würfel (min/max).

**UI**
- **Show Ui**: Ein-/Ausblenden des Ingame-UI.
- **Ui Rect**: Position/Größe des UI-Containers (Screen-Koordinaten).

**Agent Visuals**
- **Active Agent Color**: Highlight-Farbe für den aktuell aktiven Agenten.
- **Active Agent Emission**: Emissionsstärke für den aktiven Agenten (falls Material Emission unterstützt).
- **Bubble Height**: Höhe der Chat-Bubbles über dem Agenten.
- **Bubble Duration**: Wie lange eine Bubble sichtbar bleibt.
- **Bubble Stagger**: Verzögerung zwischen mehreren Bubbles.
- **Handoff Delay**: Wartezeit zwischen Handoff-Hinweis und eigentlicher Antwort.
- **Handoff Indicator Duration**: Dauer des Handoff-Indikators (Bubble + Linie).
- **Handoff Line Width**: Linienbreite der Übergabe-Linie.

**Camera Movement**
- **Enable Free Movement**: Aktiviert freie Kamera (WASD/QE + rechte Maustaste).
- **Camera Move Speed**: Grundgeschwindigkeit der Kamera.
- **Camera Boost Multiplier**: Multiplikator bei gedrückter Shift-Taste.
- **Camera Look Speed**: Maus-Geschwindigkeit für Blickbewegung.
- **Camera Look Clamp**: Max. Pitch-Winkel (nach oben/unten).

**Runtime (nur Play-Mode)**
- **Session Id**: Aktuelle Session vom Backend; wird nach Setup gesetzt.
- **Active Agent Id**: Der ausgewählte Agent, an den Chats gesendet werden.

**UI-Workflow im Play-Modus**
- **Projekt/Pfade umschalten**: „Projekt“ nutzt die Projektliste aus `GET /projects`; „Pfade“ nutzt die Inspector-Pfade.
- **Projektliste laden**: Holt die Projekte vom Backend und aktualisiert die Auswahl.
- **Setup erneut vom Server**: Startet `POST /setup` (mit Projekt oder Pfaden).
- **Agenten wählen**: Setzt den aktiven Agenten (auch per Linksklick auf Box).
- **Chat senden**: Sendet `POST /chat` an den aktiven Agenten.

### Nutzung der zusätzlichen Skripte
Die folgenden Schritte zeigen, wie du die **neuen Skripte** in ein bestehendes Unity‑Projekt einbaust:

#### A) Projekte/Agenten/Wissen im Unity‑Editor pflegen (`ProjectManagerUI.cs`)
1. Kopiere `ProjectManagerUI.cs` nach `Assets/Scripts/` (oder `Assets/Editor/`).
2. Backend starten.
3. In Unity den Menüpunkt **Tools → Project Manager** öffnen.
4. Projekte, Agenten und Wissenseinträge im Editor anlegen/ändern, bevor du in den Play‑Modus gehst.

**Projekt-Manager Felder (genaue Bedeutung & Ausfüllhilfe)**
- **Backend Base Url**: Basis-URL des Python-Backends (z. B. `http://127.0.0.1:8787`). Muss erreichbar sein, sonst schlagen alle Requests fehl.
- **Projekte aktualisieren**: Lädt die Projektliste via `GET /projects`.

**Neues Projekt**
- **Name**: Anzeigename des Projekts (Pflicht). Wird im Editor und in der Projektliste verwendet.
- **ID (optional)**: Eindeutiger technischer Identifier. Leer lassen, um die ID vom Backend erzeugen zu lassen.
- **Beschreibung**: Freitext zur Dokumentation (optional).
- **Projekt erstellen**: Sendet die Felder an `POST /projects/create`.

**Vorhandene Projekte**
- **Laden**: Lädt ein Projekt samt Agenten und Wissen via `GET /projects/{project_id}`.

**Aktuelles Projekt**
- **Projektname**: Anzeigename des geladenen Projekts. Änderungen wirken sich auf die Projektliste aus.
- **Beschreibung**: Freitext zum Projekt.
- **Metadaten speichern**: Speichert Name/Beschreibung via `POST /projects/{project_id}/metadata`.

**Agenten**
- **Agent hinzufügen**: Legt einen neuen Agenten in der Liste an (erst nach „Agenten speichern“ im Backend).
- **ID**: Eindeutiger Agent-Identifier. Muss innerhalb des Projekts eindeutig sein (z. B. `agent_sales`).
- **Name**: Anzeigename im UI/Debug (z. B. „Sven“).
- **Persona**: Rollen-/Charakterbeschreibung für das Modell. Hier stehen Tonalität, Aufgabe und Regeln des Agenten.
- **Expertise (CSV)**: Liste von Schlagworten, kommasepariert (z. B. `Preise, Rabatte`). Dient als interne Beschreibung der Kompetenzen.
- **Wissen-Tags (CSV)**: Liste von Tags, die auf lokale Wissensordner referenzieren (z. B. `pricing, tech`). Muss zu `kb/<tag>/...` passen.
- **Bevorzugte Zonen (CSV)**: IDs von Zonen/Areas, die beim Spawn bevorzugt werden (wenn das Room-Plan Zonen definiert).
- **Bevorzugte Spawn-Tags (CSV)**: Tags von Spawnpoints/Zonen, die bevorzugt werden (nur wirksam, wenn im Room-Plan/Spawnpoints Tags vorhanden sind).
- **Agenten speichern**: Schreibt die gesamte Agentenliste via `POST /projects/{project_id}/agents`.

**Wissensdatenbank**
- **Tag**: Ordner-Schlüssel der Wissensgruppe (z. B. `pricing`). Entspricht `kb/<tag>/...`.
- **Name**: Dateiname/Identifier innerhalb des Tags (z. B. `preisliste`). Zusammen mit Tag wird daraus der Wissenseintrag.
- **Text**: Inhalt des Wissenseintrags als reiner Text/Markdown.
- **Wissen speichern**: Speichert/überschreibt den Eintrag via `POST /projects/{project_id}/knowledge` (action `upsert`).
- **Felder leeren**: Leert die Eingabefelder.
- **Vorhandenes Wissen → Laden**: Lädt den Text eines Eintrags via `POST /projects/{project_id}/knowledge/read`.
- **Vorhandenes Wissen → Löschen**: Löscht den Eintrag via `POST /projects/{project_id}/knowledge` (action `delete`).

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
