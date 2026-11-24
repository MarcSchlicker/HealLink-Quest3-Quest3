# Multiplayer mit dem Blick des Trainees

## Internet zwischen zwei Standorten

Ab Version 0.3.0 gibt es einen optionalen Relay-Modus für getrennte Netze, etwa Lissabon und Berlin. Beide Quests verbinden sich ausgehend über WSS mit einem Uni-Server und derselben Sitzungskennung. Dieser Modus verwendet weder UDP-Broadcast noch direkte Geräteverbindungen. Im Relay-Modus ist eine Sitzung auf einen Trainee und einen Expert begrenzt.

Der Dienst, Docker-Konfiguration und die Einrichtung der Quests sind unter [Server/README.md](../Server/README.md) beschrieben. Ohne aktivierte Relay-Konfiguration bleibt der bisherige LAN-Modus aktiv. Ein Eintrag in relay.json im persistentDataPath hat Vorrang vor den leeren Vorgaben in Assets/Resources/HealLinkRelay.json. Die Konfiguration wird beim Öffnen einer Rollen-Szene gelesen.

Der Serverzugang wird durch ein geheimes Zugriffstoken geschützt. TLS schützt beide Internetverbindungen; der Server verarbeitet die Medien und ist kein Ende-zu-Ende-verschlüsselter Transport. Zertifikatsfehler werden nicht umgangen. Außerhalb des Editors werden ausschließlich wss-Adressen akzeptiert.

QuestRelayTransport multiplexiert Video, Hände und PCM-Audio. Pro Richtung gibt es einen aktuellen Video- und Hand-Frame sowie höchstens 16 Audio-Pakete. Jedem Binärpaket sind ein Kanalbyte und die 16-Byte-Sitzungskennung vorangestellt. Der Server verwirft Pakete alter Sitzungen und Bilder aus der falschen Rolle. Sprachdaten werden in beide Richtungen übertragen. Neue Partner erhalten eine neue Sitzungskennung und leere Puffer.

Bei Abbruch verbindet sich die App erneut mit dem Server. Nach erneuter Kopplung muss der Trainee die Bildschirmfreigabe gegebenenfalls neu bestätigen. Room-Code, Rolle und Serveradresse müssen auf beiden Seiten zueinander passen.

## Bedienung im LAN

1. Dieselbe Multiplayer-Version auf beiden Quest-3-Geräten installieren.
2. Beide Geräte mit einem WLAN verbinden, das lokale Gerätekommunikation erlaubt.
3. Im Menü mit dem sichtbaren Handlaser auf eine Rolle zeigen, die Finger öffnen und neu pinchen.
4. Ein Gerät als Trainee, eines als Expert starten. Die automatische Suche koppelt unterschiedliche Rollen.
5. Mikrofon auf beiden Geräten erlauben. Der Trainee bestätigt zusätzlich die Systemabfrage zur Bildschirmfreigabe.
6. Beim Expert erscheint der Blick des Trainees einschließlich Passthrough, Trainee-Händen und eingeblendeter App-Oberfläche.
7. Nach abgelehnter Freigabe kann der Trainee mit A erneut anfragen. B führt zurück zur Rollenauswahl.

Beide Rollen müssen das Update bekommen. Bei einem alten Partner meldet die App, dass die andere Quest aktualisiert werden muss.

## Was übertragen wird

Die Quelle ist die vom Quest-System ausgegebene Headset-Ansicht über Android MediaProjection. Die Blickrichtung folgt dem Trainee. Der Expert sieht einen monokularen Videoausschnitt, auf beide Augen dargestellt. Es entsteht keine unabhängig begehbare Kopie des Raums.

Die Wiedergabe sitzt kopffest in 1,5 m virtueller Entfernung. Das Seitenverhältnis bleibt erhalten. Während eines gültigen Livebildes werden die zusätzliche Expert-Statusanzeige und lokale Expert-Handmodelle ausgeblendet. Die Hände werden weiterhin getrackt und als Gelenkskelett zum Trainee gesendet. Der Expert sieht sie dadurch im zurückkommenden Trainee-Bild mit der Übertragungslatenz. Eine räumliche Kalibrierung der Räume existiert nicht.

Die Trainee-Statusanzeige reduziert sich während der Freigabe auf eine Zeile am Rand. Alles, was der Trainee tatsächlich eingeblendet sieht, kann Teil des übertragenen Bildes sein.

## Komponenten und Ablauf

