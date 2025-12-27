# Produktdetails (product)

## Kernfunktionen
- Agent-Definition per JSON (Persona, Expertise, Knowledge-Tags)
- Spawn-Placement über Zonen und Tags
- Handoff-Mechanik zwischen Agenten
- Lokale Wissensbasis (kb/*) mit Keyword-Suche

## Typische Use-Cases
- Messe-Demos mit Expert:innen (Sales/Tech/Marketing)
- Virtuelle Showrooms mit mehreren Stationen
- Onboarding-Kioske für interne Tools

## Grenzen (Transparenz)
- Keine Vektor-DB integriert (Keyword-Suche, schnell & simpel)
- LLM-Antworten benötigen einen gültigen OpenAI API Key

## Technische Voraussetzungen (kurz)
- Python 3.10+ (nur Standardbibliothek)
- Unity WebRequest/HttpClient im Frontend
