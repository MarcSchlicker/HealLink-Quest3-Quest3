using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// One paired peer, one pending frame per direction, no frame history.
public sealed class QuestCameraStream : MonoBehaviour
{
    public bool sendCamera;
    public int port = 5091;
    public Renderer remoteDisplay;

    private sealed class Target
    {
        internal readonly string Host;
        internal readonly int Port;
        internal readonly string InstanceId;
        internal Target(string host, int port, string instanceId)
        {
            Host = host;
            Port = port;
            InstanceId = instanceId;
        }
    }

    private QuestQuestApp app;
    private QuestRelayTransport relay;
    private bool relayWasPaired;
    private bool screenConsentPending;
    private float screenConsentTime;
    private HeadsetViewCapture headsetView;
    private volatile bool running;
    private volatile Target target;
    private volatile string networkFailure;
    private readonly object frameGate = new object();
    private readonly object socketGate = new object();
    private byte[] outgoing, incoming;
    private TcpClient connection;
    private TcpListener listener;
    private Thread worker;
    private float nextFrame, lastFrame = -10f;

    public bool HasRecentFrame => !sendCamera && (relay != null ? relay.IsPaired : target != null) &&
        Time.unscaledTime - lastFrame < 3f;
    public bool IsSharing => sendCamera && (relay != null ? relay.IsPaired : target != null) &&
        headsetView != null && headsetView.IsReady;
    public string Status { get; private set; } = "Waiting for the paired Quest";

    private void Start()
    {
        app = GetComponent<QuestQuestApp>();
        if (!app.RoleStarted) { enabled = false; return; }
        relay = app.Relay;
        headsetView = gameObject.AddComponent<HeadsetViewCapture>();
        if (sendCamera) headsetView.InitializeCapture();
        else if (remoteDisplay != null) headsetView.InitializeDisplay(Camera.main.transform, remoteDisplay);
        if (relay != null)
        {
            relay.VideoReceived += ReceiveRelayedVideo;
            relay.ConnectionChanged += ResetRelayedView;
            return;
        }
        running = true;
        worker = new Thread(NetworkLoop) { IsBackground = true, Name = "HealLink headset view" };
        worker.Start();
    }

    private void Update()
    {
        if (relay != null)
        {
            UpdateRelay();
            return;
        }
        string host = app.PeerSupportsHeadsetView ? app.PeerHost : null;
        int remotePort = app.PeerHeadsetViewPort;
        Target current = target;
        if (current?.Host != host || (host != null &&
            (current.Port != remotePort || current.InstanceId != app.PeerInstanceId)))
        {
            target = host == null ? null : new Target(host, remotePort, app.PeerInstanceId);
            lock (frameGate) { outgoing = null; incoming = null; }
            CloseConnection();
            lastFrame = -10f;
            if (remoteDisplay != null) remoteDisplay.enabled = false;
            if (sendCamera)
            {
                screenConsentPending = target != null;
                screenConsentTime = Time.unscaledTime + .5f;
                if (target == null) headsetView.StopSharing();
            }
        }

        if (target == null)
        {
            Status = app.PeerHost == null ? "Waiting for the paired Quest"
                : "Update the other Quest to enable headset view sharing";
        }
        else if (sendCamera)
        {
            PollScreenSharingRequest();
            Status = headsetView.Status;
        }
        else
        {
            byte[] frame;
            lock (frameGate) { frame = incoming; incoming = null; }
            if (frame != null && headsetView.ShowFrame(frame)) lastFrame = Time.unscaledTime;
            if (remoteDisplay != null) remoteDisplay.enabled = HasRecentFrame;
            Status = HasRecentFrame ? "Receiving the trainee headset view" : "Waiting for trainee screen sharing";
        }
        if (networkFailure != null) Status = networkFailure;
        if (!sendCamera) headsetView.SetLocalHandsVisible(!HasRecentFrame);
    }

    private void LateUpdate()
    {
        if (relay != null)
        {
            if (sendCamera && relay.IsPaired && Time.unscaledTime >= nextFrame)
            {
                nextFrame = Time.unscaledTime + .1f;
                headsetView.RequestFrame(relay.SendVideo);
            }
            return;
        }
        Target current = target;
        if (!sendCamera || current == null || Time.unscaledTime < nextFrame) return;
        nextFrame = Time.unscaledTime + .1f;
        headsetView.RequestFrame(frame =>
        {
            lock (frameGate)
                if (running && ReferenceEquals(target, current)) outgoing = frame;
        });
    }

    private void ReceiveRelayedVideo(byte[] frame)
    {
        lock (frameGate) incoming = frame;
    }