- [QuestQuestApp](../Assets/Scripts/QuestQuestApp.cs) lädt die Rollen-Szenen, startet Discovery, Hände und Sprachchat und verwaltet die Statusanzeige.
- QuestDiscovery in derselben Datei bewirbt Rolle, Handunterstützung, headsetViewVersion und headsetViewPort. Die existierende zufällige, gegenseitige Experten-/Trainee-Kopplung bleibt erhalten.
- [HeadsetViewCapture](../Assets/Scripts/HeadsetViewCapture.cs) verbindet Unity mit dem nativen Bildschirmdienst und stellt JPEG-Bilder dar. Dieselbe Komponente wird im Solo-Test verwendet.
- [CapturePermissionActivity](../Assets/Plugins/Android/HeadsetCapture/CapturePermissionActivity.java) öffnet die Systemfreigabe. Die Zustimmung muss im Headset erfolgen.
- [HeadsetCaptureService](../Assets/Plugins/Android/HeadsetCapture/HeadsetCaptureService.java) führt den MediaProjection-Foreground-Service aus. Ein ImageReader erhält die Compositor-Ausgabe. Zeilenabstände der Pixel werden berücksichtigt. Aktuell entstehen JPEG-Bilder mit 960 × 720 Pixeln, Qualität 70 und maximal etwa 10 Bildern pro Sekunde.
- [QuestCameraStream](../Assets/Scripts/QuestCameraStream.cs) verbindet den Trainee mit dem TCP-Empfänger des Experts. Der Empfänger nimmt nur die gekoppelte IP-Adresse an.
- [HeadsetFrameProtocol](../Assets/Scripts/HeadsetFrameProtocol.cs) definiert die TCP-Rahmung.
- QuestVoiceChat und QuestAnnotationEndpoint in QuestQuestApp übertragen Mikrofon-Audio und Expert-Handgelenke.

Der normale Build verwendet die gespeicherten Szenen [QuestQuest](../Assets/Scenes/QuestQuest.unity), [Trainee](../Assets/Scenes/Trainee.unity) und [Expert](../Assets/Scenes/Expert.unity). Die getrennten SoloTest-Szenen bleiben für einen Ein-Brillen-Test verfügbar.

## Netzwerkvertrag

| Kanal | Standard | Inhalt |
| --- | --- | --- |
| Discovery | UDP 5099 | Rolle, Sitzungsziel, Ports und Fähigkeiten |
| Expert-Hände / Annotationen | UDP 5089 | Gelenkpositionen beziehungsweise bestehende Strichpakete |
| Sprache | UDP 5090 | PCM, mono, 16 kHz |
| Trainee-Ansicht | TCP 5091 | HQV1, anschließend JPEG-Frames |
| Frühere Tiefenübertragung | UDP 5088 | Standardmäßig deaktiviert |

TCP beginnt mit den vier ASCII-Bytes HQV1. Jedes Paket enthält eine vorzeichenlose Vier-Byte-Länge in Netzwerkreihenfolge und anschließend die JPEG-Bytes. Null ist ein Heartbeat während der Freigabeabfrage. Die Obergrenze ist 512 KiB. Anfang und Ende der JPEG-Rahmung werden geprüft. Pro Richtung wartet höchstens ein vollständiger Frame im Anwendungspuffer.

Bei einem Partnerwechsel erhalten Verbindung und Puffer ein neues Zielobjekt. Ein vollständig gelesener Frame einer alten Verbindung wird dadurch nicht in eine neue Sitzung übernommen. Bei Verbindungsverlust wird wieder verbunden, ein Bild älter als drei Sekunden ausgeblendet. Beim Verlassen der Szene enden Socket, Listener und Bildschirmfreigabe. Die Systemfreigabe kann jederzeit vom Headset beendet werden.

## Grenzen

Im LAN-Modus ist die IP-Prüfung keine Authentifizierung. Dort sind Bild, Hände und Sprache unverschlüsselt und für ein vertrautes Test-WLAN vorgesehen. Die Sprache ist ein PCM-Prototyp ohne Echo-Unterdrückung oder WebRTC. Reale Latenz, Audioqualität und Kopplung müssen mit zwei Geräten geprüft werden.

Die frühere Tiefen-/Point-Cloud-Verarbeitung wird nur noch bei explizit gesetztem enableLegacyDepthTools gestartet. Sie ist im normalen Multiplayer standardmäßig aus. Das alte punktwolkenbasierte Zeichnen gehört deshalb nicht zum aktuellen Standardablauf. Die grundlegende Annotationen-Schnittstelle bleibt im Code erhalten.

## Quellen

- [Meta MediaProjection](https://developers.meta.com/vr/documentation/native/native-media-projection/) beschreibt Compositor-Bild, Benutzerzustimmung und Lebenszyklus.
- [Android MediaProjection](https://developer.android.com/media/grow/media-projection) beschreibt den Foreground-Service und die Freigabe.

Die Laufzeitunterstützung muss auf dem tatsächlich eingesetzten Horizon-OS-Stand bestätigt werden.
