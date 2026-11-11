# Tests und Builds

## Lokale Prüfungen

Im Projektverzeichnis mit .NET SDK 9 und Python ausführen.

    python Tests/Pairing/run.py
    dotnet run --project Tests/HeadsetTransport/HeadsetTransport.csproj -v quiet

Der Pairing-Test extrahiert die vorhandene Kopplungslogik aus QuestQuestApp und simuliert 100 Läufe mit vier Geräten. Er prüft Exklusivität, Timeout, Ersatzpartner und Ablehnung gleicher Rollen. Er ersetzt keinen LAN-Test.

Der Transporttest kompiliert den tatsächlichen HeadsetFrameProtocol-Quelltext. Er prüft byteweise fragmentierte Lesevorgänge, mehrere aufeinanderfolgende Frames, Heartbeats, ungültige Kennungen, Größenlimits, abgeschnittene Pakete und einen echten lokalen TCP-Roundtrip. Eine Ausführungsumgebung muss Loopback-Sockets erlauben. In der Codex-Sandbox ergab der TCP-Aufruf Fehler 10013; außerhalb der Sandbox bestand er.

Beide Prüfungen wurden am 11.11.2026 erfolgreich ausgeführt.

## APK bauen

Verwendeter Editor ist Unity 6000.6.7f1 mit Android Build Support. Android SDK, NDK und OpenJDK stammen aus der Unity-Installation.

Im Editor:

- HealLink → Build Multiplayer APK
- HealLink → Build Solo Headset Test APK

Für den Multiplayer-Build über die Kommandozeile:

    Unity.exe -batchmode -nographics -quit -accept-apiupdate -projectPath "<Projektpfad>" -buildTarget Android -executeMethod HealLinkMultiplayerBuild.Run -logFile "<Logdatei>"

Das Projekt darf dabei nicht bereits im gleichen Editor geöffnet sein. Für den verifizierten Build wurde eine separate Kopie von Assets, Packages und ProjectSettings unter Temp/QuestInstallProject verwendet.

[HealLinkMultiplayerBuild](../Assets/Editor/HealLinkMultiplayerBuild.cs) verwendet ausschließlich die drei Multiplayer-Szenen und erzeugt Builds/HealLink-Multiplayer-0.2.0.apk. Paketkennung ist de.heallink.questquest, Version 0.2.0, Android-Versioncode 2. Die vorübergehend gesetzten PlayerSettings werden danach wiederhergestellt.

[HealLinkSoloBuild](../Assets/Editor/HealLinkSoloBuild.cs) erzeugt die separate Test-App de.heallink.questquest.solotest. Die Aufnahme bleibt lokal im persistentDataPath. Bildschirm- und Mikrofonfreigabe müssen dort ebenfalls praktisch geprüft werden.

## Installation

USB-Debugging im Headset bestätigen und pro angeschlossenem Gerät ausführen:

    adb devices -l
    adb -s <Geraetekennung> install -r Builds/HealLink-Multiplayer-0.2.0.apk

Anschließend unter Unbekannte Quellen die App HealLink QuestQuest öffnen. Die Solo-Test-App hat eine eigene Paketkennung und bleibt separat.

## Manuelle Abnahme mit zwei Quest 3

Diese Schritte sind für Version 0.2.0 noch offen:

- Auf beiden Geräten denselben neuen Multiplayer-Build installieren und unterschiedliche Rollen wählen.
- Prüfen, dass das Menü erst auf einen neuen Pinch am Laserziel reagiert.
- Die Bildschirmfreigabe einmal ablehnen, mit A erneut öffnen und bestätigen.
- Prüfen, dass beim Expert der vollständige freigegebene Trainee-Blick einschließlich lokalem Passthrough ankommt.
- Kopf des Trainees bewegen. Prüfen, dass der Bildausschnitt folgt und beim Expert weder gespiegelt noch verdreht ist.
- Beide Mikrofone prüfen, jeweils abwechselnd sprechen.
- Expert-Hände bewegen und die Skelettdarstellung beim Trainee prüfen.
- Prüfen, dass keine zweite lokale Handdarstellung oder Statusanzeige den Expert-Blick verdeckt.
- Bildschirmfreigabe über das System beenden und erneut starten.
- WLAN-Verbindung unterbrechen. Nach spätestens drei Sekunden darf das letzte Bild nicht sichtbar bleiben.
- Mit B ins Menü wechseln, neu koppeln und auf alte Frames beziehungsweise alte Handdarstellungen achten.
- Einen alten Build als Partner verwenden. Die neue App muss zur Aktualisierung auffordern.

Stand von APK-Build und Artefakt ist in [CHANGELOG.md](CHANGELOG.md) dokumentiert. Frühere Nutzerbestätigungen betreffen die Verbindung und den Solo-Bildweg; sie sind keine Abnahme dieses Multiplayer-Builds.
