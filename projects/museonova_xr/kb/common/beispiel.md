# MuseoNova XR Guide – Überblick (common)

## Produktkurzbeschreibung
MuseoNova XR Guide ist ein Unity-basiertes System für **interaktive Museumsführungen**: Besucher:innen sprechen mit virtuellen Guides (NPCs), die je nach Raum/Zone andere Rollen haben (Kuratorik, Ticketing, Kinderführung, Technik, Datenschutz). Inhalte kommen aus einer **lokalen Wissensbasis** (`kb/*`) und können ohne Programmierung erweitert werden.

MuseoNova unterstützt:
- Mehrere Expert:innen-Agents mit klaren Rollen & Persönlichkeit
- Raum-/Zonen-Placement (z. B. Foyer, Galerie, Kinder-Ecke)
- Handoffs zwischen Agents ("Dafür ist Svenja zuständig …")
- Lokale Wissensbasis per Textdateien (Versionierbar in Git)

## Zielkunden
- Museen & Kulturinstitutionen (Dauerausstellung oder Sonderausstellung)
- Science Center & Erlebniswelten (XR-Stationen / Kioske)
- Stadtmarketing/Tourismus (pop-up Installationen)
- Agenturen, die Unity-Showrooms für Kunden bauen

## FAQ (Kurz)
- **Brauche ich Internet?** Für Live-LLM-Antworten ja (API-Key). Inhalte/KB liegen lokal; ein "Info-Modus" kann auch ohne externen Call betrieben werden (siehe Technik).
- **Welche Inhalte können wir hinterlegen?** Ausstellungstexte, Objektbeschreibungen, Preis-/Ticketlogik, Hausregeln, Datenschutzinfos, technische Checks.
- **Wie viele Agents sind sinnvoll?** Für kleine Demos 3–5, für echte Ausstellungen 6–12 (nach Zonen & Zielgruppen).
- **Mehrsprachig?** Ja – empfehlenswert ist pro Sprache ein eigener KB-Ordner oder klare Sprach-Sektionierung.
