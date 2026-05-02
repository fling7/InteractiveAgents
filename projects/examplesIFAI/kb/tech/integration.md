# Integration des Agenten-Sets (tech)

## Zweck
Dieses Projekt ist eine „Wissens- und Agenten-Konfiguration“. Es kann z. B. in eine Unity-/XR-Szene, eine Web-Demo oder ein internes Portal eingebunden werden.

## Agentenpositionen
In `agents.json` hat jeder Agent eine feste `position` (x,y,z). Das ist als Default-Placement gedacht (z. B. Kreis um einen zentralen Empfang).

## Wissens-Tags (Routing)
Jeder Agent hat `knowledge_tags`. Nutze sie, um:
- passende KB-Dateien zu filtern,
- Antworten auf die richtige Domäne zu fokussieren,
- Handoffs sinnvoll zu machen („Dafür ist Agent X zuständig“).

Empfohlene Tag-Gruppierung:
- `research/*`: Forschung, Methoden, Datenquellen
- `transfer/*` + `product/*`: Produkte, Transferlogik, Angebote
- `study/*`: Studierendenmitwirkung
- `network/*`: Austausch, Kooperation, Wissenschaftskommunikation
- `contact/*`: Team, Anfahrt, organisatorische Fragen

## Minimaler Ablauf (konzeptuell)
1) Nutzer fragt etwas.
2) Routing: (a) Direkt an passenden Agenten oder (b) Start beim „IFAI Empfang“.
3) Agent antwortet mit KB-Kontext.
4) Wenn nötig: Handoff an zuständigen Agenten.

Quelle (inhaltliche Basis): https://www.th-nuernberg.de/einrichtungen-gesamt/in-institute/institut-fuer-angewandte-informatik-ifai/ifai-das-institut-fuer-angewandte-informatik/
