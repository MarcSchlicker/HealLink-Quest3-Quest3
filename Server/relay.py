"""A bounded, two-participant room relay for the HealLink Internet transport."""
import asyncio
import collections
import contextlib
import hmac
import json
import logging
import os
import re
import secrets
from dataclasses import dataclass, field

from websockets.asyncio.server import serve
from websockets.exceptions import ConnectionClosed

MAX_VIDEO = 512 * 1024
HEADER = 17
ROOM_NAME = re.compile(r"[A-Za-z0-9_-]{6,64}\Z")


@dataclass(eq=False)
class Peer:
    socket: object
    role: str
    room: str
    session: bytes = b""
    partner: object = None
    controls: collections.deque = field(default_factory=collections.deque)
    audio: collections.deque = field(default_factory=lambda: collections.deque(maxlen=16))
    hands: bytes = None
    video: bytes = None
    wake: asyncio.Event = field(default_factory=asyncio.Event)

    def clear_media(self):
        self.audio.clear()
        self.hands = self.video = None

    def control(self, kind):
        self.controls.clear()
        self.controls.append(json.dumps({
            "type": kind, "session": self.session.hex()
        }))
        self.wake.set()

    def enqueue(self, message):
        channel = message[0]
        if channel == 1:
            self.video = message
        elif channel == 2:
            self.hands = message
        else:
            self.audio.append(message)
        self.wake.set()

    async def write(self):
        while True:
            await self.wake.wait()
            self.wake.clear()
            while self.controls or self.audio or self.hands or self.video:
                if self.controls:
                    message = self.controls.popleft()
                elif self.audio:
                    message = self.audio.popleft()
                elif self.hands:
                    message, self.hands = self.hands, None
                else:
                    message, self.video = self.video, None
                if isinstance(message, bytes) and message[1:HEADER] != self.session:
                    continue
                # A slow peer must not retain an unlimited TCP backlog.
                await asyncio.wait_for(self.socket.send(message), timeout=5)


class Relay:
    def __init__(self, access_token):
        if not 32 <= len(access_token) <= 512 or access_token.startswith("replace-"):
            raise ValueError("HEALLINK_RELAY_TOKEN must be a new secret with 32 to 512 characters")
        self.access_token = access_token
        self.rooms = {}
        self.connections = 0

    def leave(self, peer):
        room = self.rooms.get(peer.room, {})
        if room.get(peer.role) is peer:
            del room[peer.role]
        if not room:
            self.rooms.pop(peer.room, None)
        partner = peer.partner
        peer.partner = None
        peer.session = b""
        peer.clear_media()
        if partner is not None and partner.partner is peer:
            partner.partner = None
            partner.session = b""
            partner.clear_media()
            partner.control("waiting")

    async def handle(self, socket):
        self.connections += 1
        peer = None
        writer = None
        try:
            if self.connections > 128 or socket.request.path != "/relay":
                await socket.close(1008, "Connection unavailable")
                return
            hello = await asyncio.wait_for(socket.recv(), timeout=10)
            if not isinstance(hello, str) or len(hello) > 4096:
                await socket.close(1008, "Invalid join")
                return
            join = json.loads(hello)
            token = join.get("accessToken", "")
            room_name, role = join.get("room", ""), join.get("role", "")
            if (not isinstance(token, str) or
                not hmac.compare_digest(token.encode(), self.access_token.encode()) or
                join.get("version") != 1 or
                not isinstance(room_name, str) or not ROOM_NAME.fullmatch(room_name) or
                role not in ("Trainee", "Expert")):
                await socket.close(1008, "Join rejected")
                return
            if room_name not in self.rooms and len(self.rooms) >= 64:
                await socket.close(1013, "Room capacity reached")
                return
            room = self.rooms.setdefault(room_name, {})
            if role in room:
                await socket.close(1008, "Role already occupied in this room")
                return
            peer = Peer(socket, role, room_name)
            room[role] = peer
            partner = room.get("Expert" if role == "Trainee" else "Trainee")
            if partner is None:
                peer.control("waiting")
            else:
                session = secrets.token_bytes(16)
                peer.partner, partner.partner = partner, peer
                peer.session = partner.session = session
                peer.clear_media()
                partner.clear_media()
                peer.control("paired")
                partner.control("paired")
            writer = asyncio.create_task(peer.write())

            async def read():
                async for message in socket:
                    if not isinstance(message, bytes) or len(message) <= HEADER:
                        await socket.close(1008, "Invalid media packet")
                        return
                    channel, session, payload = message[0], message[1:HEADER], message[HEADER:]
                    if not peer.session or session != peer.session or peer.partner is None:
                        continue
                    valid = (
                        channel == 1 and role == "Trainee" and len(payload) <= MAX_VIDEO
                        and payload.startswith(b"\xff\xd8") and payload.endswith(b"\xff\xd9")
                    ) or (
                        channel == 2 and role == "Expert" and len(payload) <= 8192
                    ) or (
                        channel == 3 and 10 <= len(payload) <= 1290
                        and payload[:4] == b"HQA1"
                        and len(payload) == 10 + 2 * int.from_bytes(payload[8:10], "little")
                    )
                    if not valid:
                        await socket.close(1008, "Invalid media packet")
                        return
                    peer.partner.enqueue(message)

            reading = asyncio.create_task(read())
            try:
                done, pending = await asyncio.wait((reading, writer), return_when=asyncio.FIRST_COMPLETED)
                for task in done:
                    task.result()
            finally:
                reading.cancel()
                with contextlib.suppress(asyncio.CancelledError, ConnectionClosed):
                    await reading
        except (ConnectionClosed, asyncio.TimeoutError, ValueError, TypeError, AttributeError):
            pass
        finally:
            if writer is not None:
                writer.cancel()
                with contextlib.suppress(asyncio.CancelledError, ConnectionClosed, asyncio.TimeoutError):
                    await writer
            if peer is not None:
                self.leave(peer)
            self.connections -= 1
            await socket.close()


async def main():
    relay = Relay(os.environ.get("HEALLINK_RELAY_TOKEN", ""))
    host = os.environ.get("HEALLINK_RELAY_HOST", "0.0.0.0")
    port = int(os.environ.get("HEALLINK_RELAY_PORT", "8080"))
    # TLS is terminated by the university's HTTPS reverse proxy or Caddy.
    async with serve(relay.handle, host, port, compression=None, max_size=MAX_VIDEO + HEADER,
                     max_queue=4, write_limit=32768, ping_interval=20, ping_timeout=20,
                     close_timeout=3):
        print("HealLink relay listening", flush=True)
        await asyncio.Future()


if __name__ == "__main__":
    logging.basicConfig(level=logging.WARNING)
    asyncio.run(main())
