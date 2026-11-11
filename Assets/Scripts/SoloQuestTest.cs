using System;
using System.Collections.Generic;
using System.IO;
using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public sealed class SoloQuestTest : MonoBehaviour
{
    private const float Duration = 12f;
    private const int AudioRate = 48000;
    private const int FrameLimit = 260;
    [SerializeField] private bool trainee;
    [SerializeField] private GameObject sceneOverview;
    [SerializeField] private TextMesh statusText;
    [SerializeField] private Renderer remoteDisplay;

    private readonly List<Frame> recording = new List<Frame>();
    private List<Frame> playback;
    private Camera eye;
    private AudioClip microphone;
    private AudioSource speaker;
    private HeadsetViewCapture headsetView;
    private int captureGeneration;
    private QuestAnnotationEndpoint hands;
    private float started, nextFrame, playbackStarted;
    private float microphoneDeadline;
    private bool waitingForSharing;
    private float sharingDeadline;
    private int microphoneStart, displayedFrame = -1;
    private bool recordingNow, previousRecord = true, previousBack = true, readyForInput;
    private string message;

    private string MediaPath => Path.Combine(Application.persistentDataPath, "trainee-headset-view-v4.bin");
    private string HandsPath => Path.Combine(Application.persistentDataPath, "expert-hands-v1.bin");
    private string CapturePath => trainee ? MediaPath : HandsPath;

    private struct Frame
    {
        public float Time;
        public byte[] Data;
    }

    private void Start()
    {
        eye = Camera.main;
        if (eye == null)
        {
            Debug.LogError("Solo test requires a main camera.");
            enabled = false;
            return;
        }

        sceneOverview.SetActive(false);
        var manager = FindAnyObjectByType<OVRManager>();
        if (manager != null) manager.isInsightPassthroughEnabled = trainee;
        eye.clearFlags = CameraClearFlags.SolidColor;
        eye.backgroundColor = trainee ? Color.clear : new Color(.015f, .025f, .04f, 1f);
        eye.allowHDR = false;
        eye.GetUniversalAdditionalCameraData().renderPostProcessing = false;

        headsetView = gameObject.AddComponent<HeadsetViewCapture>();
        if (trainee)
        {
            headsetView.InitializeCapture();
            hands = gameObject.AddComponent<QuestAnnotationEndpoint>();
            RequestCapturePermissions();
        }
        else
        {
            remoteDisplay.enabled = false;
            headsetView.InitializeDisplay(eye.transform, remoteDisplay);
            speaker = gameObject.AddComponent<AudioSource>();
            speaker.loop = true;
        }
        LoadPlayback();
    }

    private static void RequestCapturePermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        var missing = new List<string>();
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) missing.Add(Permission.Microphone);
        if (missing.Count > 0) Permission.RequestUserPermissions(missing.ToArray());
