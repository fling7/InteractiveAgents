# Technik & Integration (tech)

## Architektur (Referenz)
- Unity-Client kommuniziert per REST mit einem Python-HTTP-Server
- Endpoints: `/setup` (Agents/Zonen), `/chat` (Nachrichten)
- Strukturierte JSON-Antworten (für Animationen, UI-Hints, Handoffs)

## Deployment-Varianten
- **Kiosk/PC**: lokaler Python-Service + Unity-App (Windows/Linux)
- **VR**: gleicher Stack, optimiert für geringe Latenz
- **WebGL**: Server extern, CORS aktiviert, Tokens serverseitig

## Integrationen
- Ticketing: Preisabfrage / Öffnungszeiten via JSON-Bridge (Webhook/Proxy)
- CMS: KB-Dateien aus Git oder CMS-Export (Build-Step)
- Analytics: Event-Export (CSV/JSON) oder Weiterleitung an euer BI

## Betrieb & Sicherheit
- Kein DB-Setup notwendig (optional: Logs/Exports)
- API-Key via `config.json` oder `OPENAI_API_KEY` (serverseitig!)
- Empfohlen: Rate-Limits, IP-Allowlist im Intranet, getrennte Keys pro Standort

## Performance-Notizen
- Keyword-Suche: sehr schnell bei kleinen/mittleren KBs
- Für große Wissensbasen: KB in Themen splitten, harte Zonenfilter nutzen
- Mehrere Agents pro Session möglich; pro Agent separate System-Persona
