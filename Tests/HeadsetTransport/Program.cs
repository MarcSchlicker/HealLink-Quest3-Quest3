using System.Net;
using System.Net.Sockets;

static void ExpectFailure(byte[] bytes, Action<Stream> read)
{
    try { read(new FragmentedStream(bytes)); }
    catch (Exception ex) when (ex is IOException || ex is InvalidDataException) { return; }
    throw new Exception("Malformed stream was accepted");
}

byte[] jpeg = { 0xff, 0xd8, 1, 2, 3, 0xff, 0xd9 };
using var wire = new MemoryStream();
HeadsetFrameProtocol.WriteHello(wire);
HeadsetFrameProtocol.WriteFrame(wire, null);
HeadsetFrameProtocol.WriteFrame(wire, jpeg);
HeadsetFrameProtocol.WriteFrame(wire, jpeg);
using (var fragmented = new FragmentedStream(wire.ToArray()))
{
    HeadsetFrameProtocol.ReadHello(fragmented);
    if (HeadsetFrameProtocol.ReadFrame(fragmented) != null) throw new Exception("Heartbeat was not empty");
    for (int i = 0; i < 2; i++)
        if (!HeadsetFrameProtocol.ReadFrame(fragmented).SequenceEqual(jpeg))
            throw new Exception("Fragmented frame did not round-trip");
}
ExpectFailure(new byte[] { 0, 0, 0, 0 }, HeadsetFrameProtocol.ReadHello);
ExpectFailure(new byte[] { 0, 8, 0, 1 }, s => HeadsetFrameProtocol.ReadFrame(s));
ExpectFailure(new byte[] { 0, 0, 0, 7, 0xff, 0xd8 }, s => HeadsetFrameProtocol.ReadFrame(s));
ExpectFailure(new byte[] { 0, 0, 0, 4, 1, 2, 3, 4 }, s => HeadsetFrameProtocol.ReadFrame(s));
ExpectFailure(new byte[] { 255, 255, 255, 255 }, s => HeadsetFrameProtocol.ReadFrame(s));
try
{
    HeadsetFrameProtocol.WriteFrame(new MemoryStream(), new byte[HeadsetFrameProtocol.MaxFrameBytes + 1]);
    throw new Exception("Oversized outgoing frame accepted");
}
catch (InvalidDataException) { }

// Exercise the same framing through a real localhost TCP connection.
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
try
{
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var receive = Task.Run(() =>
    {
        using var accepted = listener.AcceptTcpClient();
        accepted.ReceiveTimeout = 2000;
        using var stream = accepted.GetStream();
        HeadsetFrameProtocol.ReadHello(stream);
        if (!HeadsetFrameProtocol.ReadFrame(stream).SequenceEqual(jpeg))
            throw new Exception("TCP frame differs");
    });
    using var sender = new TcpClient();
    sender.Connect(IPAddress.Loopback, port);
    HeadsetFrameProtocol.WriteHello(sender.GetStream());
    HeadsetFrameProtocol.WriteFrame(sender.GetStream(), jpeg);
    if (!receive.Wait(3000)) throw new Exception("TCP test timed out");
}
finally { listener.Stop(); }
Console.WriteLine("PASS: fragmented frames, heartbeats, invalid headers, limits, truncation and localhost TCP.");

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override int Read(byte[] buffer, int offset, int count) =>
        base.Read(buffer, offset, Math.Min(count, 1));
}