#endif
    }

    private void Update()
    {
        bool record = OVRInput.Get(OVRInput.Button.One) || (!recordingNow && QuestHands.IsPinching(true));
        bool back = OVRInput.Get(OVRInput.Button.Two) || (!recordingNow && QuestHands.IsPinching(false));
        if (!record && !back) readyForInput = true;
        if (readyForInput && back && !previousBack)
        {
            CancelRecording();
            SceneManager.LoadScene("SoloTestMenu");
            return;
        }
        if (readyForInput && record && !previousRecord)
        {
            if (recordingNow) FinishRecording();
            else StartRecording();
        }
        previousRecord = record;
        previousBack = back;

        if (waitingForSharing)
        {
            message = headsetView.Status;
            if (headsetView.IsReady)
            {
                waitingForSharing = false;
                StartRecording();
            }
            else if (Time.unscaledTime >= sharingDeadline)
            {
                waitingForSharing = false;
                headsetView.StopSharing();
                message = "Screen sharing did not start. Press record to retry.";
            }
        }
        if (microphoneDeadline > 0f)
        {
            int position = Microphone.GetPosition(null);
            if (position > 0)
            {
                microphoneStart = position;
                BeginCapture();
            }
            else if (Time.unscaledTime >= microphoneDeadline)
            {
                StopMicrophone();
                message = "Microphone did not start. Try again.";
            }
        }

        if (recordingNow)
        {
            float elapsed = Time.unscaledTime - started;
            if (elapsed >= Duration) FinishRecording();
            else if (!trainee && elapsed >= nextFrame)
            {
                nextFrame = elapsed + .05f;
                recording.Add(new Frame
                {
                    Time = elapsed,
                    Data = System.Text.Encoding.UTF8.GetBytes(QuestAnnotationEndpoint.CaptureHandFrame(eye.transform))
                });
            }
        }

        UpdatePlayback();
        bool showLocalHands = trainee || playback == null || recordingNow;
        headsetView.SetLocalHandsVisible(showLocalHands);
        statusText.gameObject.SetActive(trainee || playback == null || recordingNow);
        Transform label = statusText.transform;
        label.position = eye.transform.TransformPoint(recordingNow
            ? new Vector3(-.3f, .35f, 1.1f) : new Vector3(0f, -.4f, 1.1f));
        label.rotation = eye.transform.rotation;
        statusText.text = (trainee ? "Trainee test" : "Expert test") + "\n"
            + (recordingNow ? $"Recording {Mathf.CeilToInt(Duration - (Time.unscaledTime - started))}s"
                + (trainee ? $"  /  {recording.Count} headset frames" : "") : message)
            + (trainee ? "\n" + headsetView.Status : "")
            + (recordingNow ? "\nA: save early   B: cancel"
                : "\nA / left pinch: record   B / right pinch: menu");
        if (recordingNow)
            statusText.text = $"Recording {Mathf.CeilToInt(Duration - (Time.unscaledTime - started))}s";
    }

    private void StartRecording()
    {
        if (trainee)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                message = "Allow the microphone, then try again";
                RequestCapturePermissions();
                return;
            }
