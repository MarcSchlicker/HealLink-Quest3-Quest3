using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Outbound WSS transport; all Unity-facing callbacks run in Update.</summary>
[DefaultExecutionOrder(-50)]
public sealed class QuestRelayTransport : MonoBehaviour
{
    private const int Header = 17, MaxMessage = 512 * 1024 + Header;
    private readonly object gate = new object();
    private readonly Queue<byte[]> outboundAudio = new Queue<byte[]>();
    private readonly Queue<byte[]> inboundAudio = new Queue<byte[]>();
    private byte[] outboundVideo, outboundHands, inboundVideo, inboundHands;
    private byte[] session;
    private int revision, deliveredRevision = -1;
    private string pendingStatus = "Connecting to relay";
    private CancellationTokenSource lifetime;
    private ClientWebSocket socket;
    private Task worker;
    private QuestRole role;

    public bool IsPaired { get; private set; }
    public string SessionId { get; private set; }
    public string Status { get; private set; } = "Connecting to relay";
    public event Action ConnectionChanged;
    public event Action<byte[]> VideoReceived;
    public event Action<byte[]> HandsReceived;
    public event Action<byte[]> AudioReceived;

    [Serializable]
    private sealed class Join
    {
        public int version = 1;
        public string role, room, accessToken;
    }

    public void Configure(QuestRole selectedRole, QuestRelaySettings settings)
    {
        role = selectedRole;
        lifetime = new CancellationTokenSource();
        string hello = JsonUtility.ToJson(new Join
        {
            role = role.ToString(), room = settings.room, accessToken = settings.accessToken
        });
        worker = Run(new Uri(settings.serverUrl), hello, lifetime.Token);
    }

    public void SendVideo(byte[] data) => Enqueue(1, data);
    public void SendHands(byte[] data) => Enqueue(2, data);
    public void SendAudio(byte[] data) => Enqueue(3, data);

    private void Enqueue(byte channel, byte[] payload)
    {
        if (payload == null || (channel == 1 ? payload.Length > 512 * 1024 :
            channel == 2 ? payload.Length > 8192 : payload.Length > 1290)) return;
        lock (gate)
        {
            if (session == null) return;
            var packet = new byte[Header + payload.Length];
            packet[0] = channel;
            Buffer.BlockCopy(session, 0, packet, 1, 16);
            Buffer.BlockCopy(payload, 0, packet, Header, payload.Length);
            if (channel == 1) outboundVideo = packet;
            else if (channel == 2) outboundHands = packet;
            else
            {
                if (outboundAudio.Count >= 16) outboundAudio.Dequeue();
                outboundAudio.Enqueue(packet);
            }
        }
    }

    private void ClearMedia()
    {
        outboundVideo = outboundHands = inboundVideo = inboundHands = null;
        outboundAudio.Clear();
        inboundAudio.Clear();
    }

    private void SetSession(byte[] value, string status)
    {
        lock (gate)
        {
            session = value;
            pendingStatus = status;
            revision++;
            ClearMedia();
        }
    }

