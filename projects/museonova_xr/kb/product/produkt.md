# Produktdetails (product)

## Kernfunktionen
- Agent-Definition per JSON (Persona, Expertise, Knowledge-Tags)
- Zonenbasiertes Placement (Foyer, Galerie, Kinder-Ecke …)
- Handoff-Mechanik zwischen Agents ("Ich verbinde dich mit …")
- Lokale Wissensbasis (`kb/*`) mit schneller Keyword-Suche
- Rollenmodus: Besucherführung vs. Staff-Mode (für interne Fragen)

## Typische Use-Cases
- Interaktive Führung in Sonderausstellungen (Pop-up)
- Dauerhafte XR-Stationen mit wechselnden Themen
- Schulklassen-Modus (Rätsel, kurze Antworten, einfache Sprache)
- Sponsor-Lounge: Impact-Story + Kennzahlen (Verweildauer/Interaktionen)

## Grenzen (Transparenz)
- KB-Suche ist keyword-basiert (keine eingebaute Vektor-DB)
- Ohne externe LLM-Anbindung ist der "Info-Modus" begrenzt (FAQ/KB-Antworten)
- Inhalte müssen redaktionell gepflegt werden (Qualität = KB-Qualität)

## Qualitäts-Tipps für eure KB
- Pro Datei: 1 Thema, klare Überschriften, kurze Bullet-Listen
- Zahlen/Preise immer mit Einheit und Kontext ("pro Standort", "pro Monat")
- Einmal pro Release: "KB-Lint" (Widersprüche, veraltete Preise, Schreibweisen)
