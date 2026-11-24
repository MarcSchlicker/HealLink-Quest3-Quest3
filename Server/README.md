# HealLink Internet relay

The relay connects one Trainee and one Expert in the same room. The Quests can use different Wi-Fi networks or phone hotspots. They only make outbound WSS connections.

## What the university needs to provide

- A Linux host with Docker Compose, or a Python 3.12 service behind its existing HTTPS proxy.
- A public DNS name and a valid TLS certificate.
- WebSocket connections on TCP 443, with long connection timeouts.
- Enough bandwidth for the headset video. Capture currently uses JPEG at up to 10 fps, plus PCM voice. Actual bandwidth and latency need measurement on the two sites.

The relay receives the media in cleartext inside the service. TLS protects the connections to the server; this is not end-to-end encryption.

## Docker deployment

1. Copy this folder to the server.
2. Copy .env.example to .env and set RELAY_DOMAIN to the public hostname.
3. Generate a secret with `python -c "import secrets; print(secrets.token_urlsafe(32))"` and put it in HEALLINK_RELAY_TOKEN. Keep this value out of Git.
4. Run `docker compose up -d --build`.

The included Caddy service manages HTTPS certificates. Ports 80 and 443 must reach Caddy for this configuration. If the university already supplies a TLS reverse proxy, run only the relay service on internal port 8080 and proxy the WebSocket path /relay to it. Do not expose unencrypted port 8080 to the Internet.

No recording is stored by the relay. Media queues are bounded, and old frames are discarded after a participant disconnects. The shared access token is the server access control. Room codes separate sessions; use a fresh, hard-to-guess room code for each pair. A room accepts one participant per role.

## Configure the Quests

Create a local file named relay.json. Give both headsets the same values:

```json
{
  "enabled": true,
  "serverUrl": "wss://relay.example.edu/relay",
  "room": "replace-with-a-random-room-code",
  "accessToken": "replace-with-the-server-access-token"
}
```

Room codes use 6–64 letters, digits, underscores or hyphens. The access token has at least 32 characters. The shipped defaults in Assets/Resources/HealLinkRelay.json contain no server credentials and leave LAN mode enabled.

Push the local configuration to each connected headset:

    adb -s <device-id> push relay.json /sdcard/Android/data/de.heallink.questquest/files/relay.json

Restart HealLink. The menu shows Internet and the room code. Choose Trainee on one device and Expert on the other, then approve the Trainee screen-sharing prompt and microphone access. Switching the roles also switches the streaming direction.

Set enabled to false in the local configuration to return to LAN mode. An enabled but invalid server configuration reports an error instead of silently choosing LAN.

The configuration UI currently uses a file loaded at app startup. There is no in-headset server address keyboard yet. Do not commit a configuration containing real access tokens.

## Local tests

From the repository root:

    python -m venv Server/.venv
    Server/.venv/Scripts/python -m pip install -r Server/requirements.txt
    Server/.venv/Scripts/python -m unittest discover -s Server/tests -v

On Linux, use Server/.venv/bin/python. The tests include the actual Unity C# transport compiled with .NET SDK 9 and small Unity API stubs. They exercise pairing, video, hand packets, voice in both directions, room isolation, authentication, duplicate roles, session replacement and bounded queues over real localhost WebSockets.

The Android/IL2CPP transport still needs a test with two Quests and the actual university-hosted WSS endpoint. A passing desktop test does not verify that setup.