    private async Task Run(Uri uri, string hello, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new ClientWebSocket();
                lock (gate) socket = client;
                client.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connect.CancelAfter(TimeSpan.FromSeconds(12));
                    await client.ConnectAsync(uri, connect.Token).ConfigureAwait(false);
                }
                byte[] join = Encoding.UTF8.GetBytes(hello);
                await client.SendAsync(new ArraySegment<byte>(join), WebSocketMessageType.Text, true, token)
                    .ConfigureAwait(false);
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task sending = SendLoop(client, connection.Token);
                Task receiving = ReceiveLoop(client, connection.Token);
                try
                {
                    Task ended = await Task.WhenAny(sending, receiving).ConfigureAwait(false);
                    await ended.ConfigureAwait(false);
                }
                finally
                {
                    connection.Cancel();
                    client.Abort();
                    try { await Task.WhenAll(sending, receiving).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is WebSocketException ||
                        ex is IOException || ex is ObjectDisposedException || ex is FormatException) { }
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is IOException ||
                ex is OperationCanceledException || ex is ObjectDisposedException || ex is FormatException)
            {
                // Never expose access tokens, URLs or exception details through the status label.
            }
            finally
            {
                lock (gate) socket = null;
                SetSession(null, token.IsCancellationRequested ? "Relay disconnected" :
                    "Relay disconnected. Check server, room and access token.");
            }
            try { await Task.Delay(2000, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SendLoop(ClientWebSocket client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            byte[] packet = null;
            lock (gate)
            {
                if (session != null)
                {
                    if (outboundAudio.Count > 0) packet = outboundAudio.Dequeue();
                    else if (outboundHands != null) { packet = outboundHands; outboundHands = null; }
                    else if (outboundVideo != null) { packet = outboundVideo; outboundVideo = null; }
                }
            }
            if (packet == null) { await Task.Delay(5, token).ConfigureAwait(false); continue; }
            using var send = CancellationTokenSource.CreateLinkedTokenSource(token);
            send.CancelAfter(TimeSpan.FromSeconds(5));
            await client.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, send.Token)
                .ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoop(ClientWebSocket client, CancellationToken token)
    {
        var chunk = new byte[16384];
        while (!token.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await client.ReceiveAsync(new ArraySegment<byte>(chunk), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (message.Length + result.Count > MaxMessage) throw new IOException("Relay message exceeds limit");
                message.Write(chunk, 0, result.Count);
            } while (!result.EndOfMessage);
            byte[] bytes = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                if (bytes.Length > 4096) throw new IOException("Relay control exceeds limit");
                // Control JSON is handled below without touching Unity APIs on the worker.
                string control = Encoding.UTF8.GetString(bytes);
                var kind = System.Text.RegularExpressions.Regex.Match(control,
                    @"""type""\s*:\s*""(waiting|paired)""");
                if (kind.Groups[1].Value == "waiting")
                    SetSession(null, "Waiting for the other role in this room");
                else
                {
                    var match = System.Text.RegularExpressions.Regex.Match(control,
                        "\\\"session\\\"\\s*:\\s*\\\"([a-f0-9]{32})\\\"");
                    if (kind.Groups[1].Value != "paired" || !match.Success)
                        throw new FormatException("Invalid relay control");
                    var id = new byte[16];
                    for (int i = 0; i < 16; i++) id[i] = Convert.ToByte(match.Groups[1].Value.Substring(i * 2, 2), 16);
                    SetSession(id, "Connected through relay");
                }
                continue;
            }
            if (bytes.Length <= Header) throw new IOException("Invalid relay packet");
            lock (gate)
            {
                if (session == null) continue;
                bool matches = true;
                for (int i = 0; i < 16; i++) matches &= bytes[i + 1] == session[i];
                if (!matches) continue;
                int length = bytes.Length - Header;
                if ((bytes[0] == 1 && (role != QuestRole.Expert || length > 512 * 1024)) ||
                    (bytes[0] == 2 && (role != QuestRole.Trainee || length > 8192)) ||
                    (bytes[0] == 3 && (length < 10 || length > 1290)) ||
                    bytes[0] < 1 || bytes[0] > 3) throw new IOException("Invalid relay channel");
                var payload = new byte[length];
                Buffer.BlockCopy(bytes, Header, payload, 0, length);
                if (bytes[0] == 1) inboundVideo = payload;
                else if (bytes[0] == 2) inboundHands = payload;
                else
                {
                    if (inboundAudio.Count >= 16) inboundAudio.Dequeue();
                    inboundAudio.Enqueue(payload);
                }
            }
        }
    }

    private void Update()
    {
        byte[] video, hands;
        byte[][] audio;
        bool changed;
        lock (gate)
        {
            changed = revision != deliveredRevision;
            deliveredRevision = revision;
            IsPaired = session != null;
            SessionId = session != null ? BitConverter.ToString(session).Replace("-", "") : null;
            Status = pendingStatus;
            video = inboundVideo; hands = inboundHands;
            inboundVideo = inboundHands = null;
            audio = inboundAudio.ToArray();
            inboundAudio.Clear();
        }
        if (changed) ConnectionChanged?.Invoke();
        if (!IsPaired) return;
        if (video != null) VideoReceived?.Invoke(video);
        if (hands != null) HandsReceived?.Invoke(hands);
        foreach (var frame in audio) AudioReceived?.Invoke(frame);
    }

    private void OnDestroy()
    {
        lifetime?.Cancel();
        lock (gate) socket?.Abort();
        var cancellation = lifetime;
        if (worker != null)
            worker.ContinueWith(task => { var ignored = task.Exception; cancellation.Dispose(); },
                TaskScheduler.Default);
        else cancellation?.Dispose();
        // Cancellation closes the actual socket; no detached blocking receive remains.
    }
}
