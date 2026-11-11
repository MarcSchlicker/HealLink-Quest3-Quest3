using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Meta.XR;
using UnityEngine;

// Bounded transmission of the consenting trainee's headset compositor view.
public sealed class QuestCameraStream : MonoBehaviour
{
    public bool sendCamera;
    public int port = 5091;
    public Renderer remoteDisplay;
    private QuestQuestApp app;
    private volatile bool running;
    private volatile string peerHost;
    private readonly object gate = new object();
    private byte[] outgoing, incoming;
    private TcpListener listener;
    private TcpClient client;
    private Thread worker;
    private HeadsetViewCapture headsetView;
    private float nextFrame, lastFrame;
    public string Status { get; private set; } = "Camera: waiting for a connection";

    private void Start()
    {
        app = GetComponent<QuestQuestApp>();
        headsetView = gameObject.AddComponent<HeadsetViewCapture>();
        if (sendCamera) headsetView.InitializeCapture();
        else if (remoteDisplay != null) headsetView.InitializeDisplay(Camera.main.transform, remoteDisplay);
        running = true;
        worker = new Thread(NetworkLoop) { IsBackground = true, Name = "HealLink Camera" };
        worker.Start();
    }
    private void Update()
    {
        string target = app != null ? app.PeerHost : null;
        if (peerHost != target)
        {
            peerHost = target;
            lock(gate) { outgoing = null; incoming = null; }
            client?.Close();
            if (sendCamera) { if (target == null) headsetView.StopSharing(); else headsetView.RequestSharing(); }
            if(remoteDisplay != null) remoteDisplay.enabled = false;
        }
        if (string.IsNullOrEmpty(target)) { Status = "Camera: waiting for a partner"; return; }
        if (sendCamera && OVRInput.GetDown(OVRInput.Button.One)) headsetView.RequestSharing();
        Status = headsetView.Status;
        if (!sendCamera)
        {
            byte[] frame;
            lock (gate) { frame = incoming; incoming = null; }
            if (frame != null && headsetView.ShowFrame(frame)) lastFrame = Time.unscaledTime;
            if (Time.unscaledTime - lastFrame > 3f && remoteDisplay != null) remoteDisplay.enabled = false;
        }
    }
    private void LateUpdate()
    {
        if (!sendCamera || string.IsNullOrEmpty(peerHost) || Time.unscaledTime < nextFrame) return;
        nextFrame = Time.unscaledTime + .1f;
        string target = peerHost;
        headsetView.RequestFrame(frame =>
        {
            if (running && peerHost == target) lock (gate) outgoing = frame;
        });
    }
    private void NetworkLoop()
    {
        try
        {
            if(!sendCamera) { listener = new TcpListener(IPAddress.Any,port); listener.Start(); }
            while(running)
            {
                string target = peerHost;
                if(string.IsNullOrEmpty(target)) { Thread.Sleep(100); continue; }
                try
                {
                    if(sendCamera)
                    {
                        client = new TcpClient();
                        var attempt = client.BeginConnect(target,port,null,null);
                        using(attempt.AsyncWaitHandle)
                        {
                            if(!attempt.AsyncWaitHandle.WaitOne(1500)) throw new IOException("Connect timeout");
                            client.EndConnect(attempt);
                        }
                    }
                    else
                    {
                        if(!listener.Pending()) { Thread.Sleep(100); continue; }
                        client = listener.AcceptTcpClient();
                        if(((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString() != target) { client.Close(); continue; }
                    }
                    client.SendTimeout = 1500; client.ReceiveTimeout = 1500; client.NoDelay = true;
                    using(var stream=client.GetStream())
                    while(running && peerHost == target)
                    {
                        if(sendCamera)
                        {
                            byte[] frame;
                            lock(gate) { frame=outgoing;outgoing=null; }
                            if(frame==null) { Thread.Sleep(20);continue; }
                            byte[] header=BitConverter.GetBytes(IPAddress.HostToNetworkOrder(frame.Length));
                            stream.Write(header,0,4);stream.Write(frame,0,frame.Length);
                        }
                        else
                        {
                            var header=ReadExactly(stream,4);
                            int length=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header,0));
                            if(length<1 || length>2*1024*1024) throw new IOException("Invalid camera frame");
                            byte[] frame=ReadExactly(stream,length);
                            lock(gate) incoming=frame;
                        }
                    }
                }
                catch(Exception e) when(e is IOException || e is SocketException || e is ObjectDisposedException) { }
                finally { client?.Close(); }
                if(running) Thread.Sleep(250);
            }
        }
        catch(SocketException) { /* Main-thread status remains waiting; port may be unavailable. */ }
        finally { listener?.Stop(); }
    }
    private static byte[] ReadExactly(Stream stream,int count)
    {
        var bytes=new byte[count];int offset=0;
        while(offset<count) { int n=stream.Read(bytes,offset,count-offset);if(n==0)throw new IOException("Disconnected");offset+=n; }
        return bytes;
    }
    private void OnDestroy()
    {
        running=false;client?.Close();listener?.Stop();
        worker?.Join(2000);

    }
}
