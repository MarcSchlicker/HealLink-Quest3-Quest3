# Änderungsverlauf

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
