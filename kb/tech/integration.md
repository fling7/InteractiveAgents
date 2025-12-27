# Technik & Integration (tech)

## Architektur
- Python-HTTP-Server (Standardbibliothek)
- Unity-Client via REST (`/setup`, `/chat`)
- Strukturierte JSON-Ausgaben (Schema)

## Integrationen
- Unity: WebRequest/HttpClient für Setup und Chat
- Externe Systeme: via JSON-Bridge möglich

## Betrieb & Sicherheit
- Kein DB-Setup notwendig
- API Key in `config.json` oder via `OPENAI_API_KEY`
- CORS ist für WebGL aktiviert

## Performance-Notizen
- KB-Suche über Keywords, schnell für kleine/mittlere Datenmengen
- Mehrere Agenten pro Session möglich