#endif
            if (!headsetView.IsReady)
            {
                if (!waitingForSharing)
                {
                    headsetView.RequestSharing();
                    waitingForSharing = true;
                    sharingDeadline = Time.unscaledTime + 60f;
                }
                message = headsetView.Status;
                return;
            }

            if (microphoneDeadline > 0f) return;
            microphone = Microphone.Start(null, true, 16, AudioRate);
            if (microphone == null) { message = "Microphone unavailable. Try again."; return; }
            microphoneDeadline = Time.unscaledTime + 3f;
            message = "Starting microphone...";
            return;
        }
        BeginCapture();
    }

    private void BeginCapture()
    {
        microphoneDeadline = 0f;
        captureGeneration++;
        recording.Clear();
        nextFrame = 0f;
        started = Time.unscaledTime;
        recordingNow = true;
    }

    private void LateUpdate()
    {
        if (!trainee || !recordingNow) return;
        float elapsed = Time.unscaledTime - started;
        if (elapsed < nextFrame || elapsed >= Duration) return;
        int generation = captureGeneration;
        nextFrame = elapsed + .1f;
        headsetView.RequestFrame(frame =>
        {
            if (recordingNow && generation == captureGeneration)
                recording.Add(new Frame { Time = elapsed, Data = frame });
        });
    }

    private void FinishRecording()
    {
        recordingNow = false;
        readyForInput = false;
        string pending = CapturePath + ".pending";
        try
        {
            float length = Mathf.Min(Time.unscaledTime - started, Duration);
            if (recording.Count == 0 || length < .2f)
            {
                message = "No frames saved. Try recording again.";
                return;
            }
            int rate = 0;
            float[] audio = Array.Empty<float>();
            if (trainee)
            {
                rate = microphone.frequency;
                int available = (Microphone.GetPosition(null) - microphoneStart + microphone.samples) % microphone.samples;
                int samples = Mathf.Min(available, Mathf.RoundToInt(length * rate));
                if (samples <= 0) { message = "No microphone audio saved. Try again."; return; }
                var interleaved = new float[samples * microphone.channels];
                if (!microphone.GetData(interleaved, microphoneStart))
                {
                    message = "Could not read microphone audio. Try again.";
                    return;
                }
                audio = new float[samples];
                for (int i = 0; i < samples; i++) audio[i] = interleaved[i * microphone.channels];
            }
            using (var writer = new BinaryWriter(File.Create(pending)))
            {
                writer.Write(length);
                writer.Write(recording.Count);
                foreach (Frame frame in recording)
                {
                    writer.Write(frame.Time);
                    writer.Write(frame.Data.Length);
                    writer.Write(frame.Data);
                }
                writer.Write(rate);
                writer.Write(audio.Length);
                foreach (float sample in audio) writer.Write(sample);
            }
            if (File.Exists(CapturePath)) File.Replace(pending, CapturePath, null);
            else File.Move(pending, CapturePath);
            message = trainee ? "Saved headset view. Open Expert to play it."
                : "Saved. Open Trainee to loop the expert hands.";
        }
        catch (IOException) { message = "Could not save the recording."; }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
            recording.Clear();
            StopMicrophone();
            headsetView.StopSharing();
        }
    }

    private float playbackLength;

    private void LoadPlayback()
    {
        string path = trainee ? HandsPath : MediaPath;
        message = trainee ? "Record headset view and microphone" : "Record expert hand movements";
        if (!File.Exists(path))
        {
            message += trainee ? " / No expert recording yet" : " / No trainee recording yet";
            return;
        }
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            playbackLength = reader.ReadSingle();
            int frames = reader.ReadInt32();
            if (!(playbackLength >= .2f && playbackLength <= Duration) || frames < 1 || frames > FrameLimit)
                throw new InvalidDataException();
            playback = new List<Frame>(frames);
            for (int i = 0; i < frames; i++)
            {
                float timestamp = reader.ReadSingle();
                int size = reader.ReadInt32();
                if (!(timestamp >= 0f && timestamp <= playbackLength) || size < 1 || size > 512 * 1024)
                    throw new InvalidDataException();
                byte[] data = reader.ReadBytes(size);
                if (data.Length != size) throw new EndOfStreamException();
                playback.Add(new Frame { Time = timestamp, Data = data });
            }
            int rate = reader.ReadInt32();
            int samples = reader.ReadInt32();
            if (!trainee)
            {
                if (rate < 8000 || rate > 96000 || samples < 1 || samples > Duration * rate)
                    throw new InvalidDataException();
                int loopSamples = Mathf.Max(samples, Mathf.RoundToInt(playbackLength * rate));
                var audio = new float[loopSamples];
                for (int i = 0; i < samples; i++) audio[i] = reader.ReadSingle();
                speaker.clip = AudioClip.Create("Recorded trainee microphone", loopSamples, 1, rate, false);
                speaker.clip.SetData(audio, 0);
                speaker.Play();
            }
            playbackStarted = Time.unscaledTime;
            message = trainee ? "Looping recorded expert hands" : "Looping the trainee headset view and audio";
        }
        catch (IOException)
        {
            playback = null;
            message = "Recording unreadable. Make a new recording.";
        }
    }

    private void UpdatePlayback()
    {
        if (playback == null) return;
        float time = speaker != null && speaker.isPlaying
            ? speaker.timeSamples / (float)speaker.clip.frequency
            : (Time.unscaledTime - playbackStarted) % playbackLength;
        int index = 0;
        while (index + 1 < playback.Count && playback[index + 1].Time <= time) index++;
        if (index == displayedFrame) return;
        displayedFrame = index;
        if (trainee)
            hands.PlayHandFrame(eye.transform, System.Text.Encoding.UTF8.GetString(playback[index].Data));
        else headsetView.ShowFrame(playback[index].Data);
    }

    private void StopMicrophone()
    {
        microphoneDeadline = 0f;
        if (microphone == null) return;
        Microphone.End(null);
        Destroy(microphone);
        microphone = null;
    }

    private void CancelRecording()
    {
        recordingNow = false;
        waitingForSharing = false;
        recording.Clear();
        StopMicrophone();
        if (headsetView != null) headsetView.StopSharing();
    }

    private void OnDestroy()
    {
        CancelRecording();
        if (speaker != null && speaker.clip != null) Destroy(speaker.clip);
    }
}