    private void ResetRelayedView()
    {
        lock (frameGate) incoming = null;
        lastFrame = -10f;
        relayWasPaired = false;
        screenConsentPending = false;
        if (remoteDisplay != null) remoteDisplay.enabled = false;
        if (sendCamera) headsetView.StopSharing();
    }

    private void UpdateRelay()
    {
        if (sendCamera)
        {
            if (relay.IsPaired && !relayWasPaired)
            {
                screenConsentPending = true;
                screenConsentTime = Time.unscaledTime + .5f;
            }
            if (relay.IsPaired) PollScreenSharingRequest();
            Status = relay.IsPaired ? headsetView.Status : relay.Status;
        }
        else
        {
            byte[] frame;
            lock (frameGate) { frame = incoming; incoming = null; }
            if (relay.IsPaired && frame != null && headsetView.ShowFrame(frame))
                lastFrame = Time.unscaledTime;
            if (remoteDisplay != null) remoteDisplay.enabled = HasRecentFrame;
            headsetView.SetLocalHandsVisible(!HasRecentFrame);
            Status = !relay.IsPaired ? relay.Status : HasRecentFrame
                ? "Receiving the trainee through relay" : "Waiting for trainee screen sharing";
        }
        relayWasPaired = relay.IsPaired;
    }

    private void PollScreenSharingRequest()
    {
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            screenConsentPending = true;
            screenConsentTime = Time.unscaledTime;
        }
        if (screenConsentPending && Time.unscaledTime >= screenConsentTime &&
            !app.MicrophonePermissionPending)
        {
            screenConsentPending = false;
            headsetView.RequestSharing();
        }
    }

    private void NetworkLoop()
    {
        try
        {
            if (!sendCamera)
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            while (running)
            {
                Target current = target;
                if (current == null) { Thread.Sleep(100); continue; }
                try
                {
                    using (TcpClient client = Connect(current))
                    {
                        if (client == null) continue;
                        lock (socketGate)
                        {
                            if (!running || !ReferenceEquals(target, current)) continue;
                            connection = client;
                        }
                        client.NoDelay = true;
                        client.SendTimeout = 1500;
                        client.ReceiveTimeout = 2000;
                        using NetworkStream stream = client.GetStream();
                        if (sendCamera) HeadsetFrameProtocol.WriteHello(stream);
                        else HeadsetFrameProtocol.ReadHello(stream);
                        while (running && ReferenceEquals(target, current))
                        {
                            if (sendCamera)
                            {
                                byte[] frame;
                                lock (frameGate) { frame = outgoing; outgoing = null; }
                                HeadsetFrameProtocol.WriteFrame(stream, frame);
                                // A zero-length heartbeat keeps the receiver alive while consent is pending.
                                if (frame == null) Thread.Sleep(100);
                                else Thread.Sleep(10);
                            }
                            else
                            {
                                byte[] frame = HeadsetFrameProtocol.ReadFrame(stream);
                                if (frame == null) continue;
                                lock (frameGate)
                                    if (ReferenceEquals(target, current)) incoming = frame;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException ||
                    ex is SocketException || ex is ObjectDisposedException)
                {
                    // Retrying retains only the latest frame; stale imagery expires on the main thread.
                }
                finally
                {
                    lock (socketGate) connection = null;
                }
                if (running) Thread.Sleep(250);
            }
        }
        catch (SocketException) { networkFailure = "Headset view port is unavailable. Restart the session."; }
        finally { listener?.Stop(); }
    }

    private TcpClient Connect(Target current)
    {
        if (!sendCamera)
        {
            if (!listener.Pending()) { Thread.Sleep(100); return null; }
            TcpClient accepted = listener.AcceptTcpClient();
            if (((IPEndPoint)accepted.Client.RemoteEndPoint).Address.Equals(IPAddress.Parse(current.Host)))
                return accepted;
            accepted.Close();
            return null;
        }
        var client = new TcpClient();
        try
        {
            var attempt = client.BeginConnect(current.Host, current.Port, null, null);
            using (attempt.AsyncWaitHandle)
            {
                if (!attempt.AsyncWaitHandle.WaitOne(1000)) throw new IOException("Connect timeout");
                client.EndConnect(attempt);
            }
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private void CloseConnection()
    {
        lock (socketGate) connection?.Close();
    }

    private void OnDestroy()
    {
        if (relay != null)
        {
            relay.VideoReceived -= ReceiveRelayedVideo;
            relay.ConnectionChanged -= ResetRelayedView;
        }
        running = false;
        target = null;
        CloseConnection();
        listener?.Stop();
        worker?.Join(2500);
        if (headsetView != null) headsetView.StopSharing();
    }
}
