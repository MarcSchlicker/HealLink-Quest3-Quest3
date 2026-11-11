# Funktionskarte

| Funktion | Quellanker | Ablauf und Prüfung |
| --- | --- | --- |
| Rollen und Kopplung | QuestQuestApp.StartRole, QuestDiscovery.MatchPeer | Menü → Rolle → gegenseitige Auswahl; Tests/Pairing/run.py |
| Systembild | HeadsetViewCapture.RequestSharing, CapturePermissionActivity.onActivityResult, HeadsetCaptureService.onStartCommand | Systemzustimmung → Foreground-Service → ImageReader → JPEG; praktische Abnahme im Headset offen |
| LAN-Ansicht | QuestCameraStream.Update / NetworkLoop, HeadsetFrameProtocol.ReadFrame | Discovery-Ziel → TCP → ein aktueller Frame → kopffeste Darstellung; Tests/HeadsetTransport |
| Hände | QuestAnnotationEndpoint.SendHands / PlayHandFrame, HeadsetViewCapture.SetLocalHandsVisible | Expert-Joints → UDP → Trainee-Skelett; keine Raumkalibrierung, zwei Geräte erforderlich |
| Sprache | QuestVoiceChat.Configure / Update / FillAudio | Mikrofonfreigabe → PCM → UDP → AudioSource; kein Echo-Cancelling |
| Ein-Brillen-Test | SoloQuestTest.StartRecording / UpdatePlayback | lokale Headset-Aufnahme und Hand-Loop; eigene App-ID |
| Android-Builds | HealLinkMultiplayerBuild.Run, HealLinkSoloBuild.Run | explizite Szenenlisten → APK; siehe TESTING |

Die C#-Laufzeitquellen liegen unter Assets/Scripts, die nativen Klassen unter Assets/Plugins/Android/HeadsetCapture, die Editor-Builds unter Assets/Editor. Nicht aufgeführte SDK-Interna und die experimentelle ältere Tiefenverarbeitung wurden hier nicht vollständig kartiert.
