# Änderungsverlauf

## 0.3.0 — 24.11.2026

Der optionale Internetmodus verbindet Trainee und Expert über einen selbst gehosteten WSS-Relay. Damit können die Quests unterschiedliche WLANs oder Hotspots nutzen, etwa in Lissabon und Berlin. Übertragen werden die freigegebene Trainee-Headsetansicht, Expert-Handgelenke und Sprache in beide Richtungen. Der LAN-Modus bleibt verfügbar.

Der Server liegt mit Docker-Konfiguration und Einrichtung in [Server/README.md](../Server/README.md). Beide Quests benötigen dieselbe Serveradresse, denselben Raumcode und Zugangsschlüssel in ihrer lokalen relay.json. Zugangsdaten sind nicht im Repository enthalten. Ein öffentlich erreichbarer Server ist noch nicht eingerichtet.

Sechs lokale Relay-Tests einschließlich des tatsächlichen C#-Clients, die Kopplungssimulationen, TCP-Transporttests und die Compose-Konfigurationsprüfung bestanden. Der abschließende Android-/IL2CPP-Build mit Unity 6000.6.7f1 war erfolgreich. Containerbetrieb, Android-WSS-Lauf und die Verbindung zwischen den beiden Standorten sind noch nicht praktisch abgenommen. Diese APK wurde noch nicht auf den Quests installiert.

- Quell-Commit: b8ef1d1c6f6bc0b791cc81a1cd2d8792d5d28dd1
- Artefakt: Builds/HealLink-Multiplayer-0.3.0.apk, außerhalb von Git
- Paketkennung: de.heallink.questquest
- Version: 0.3.0, Android-Versioncode 3, ARM64
- SHA-256: fbb4f241ea75d88c0e4b2257c3266f210a31b6940ec3682a81442b9603214118

## Öffentlicher Nachfolger — 24.11.2026

Das Repository HealLink-Quest3-Quest3 führt die Quest-zu-Quest-Version als öffentlichen Nachfolger der bisherigen HoloLens-/Quest-Repositories weiter. Die bestehende lokale Commit-Historie wird übernommen. README und Dokumentation verweisen auf die Vorgänger.

Multiplayer 0.2.0 wurde am 24.11.2026 auf beiden angeschlossenen Quest-3-Geräten installiert. ADB bestätigte jeweils Versioncode 2 und Versionname 0.2.0. Dies bestätigt die Installation, keine vollständige Funktionsabnahme mit zwei getragenen Headsets.

## 0.2.0 — 11.11.2026

### Multiplayer

- Die Headset-Bildschirmfreigabe aus dem Solo-Test ist in Trainee und Expert integriert.
- Die Rollen werden mit Handlaser und neuem Pinch gewählt.
- Discovery bewirbt die Version der Bildübertragung und deren Port. Alte Partner erhalten einen Aktualisierungshinweis.
- HQV1 rahmt JPEG-Bilder mit Größenlimit und Heartbeats. Der Anwendungspuffer enthält höchstens einen Frame pro Richtung.
- Sitzungswechsel berücksichtigen zusätzlich die Instanzkennung des Partners, auch bei gleicher IP und gleichem Port.
- Nach drei Sekunden ohne aktuelles Bild wird die Wiedergabe ausgeblendet.
- Lokale Expert-Handmodelle und zusätzliche Statusanzeigen überlagern das Livebild nicht. Die Gelenkübertragung zum Trainee bleibt aktiv.
- Der vorherige Point-Cloud-Modus startet standardmäßig nicht mehr. Seine Zeichenwerkzeuge sind nur im expliziten Legacy-Modus verfügbar.
- B führt in die Rollenauswahl zurück.

### Verifikation

Bestanden sind die 100 Kopplungssimulationen, die Tests des tatsächlichen TCP-Rahmungsquelltexts einschließlich lokaler Socket-Verbindung sowie die Prüfung der Dokumentationslinks und Szenenverweise.

Die zwei getragenen Headsets, Bildschirmfreigabe, Sichtausrichtung, Audioqualität, Handdarstellung und Wiederverbindung müssen mit diesem Build noch praktisch abgenommen werden. Der abschließende Android-Build mit Unity 6000.6.7f1 war erfolgreich. Artefakt ist Builds/HealLink-Multiplayer-0.2.0.apk. APKs bleiben außerhalb des Git-Verlaufs.

- Quell-Commit: f3643f274e4ad76457cdec07b746072bfe0de2e2
- Paketkennung: de.heallink.questquest
- Version: 0.2.0, Android-Versioncode 2
- SHA-256: 493e6321e1d50cb27dc791ad7caea540e5b74168f049126e220e70b696858e73
- Die neue Multiplayer-APK wurde in diesem Arbeitsschritt noch nicht auf den Headsets installiert.
