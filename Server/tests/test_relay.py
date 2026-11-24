import asyncio
import json
import os
from pathlib import Path
import struct
import sys
import unittest

from websockets.asyncio.client import connect
from websockets.asyncio.server import serve
from websockets.exceptions import ConnectionClosed

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from relay import Relay, Peer, HEADER

TOKEN = "local-test-token-" + "x" * 32
JPEG = b"\xff\xd8picture\xff\xd9"
AUDIO = b"HQA1" + struct.pack("<iHh", 1, 1, 100)


class RelayTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.relay = Relay(TOKEN)
        self.server = await serve(self.relay.handle, "127.0.0.1", 0, compression=None, max_size=512 * 1024 + HEADER)
        self.url = "ws://127.0.0.1:" + str(self.server.sockets[0].getsockname()[1]) + "/relay"
        self.clients = []

    async def asyncTearDown(self):
        for client in self.clients:
            await client.close()
        self.server.close()
        await self.server.wait_closed()
        self.assertEqual(self.relay.rooms, {})
        self.assertEqual(self.relay.connections, 0)

    async def join(self, role, room="room-one", token=TOKEN):
        client = await connect(self.url, compression=None)
        self.clients.append(client)
        await client.send(json.dumps({"version": 1, "role": role, "room": room, "accessToken": token}))
        return client

    async def control(self, client, kind):
        message = json.loads(await asyncio.wait_for(client.recv(), 2))
        self.assertEqual(message["type"], kind)
        return bytes.fromhex(message["session"])

    async def pair(self, room="room-one"):
        trainee = await self.join("Trainee", room)
        await self.control(trainee, "waiting")
        expert = await self.join("Expert", room)
        session = await self.control(expert, "paired")
        self.assertEqual(session, await self.control(trainee, "paired"))
        return trainee, expert, session

    async def test_channels_and_room_isolation(self):
        trainee, expert, session = await self.pair()
        other_trainee, other_expert, other_session = await self.pair("room-two")
        for sender, receiver, channel, payload in (
            (trainee, expert, 1, JPEG), (expert, trainee, 2, b'{"action":"hands"}'),
            (trainee, expert, 3, AUDIO), (expert, trainee, 3, AUDIO)
        ):
            message = bytes([channel]) + session + payload
            await sender.send(message)
            self.assertEqual(await asyncio.wait_for(receiver.recv(), 2), message)
        message = b"\x01" + other_session + JPEG
        await other_trainee.send(message)
        self.assertEqual(await asyncio.wait_for(other_expert.recv(), 2), message)
        with self.assertRaises(asyncio.TimeoutError):
            await asyncio.wait_for(expert.recv(), .1)

    async def test_authentication_and_same_role(self):
        denied = await self.join("Expert", token="not-the-token")
        with self.assertRaises(ConnectionClosed):
            await denied.recv()
        trainee = await self.join("Trainee")
        await self.control(trainee, "waiting")
        duplicate = await self.join("Trainee")
        with self.assertRaises(ConnectionClosed):
            await duplicate.recv()

    async def test_replacement_rejects_old_session(self):
        trainee, expert, old = await self.pair()
        await expert.close()
        await self.control(trainee, "waiting")
        replacement = await self.join("Expert")
        new = await self.control(replacement, "paired")
        await self.control(trainee, "paired")
        self.assertNotEqual(old, new)
        await trainee.send(b"\x01" + old + JPEG)
        await trainee.send(b"\x01" + new + JPEG)
        self.assertEqual(await asyncio.wait_for(replacement.recv(), 2), b"\x01" + new + JPEG)

    async def test_wrong_role_video_closes_connection(self):
        trainee, expert, session = await self.pair()
        await expert.send(b"\x01" + session + JPEG)
        with self.assertRaises(ConnectionClosed):
            await expert.recv()
        await self.control(trainee, "waiting")

    async def test_bounded_media_queues(self):
        peer = Peer(None, "Expert", "room-one")
        peer.session = b"a" * 16
        for i in range(100):
            peer.enqueue(b"\x03" + peer.session + bytes([i]))
            peer.enqueue(b"\x01" + peer.session + bytes([i]))
            peer.enqueue(b"\x02" + peer.session + bytes([i]))
        self.assertEqual(len(peer.audio), 16)
        self.assertEqual(peer.video[-1], 99)
        self.assertEqual(peer.hands[-1], 99)

    async def test_actual_unity_transport_source(self):
        root = Path(__file__).resolve().parents[2]
        env = dict(os.environ, HEALLINK_TEST_URL=self.url, HEALLINK_TEST_TOKEN=TOKEN)
        process = await asyncio.create_subprocess_exec("dotnet", "run", "--project",
            str(root / "Tests/RelayClient/RelayClient.csproj"), "-v", "quiet",
            env=env, cwd=root, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.STDOUT)
        try:
            output, _ = await asyncio.wait_for(process.communicate(), 45)
        except asyncio.TimeoutError:
            process.kill()
            await process.wait()
            raise
        self.assertEqual(process.returncode, 0, output.decode())
        self.assertIn(b"PASS: Unity relay client", output)


if __name__ == "__main__":
    unittest.main()
