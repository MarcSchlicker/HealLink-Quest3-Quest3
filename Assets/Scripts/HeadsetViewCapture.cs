using System;
using UnityEngine;

/// <summary>Shares Horizon OS's compositor output after the user approves screen sharing.</summary>
public sealed class HeadsetViewCapture : MonoBehaviour
{
    private bool captureOwner;
    private Renderer display;
    private Texture2D image;
    private Material material;
    private Mesh screen;
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaClass bridge;
#endif

    public string Status
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (captureOwner && bridge != null) return bridge.CallStatic<string>("getStatus");
#endif
            return captureOwner ? "Screen sharing requires a Quest headset" : "Replaying the trainee headset view";
        }
    }

    public bool IsReady
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return captureOwner && bridge != null && bridge.CallStatic<bool>("isReady");
#else
            return false;
#endif
        }
    }

    public void InitializeCapture()
    {
        captureOwner = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge = new AndroidJavaClass("de.heallink.capture.HeadsetCaptureService");
#endif
    }

    public void RequestSharing()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!captureOwner || bridge == null) return;
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        bridge.CallStatic("request", activity);
#endif
    }

    public void StopSharing()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!captureOwner || bridge == null) return;
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        bridge.CallStatic("stop", activity);
#endif
    }

    public void RequestFrame(Action<byte[]> complete)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!IsReady) return;
        byte[] frame = bridge.CallStatic<byte[]>("takeFrame");
        if (frame != null && frame.Length > 0 && frame.Length <= 512 * 1024)
            complete(frame);
#endif
    }

    public void InitializeDisplay(Transform eye, Renderer target)
    {
        display = target;
        display.transform.SetParent(eye, false);
        display.transform.localPosition = Vector3.forward;
        display.transform.localRotation = Quaternion.identity;
        display.transform.localScale = Vector3.one;
        var collider = display.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;

        screen = new Mesh { name = "Trainee headset screen" };
        screen.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0),
            new Vector3(-1, 1, 0), new Vector3(1, 1, 0) };
        screen.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        screen.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        screen.RecalculateBounds();
        display.GetComponent<MeshFilter>().sharedMesh = screen;
        material = new Material(Shader.Find("HealLink/RemoteCamera"));
        material.color = Color.white;
        display.sharedMaterial = material;
        image = new Texture2D(2, 2);
        display.enabled = false;
    }

    public bool ShowFrame(byte[] bytes)
    {
        if (display == null || bytes == null || bytes.Length < 3 ||
            bytes[0] != 0xff || bytes[1] != 0xd8 || !image.LoadImage(bytes)) return false;
        Camera eye = display.GetComponentInParent<Camera>();
        if (eye == null) return false;
        float distance = Mathf.Max(1.5f, eye.nearClipPlane + .02f);
        display.transform.localPosition = Vector3.forward * distance;
        float availableHeight = Mathf.Tan(eye.fieldOfView * Mathf.Deg2Rad * .5f) * distance;
        float aspect = image.width / (float)image.height;
        float height = Mathf.Min(availableHeight, availableHeight * eye.aspect / aspect);
        display.transform.localScale = new Vector3(height * aspect, height, 1f);
        material.mainTexture = image;
        display.enabled = true;
        return true;
    }

    private void OnDestroy()
    {
        StopSharing();
#if UNITY_ANDROID && !UNITY_EDITOR
        bridge?.Dispose();
        bridge = null;
#endif
        if (screen != null) Destroy(screen);
        if (image != null) Destroy(image);
        if (material != null) Destroy(material);
    }
}
