# KI-Einstieg

HealLink verbindet zwei Quest 3 im lokalen WLAN. Ein Trainee gibt seinen tatsächlich angezeigten Headset-Blick per MediaProjection frei, ein Expert empfängt diesen Blick und sendet Handgelenkpositionen zurück. Beide sprechen über einen PCM-Prototyp. Der Expert soll dem Trainee-Blick folgen.

Lesereihenfolge ist [INDEX](INDEX.md), [Funktionskarte](FEATURE_MAP.md), benötigte Codeabschnitte. [MULTIPLAYER](MULTIPLAYER.md) enthält Bedienung und Protokoll, [TESTING](TESTING.md) den belegten Prüfstand.

Wichtige Entscheidungen:

- Quelle ist die System-Bildschirmfreigabe, nicht die rohe PassthroughCameraAccess-Textur und nicht eine Tiefenrekonstruktion.
- Der Expert sieht einen monokularen Stream mit unverändertem Seitenverhältnis und 1,5 m virtuellem Betrachtungsabstand.
- Beide Geräte benötigen denselben neuen Build. Discovery bewirbt headsetViewVersion = 1.
- UDP-Kopplung, Handprotokoll und Mikrofonkanal bleiben vorhanden. Der alte Point-Cloud-Modus ist standardmäßig aus.
- Puffer sind auf einen Frame begrenzt; alte Zielobjekte dürfen nach einem Partnerwechsel keine Frames veröffentlichen.
- Die Systemfreigabe ist ausschließlich eine Benutzeraktion im Headset.
- Keine Geräteabnahme aus einem erfolgreichen Build ableiten.

Git wurde am 11.11.2026 lokal initialisiert, weil nur ein unvollständiger .git/objects-Ordner vorhanden war. Der erste Commit erhält den damaligen Gesamtstand. Ein Remote ist nicht eingerichtet. Die Datei PROJEKTPILOT.md gehörte bereits zum Arbeitsverzeichnis und wurde nicht in diesen Änderungssatz aufgenommen.
