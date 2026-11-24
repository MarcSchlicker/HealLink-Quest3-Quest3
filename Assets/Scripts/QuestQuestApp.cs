using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public enum QuestRole
{
    None,
    Trainee,
    Expert
}

[DisallowMultipleComponent]
public sealed class QuestQuestApp : MonoBehaviour
{
    public int discoveryPort = 5099;
    public int pointCloudPort = 5088;
    public int annotationPort = 5089;
    public int audioPort = 5090;

    public string PeerHost => discovery != null && discovery.Peer != null ? discovery.Peer.host : null;
    public bool PeerSupportsHeadsetView => discovery?.Peer?.headsetViewVersion == 1;
    public string PeerInstanceId => discovery?.Peer?.instanceId;
    public int PeerHeadsetViewPort => discovery?.Peer?.headsetViewPort ?? 0;
    [SerializeField] private bool enableLegacyDepthTools;
    private Camera xrCamera;
    private Transform cameraTransform;
    private QuestRole selectedRole = QuestRole.None;
    [SerializeField] private bool roleScene;
    [SerializeField] private bool soloTestMenu;
    private bool loadingScene;
    private QuestRoleSelection roleSelection;
    private QuestDiscovery discovery;
    public QuestRelayTransport Relay { get; private set; }
    public bool RoleStarted => selectedRole != QuestRole.None;
    public bool MicrophonePermissionPending => voiceChat != null && voiceChat.PermissionPending;
    private QuestDepthSource depthSource;
    private QuestPointCloudSender pointCloudSender;
    private QuestPointCloudReceiver pointCloudReceiver;
    private QuestAnnotationEndpoint annotations;
    private QuestPointCloudInteraction expertInteraction;
    private QuestVoiceChat voiceChat;
    [SerializeField] private TextMesh statusText;
    [SerializeField] private GameObject roleSelectionRoot;

    private void Awake()
    {
        EnsureQuestRig();
        CreateStatusText();
        if (!roleScene)
        {
            CreateRoleSelection();
            if (!soloTestMenu)
            {
                try
                {
                    var settings = QuestRelaySettings.Load();
                    statusText.text = settings.enabled ? "Internet / Room " + settings.room : "Local Wi-Fi";
                }
                catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException)
                {
                    statusText.text = "Relay configuration invalid. Check relay.json.";
                }
            }
        }
    }

    private void Update()
    {
        if (GetComponent<SoloQuestTest>() == null && OVRInput.GetDown(OVRInput.Button.Two))
        {
            SceneManager.LoadScene("QuestQuest");
            return;
        }
        if (selectedRole == QuestRole.None)
        {
            if (roleScene || loadingScene) return;
            QuestRole pickedRole;
            if (roleSelection != null && roleSelection.TryPickRole(cameraTransform, out pickedRole, true))
            {
                string targetScene = pickedRole == QuestRole.Trainee ? "Trainee" : "Expert";
                if (soloTestMenu) targetScene = "SoloTest" + targetScene;
                if (Application.CanStreamedLevelBeLoaded(targetScene))
                {
                    loadingScene = true;
                    SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
                }
                else Debug.LogError("Role scene missing from build: " + targetScene);
            }

            return;
        }

        UpdatePeerTargets();
        UpdateStatusText();
    }

    public void StartRole(QuestRole role)
    {
        if (!Application.isPlaying || selectedRole != QuestRole.None) return;
        if (role != QuestRole.Trainee && role != QuestRole.Expert) throw new ArgumentOutOfRangeException(nameof(role));
        selectedRole = role;
        if (roleSelection != null)
        {
            roleSelection.Hide();
        }

        QuestRelaySettings settings;
        try { settings = QuestRelaySettings.Load(); }
        catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException)
        {
            selectedRole = QuestRole.None;
            statusText.text = "Relay configuration invalid. Check relay.json.";
            return;
        }
        if (settings.enabled)
        {
            Relay = gameObject.AddComponent<QuestRelayTransport>();
            Relay.Configure(role, settings);
        }
        else
        {
            discovery = gameObject.AddComponent<QuestDiscovery>();
            discovery.Configure(role, discoveryPort, pointCloudPort, annotationPort, audioPort,
                GetComponent<QuestCameraStream>()?.port ?? 5091);
        }

        annotations = gameObject.AddComponent<QuestAnnotationEndpoint>();
        annotations.Configure(role, annotationPort, cameraTransform, null, Relay);

        voiceChat = gameObject.AddComponent<QuestVoiceChat>();
        voiceChat.Configure(audioPort, true, Relay);

        if (Relay == null && enableLegacyDepthTools && role == QuestRole.Trainee)
        {
            depthSource = gameObject.AddComponent<QuestDepthSource>();
            depthSource.referenceCamera = cameraTransform;

            pointCloudSender = gameObject.AddComponent<QuestPointCloudSender>();
            pointCloudSender.Configure(depthSource, discovery, pointCloudPort);
        }
        else if (Relay == null && enableLegacyDepthTools)
        {
            pointCloudReceiver = gameObject.AddComponent<QuestPointCloudReceiver>();
            pointCloudReceiver.Configure(cameraTransform, pointCloudPort);

            expertInteraction = gameObject.AddComponent<QuestPointCloudInteraction>();
            expertInteraction.Configure(pointCloudReceiver, annotations, cameraTransform);

            annotations.SetExpertCloud(pointCloudReceiver);
        }

        UpdateStatusText();
    }

    private void UpdatePeerTargets()
    {
        if (Relay != null) return;
        QuestPeer peer = discovery != null ? discovery.Peer : null;
        if (annotations != null)
        {
            annotations.SetRemote(peer != null ? peer.host : null, peer != null ? peer.annotationPort : 0, peer != null && peer.supportsHands);
        }

        if (voiceChat != null)
        {
            voiceChat.SetRemote(peer != null ? peer.host : null, peer != null ? peer.audioPort : 0);
        }
    }

    private void UpdateStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        string roleText = selectedRole == QuestRole.None ? "Choose a role" : selectedRole.ToString();
        string peerText = Relay != null ? Relay.Status : discovery != null && discovery.Peer != null
            ? "Connected to " + discovery.Peer.role
            : "Waiting for the other Quest";
        var video = GetComponent<QuestCameraStream>();
        bool viewing = selectedRole == QuestRole.Expert && video != null && video.HasRecentFrame;
        statusText.gameObject.SetActive(!viewing);
        statusText.text = video != null && video.IsSharing
            ? "Sharing headset view"
            : roleText + "\n" + peerText + (video != null ? "\n" + video.Status : "");
        if (voiceChat != null && (video == null || !video.IsSharing))
            statusText.text += "\n" + voiceChat.Status;
    }

    private void EnsureQuestRig()
    {
        xrCamera = Camera.main;
        if (xrCamera == null)
        {
            GameObject rig = new GameObject("Quest Camera Rig");
            GameObject trackingSpace = new GameObject("TrackingSpace");
            GameObject centerEye = new GameObject("CenterEyeAnchor");

            trackingSpace.transform.SetParent(rig.transform, false);
            centerEye.transform.SetParent(trackingSpace.transform, false);

            xrCamera = centerEye.AddComponent<Camera>();
            xrCamera.tag = "MainCamera";
            xrCamera.nearClipPlane = 0.05f;
            xrCamera.farClipPlane = 100f;
            centerEye.AddComponent<AudioListener>();
        }

        cameraTransform = xrCamera.transform;
        if (FindObjectOfType<AudioListener>() == null)
        {
            xrCamera.gameObject.AddComponent<AudioListener>();
        }
    }

    private void OnDestroy() => roleSelection?.ReleaseLaserMaterial();

    private void CreateRoleSelection()
    {
        roleSelection = new QuestRoleSelection();
        roleSelection.Create(roleSelectionRoot);
    }

    private void CreateStatusText()
    {
        if (statusText != null) return;
        GameObject statusObject = new GameObject("Status");
        statusText = statusObject.AddComponent<TextMesh>();
        statusText.anchor = TextAnchor.MiddleCenter;
        statusText.alignment = TextAlignment.Center;
        statusText.fontSize = 42;
        statusText.characterSize = 0.012f;
        statusText.color = Color.white;
        statusObject.transform.SetParent(transform, false);
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            return;
        }

        if (statusText != null)
        {
            var video = GetComponent<QuestCameraStream>();
            Vector3 offset = video != null && video.IsSharing
                ? new Vector3(-.3f, .35f, 1.1f) : new Vector3(0f, -.58f, 1.25f);
            statusText.transform.position = cameraTransform.TransformPoint(offset);
            statusText.transform.rotation = Quaternion.LookRotation(statusText.transform.position - cameraTransform.position, cameraTransform.up);
        }
    }
}

internal sealed class QuestRoleSelection
{
    private const float PanelDistance = 0.95f;
    private const float PanelVerticalOffset = -0.08f;
    private const float DirectTouchRadius = 0.055f;
    private const float RayHoverRadius = 0.055f;
    private const float MaxSelectionDistance = 2.5f;

    private readonly List<RoleButton> buttons = new List<RoleButton>(2);
    private readonly List<InputDevice> inputDevices = new List<InputDevice>(4);
    private GameObject root;
    private QuestRole hoveredRole = QuestRole.None;
    private QuestRole touchedRole = QuestRole.None;
    private bool pinchWasPressed;
    private bool touchWasPressed;
    private readonly LaserHand[] laserHands = { new LaserHand(), new LaserHand() };
    private Material laserMaterial;

    private sealed class LaserHand
    {
        public LineRenderer Line;
        public QuestRole Hovered;
        public float HoverSince;
        public bool WasPressed = true;
        public bool Armed;
    }

    public void Create(GameObject sceneRoot)
    {
        if (sceneRoot != null)
        {
            root = sceneRoot;
            foreach (QuestRole role in new[] { QuestRole.Trainee, QuestRole.Expert })
            {
                Transform button = root.transform.Find(role.ToString());
                Renderer renderer = button.GetComponent<Renderer>();
                renderer.material = new Material(renderer.sharedMaterial);
                buttons.Add(new RoleButton { role = role, renderer = renderer,
                    collider = button.GetComponent<Collider>(), baseColor = renderer.sharedMaterial.color });
            }
            return;
        }
        root = new GameObject("Role Selection");
        buttons.Add(CreateButton(QuestRole.Trainee, new Vector3(-0.31f, 0f, 0f), new Color(0.08f, 0.42f, 0.95f), "Trainee"));
        buttons.Add(CreateButton(QuestRole.Expert, new Vector3(0.31f, 0f, 0f), new Color(0.08f, 0.72f, 0.38f), "Expert"));
    }

#if UNITY_EDITOR
    public void AttachPreview(Transform parent, Transform view)
    {
        root.transform.SetParent(parent, false);
        root.transform.position = view.position + view.forward * PanelDistance + view.up * PanelVerticalOffset;
        root.transform.rotation = view.rotation;
    }
#endif

    public bool TryPickRole(Transform cameraTransform, out QuestRole role, bool usePinchLaser = false)
    {
        role = QuestRole.None;
        if (root == null || cameraTransform == null)
        {
            return false;
        }

        root.transform.position = cameraTransform.position + cameraTransform.forward * PanelDistance + cameraTransform.up * PanelVerticalOffset;
        root.transform.rotation = Quaternion.LookRotation(root.transform.position - cameraTransform.position, cameraTransform.up);

        if (usePinchLaser) return TryPickWithLaser(cameraTransform, out role);

        touchedRole = FindTouchedRole();
        hoveredRole = touchedRole != QuestRole.None ? touchedRole : FindHoveredRole(cameraTransform);

        for (int i = 0; i < buttons.Count; i++)
        {
            bool touched = buttons[i].role == touchedRole;
            buttons[i].SetState(touched || buttons[i].role == hoveredRole, touched);
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            role = QuestRole.Trainee;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            role = QuestRole.Expert;
            return true;
        }

        bool touching = touchedRole != QuestRole.None;
        bool touchStarted = touching && !touchWasPressed;
        touchWasPressed = touching;
        if (touchStarted)
        {
            role = touchedRole;
            return true;
        }

        bool pinchPressed = IsPinchPressed() || Input.GetMouseButton(0);
        bool pinchStarted = pinchPressed && !pinchWasPressed;
        pinchWasPressed = pinchPressed;

        if (hoveredRole != QuestRole.None && pinchStarted)
        {
            role = hoveredRole;
            return true;
        }

        return false;
    }

    private bool TryPickWithLaser(Transform eye, out QuestRole role)
    {
        role = QuestRole.None;
        for (int i = 0; i < laserHands.Length; i++)
        {
            LaserHand hand = laserHands[i];
            if (!TryGetAimRay(eye, i == 0, hand.WasPressed, out Ray ray, out bool pressed))
            {
                if (hand.Line != null) hand.Line.enabled = false;
                hand.Hovered = QuestRole.None;
                hand.Armed = false;
                hand.WasPressed = true;
                continue;
            }

            QuestRole hovered = QuestRole.None;
            float distance = MaxSelectionDistance;
            foreach (RoleButton button in buttons)
            {
                if (button.collider != null &&
                    button.collider.Raycast(ray, out RaycastHit hit, distance))
                {
                    distance = hit.distance;
                    hovered = button.role;
                }
            }

            float now = Time.unscaledTime;
            if (hovered != hand.Hovered)
            {
                hand.Hovered = hovered;
                hand.HoverSince = now;
                hand.Armed = false;
            }
            if (!pressed && hovered != QuestRole.None && now - hand.HoverSince >= .18f)
                hand.Armed = true;
            if (pressed && !hand.WasPressed && hand.Armed)
            {
                role = hovered;
                hand.Armed = false;
            }
            hand.WasPressed = pressed;
            DrawLaser(hand, ray, distance, hovered != QuestRole.None);
        }
        foreach (RoleButton button in buttons)
            button.SetState(button.role == laserHands[0].Hovered || button.role == laserHands[1].Hovered, false);
        return role != QuestRole.None;
    }

    private static bool TryGetAimRay(Transform eye, bool left, bool previouslyPressed, out Ray ray, out bool pressed)
    {
        Transform trackingSpace = eye.parent;
        MetaAimHand aim = left ? MetaAimHand.left : MetaAimHand.right;
        if (aim != null && aim.isTracked.isPressed)
        {
            Vector3 position = aim.devicePosition.ReadValue();
            Vector3 direction = aim.deviceRotation.ReadValue() * Vector3.forward;
            ray = trackingSpace != null
                ? new Ray(trackingSpace.TransformPoint(position), trackingSpace.TransformDirection(direction))
                : new Ray(position, direction);
            pressed = aim.indexPressed.isPressed;
            return true;
        }

        if (QuestHands.TryGetJoint(left, XRHandJointID.IndexTip, out Pose index) &&
            QuestHands.TryGetJoint(left, XRHandJointID.ThumbTip, out Pose thumb))
        {
            Vector3 position = trackingSpace != null ? trackingSpace.TransformPoint(index.position) : index.position;
            ray = new Ray(position, (position - eye.position).normalized);
            pressed = Vector3.Distance(index.position, thumb.position) <= (previouslyPressed ? .035f : .023f);
            return ray.direction.sqrMagnitude > .5f;
        }

        ray = default;
        pressed = false;
        return false;
    }

    private void DrawLaser(LaserHand hand, Ray ray, float distance, bool overRole)
    {
        if (hand.Line == null)
        {
            var laser = new GameObject("Role selection laser");
            laser.transform.SetParent(root.transform, false);
            hand.Line = laser.AddComponent<LineRenderer>();
            if (laserMaterial == null) laserMaterial = new Material(Shader.Find("HealLink/Unlit"));
            hand.Line.sharedMaterial = laserMaterial;
            hand.Line.useWorldSpace = true;
            hand.Line.positionCount = 2;
            hand.Line.startWidth = .003f;
            hand.Line.endWidth = .006f;
            hand.Line.numCapVertices = 3;
        }
        hand.Line.enabled = true;
        Color color = overRole ? Color.cyan : new Color(.25f, .65f, 1f, .55f);
        hand.Line.startColor = hand.Line.endColor = color;
        hand.Line.SetPosition(0, ray.origin);
        hand.Line.SetPosition(1, ray.GetPoint(Mathf.Max(.01f, distance - .008f)));
    }

    public void ReleaseLaserMaterial()
    {
        if (laserMaterial != null) UnityEngine.Object.Destroy(laserMaterial);
    }

    public void Hide()
    {
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    private RoleButton CreateButton(QuestRole role, Vector3 localPosition, Color color, string label)
    {
        GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = label + " Button";
        button.transform.SetParent(root.transform, false);
        button.transform.localPosition = localPosition;
        button.transform.localScale = new Vector3(0.46f, 0.24f, 0.04f);

        Renderer renderer = button.GetComponent<Renderer>();
        renderer.sharedMaterial = new Material(Shader.Find("HealLink/Solid"));
        renderer.sharedMaterial.color = color;

        GameObject textObject = new GameObject(label + " Label");
        textObject.transform.SetParent(button.transform, false);
        textObject.transform.localPosition = new Vector3(0f, 0f, -0.55f);
        textObject.transform.localRotation = Quaternion.identity;
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = 0.006f;
        text.color = Color.white;

        return new RoleButton
        {
            role = role,
            renderer = renderer,
            collider = button.GetComponent<Collider>(),
            baseColor = color
        };
    }

    private QuestRole FindTouchedRole()
    {
        QuestRole role;
        if (TryFindTouchedRole(false, out role))
        {
            return role;
        }

        if (TryFindTouchedRole(true, out role))
        {
            return role;
        }

        return QuestRole.None;
    }

    private bool TryFindTouchedRole(bool leftHand, out QuestRole role)
    {
        role = QuestRole.None;
        Pose index;
        if (!QuestHands.TryGetJoint(leftHand, XRHandJointID.IndexTip, out index))
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        for (int i = 0; i < buttons.Count; i++)
        {
            float distance;
            if (buttons[i].IsTouched(index.position, DirectTouchRadius, out distance) && distance < bestDistance)
            {
                bestDistance = distance;
                role = buttons[i].role;
            }
        }

        return role != QuestRole.None;
    }

    private QuestRole FindHoveredRole(Transform cameraTransform)
    {
        QuestRole role;
        Ray ray;

        if (TryGetControllerRay(false, out ray) && TryFindRayRole(ray, out role))
        {
            return role;
        }

        if (TryGetControllerRay(true, out ray) && TryFindRayRole(ray, out role))
        {
            return role;
        }

        if (TryGetHandRay(cameraTransform, false, out ray) && TryFindRayRole(ray, out role))
        {
            return role;
        }

        if (TryGetHandRay(cameraTransform, true, out ray) && TryFindRayRole(ray, out role))
        {
            return role;
        }

        ray = new Ray(cameraTransform.position, cameraTransform.forward);
        return TryFindRayRole(ray, out role) ? role : QuestRole.None;
    }

    private bool TryFindRayRole(Ray ray, out QuestRole role)
    {
        role = QuestRole.None;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < buttons.Count; i++)
        {
            float distance;
            if (buttons[i].TryHit(ray, RayHoverRadius, MaxSelectionDistance, out distance) && distance < bestDistance)
            {
                bestDistance = distance;
                role = buttons[i].role;
            }
        }

        return role != QuestRole.None;
    }

    private static bool TryGetHandRay(Transform cameraTransform, bool leftHand, out Ray ray)
    {
        ray = default(Ray);
        Pose index;
        if (!QuestHands.TryGetJoint(leftHand, XRHandJointID.IndexTip, out index))
        {
            return false;
        }

        Vector3 direction = index.position - cameraTransform.position;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        ray = new Ray(cameraTransform.position, direction.normalized);
        return true;
    }

    private bool TryGetControllerRay(bool leftHand, out Ray ray)
    {
        ray = default(Ray);
        inputDevices.Clear();
        InputDeviceCharacteristics handedness = leftHand ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | handedness, inputDevices);
        for (int i = 0; i < inputDevices.Count; i++)
        {
            Vector3 position;
            Quaternion rotation;
            if (inputDevices[i].TryGetFeatureValue(CommonUsages.devicePosition, out position) &&
                inputDevices[i].TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
            {
                ray = new Ray(position, rotation * Vector3.forward);
                return true;
            }
        }

        return false;
    }

    private static bool IsPinchPressed()
    {
        if (QuestHands.IsPinching(false, 0.034f) || QuestHands.IsPinching(true, 0.034f))
        {
            return true;
        }

        List<InputDevice> devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);
        for (int i = 0; i < devices.Count; i++)
        {
            bool pressed;
            if (devices[i].TryGetFeatureValue(CommonUsages.triggerButton, out pressed) && pressed)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class RoleButton
    {
        public QuestRole role;
        public Renderer renderer;
        public Collider collider;
        public Color baseColor;

        public void SetState(bool hovered, bool touched)
        {
            if (renderer != null)
            {
                float highlight = touched ? 0.65f : 0.35f;
                renderer.sharedMaterial.color = hovered ? Color.Lerp(baseColor, Color.white, highlight) : baseColor;
            }
        }

        public bool IsTouched(Vector3 worldPoint, float radius, out float distance)
        {
            distance = float.MaxValue;
            if (collider == null)
            {
                return false;
            }

            Vector3 closestPoint = collider.ClosestPoint(worldPoint);
            distance = Vector3.Distance(worldPoint, closestPoint);
            return distance <= radius;
        }

        public bool TryHit(Ray ray, float radius, float maxDistance, out float distance)
        {
            distance = float.MaxValue;
            if (collider == null)
            {
                return false;
            }

            RaycastHit hit;
            if (collider.Raycast(ray, out hit, maxDistance))
            {
                distance = hit.distance;
                return true;
            }

            Bounds bounds = collider.bounds;
            bounds.Expand(radius * 2f);
            return bounds.IntersectRay(ray, out distance) && distance <= maxDistance;
        }
    }
}

internal static class QuestHands
{
    private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();
    private static XRHandSubsystem subsystem;

    public static bool TryGetJoint(bool leftHand, XRHandJointID jointId, out Pose pose)
    {
        pose = default(Pose);
        XRHandSubsystem handSubsystem = GetSubsystem();
        if (handSubsystem == null || !handSubsystem.running)
        {
            return false;
        }

        XRHand hand = leftHand ? handSubsystem.leftHand : handSubsystem.rightHand;
        if (!hand.isTracked)
        {
            return false;
        }

        XRHandJoint joint = hand.GetJoint(jointId);
        return joint.TryGetPose(out pose);
    }

    public static bool IsPinching(bool leftHand)
    {
        return IsPinching(leftHand, 0.026f);
    }

    public static bool IsPinching(bool leftHand, float maxDistance)
    {
        Pose thumb;
        Pose index;
        return TryGetJoint(leftHand, XRHandJointID.ThumbTip, out thumb) &&
               TryGetJoint(leftHand, XRHandJointID.IndexTip, out index) &&
               Vector3.Distance(thumb.position, index.position) < maxDistance;
    }

    private static XRHandSubsystem GetSubsystem()
    {
        if (subsystem != null)
        {
            return subsystem;
        }

        Subsystems.Clear();
        SubsystemManager.GetSubsystems(Subsystems);
        for (int i = 0; i < Subsystems.Count; i++)
        {
            if (Subsystems[i] != null)
            {
                subsystem = Subsystems[i];
                return subsystem;
            }
        }

        return null;
    }
}

internal sealed class QuestDiscovery : MonoBehaviour
{
    private const string Protocol = "HealLink-QuestQuest-v2";
    private readonly object queueLock = new object();
    private readonly Queue<ReceivedMessage> queue = new Queue<ReceivedMessage>(32);
    private UdpClient socket;
    private Thread receiveThread;
    private volatile bool running;
    private string instanceId;
    private float nextBroadcastTime;
    private QuestRole role;
    private int discoveryPort;
    private int pointCloudPort;
    private int annotationPort;
    private int headsetViewPort;
    private int audioPort;

    private sealed class Candidate
    {
        public QuestPeer peer;
        public string target;
        public float seen;
    }
    private readonly Dictionary<string, Candidate> candidates = new Dictionary<string, Candidate>();
    private string targetId;
    private float targetSince;
    private float nextSelection;
    public QuestPeer Peer { get; private set; }

    private void MatchPeer()
    {
        float now = Time.unscaledTime;
        if (targetId != null)
        {
            Candidate c;
            if (!candidates.TryGetValue(targetId, out c) || now - c.seen > 6f ||
                (!string.IsNullOrEmpty(c.target) && c.target != instanceId) ||
                (Peer == null && now - targetSince > 6f) || (Peer != null && c.target != instanceId))
            {
                Peer = null;
                targetId = null;
                nextSelection = now + UnityEngine.Random.Range(0.5f, 1.5f);
                nextBroadcastTime = 0f;
            }
            else if (c.target == instanceId) Peer = c.peer;
        }
        if (targetId != null || now < nextSelection) return;
        nextSelection = now + 2f;
        List<string> available = new List<string>();
        List<string> expired = new List<string>();
        foreach (var entry in candidates)
        {
            if (now - entry.Value.seen > 6f) { expired.Add(entry.Key); continue; }
            if (role == QuestRole.Expert ? string.IsNullOrEmpty(entry.Value.target) : entry.Value.target == instanceId)
                available.Add(entry.Key);
        }
        foreach (string id in expired) candidates.Remove(id);
        if (available.Count == 0) return;
        targetId = available[UnityEngine.Random.Range(0, available.Count)];
        targetSince = now;
        nextBroadcastTime = 0f;
    }

    public void Configure(QuestRole selectedRole, int discovery, int pointCloud, int annotation, int audio, int viewPort)
    {
        role = selectedRole;
        discoveryPort = discovery;
        pointCloudPort = pointCloud;
        annotationPort = annotation;
        headsetViewPort = viewPort;
        audioPort = audio;
        instanceId = Guid.NewGuid().ToString("N");
        nextSelection = Time.unscaledTime + 2f;
        StartSocket();
    }

    private void OnDisable()
    {
        StopSocket();
    }

    private void Update()
    {
        ProcessQueue();
        MatchPeer();
        if (running && Time.unscaledTime >= nextBroadcastTime)
        {
            nextBroadcastTime = Time.unscaledTime + 1f;
            Broadcast();
        }
    }

    private void StartSocket()
    {
        socket = new UdpClient(AddressFamily.InterNetwork);
        socket.EnableBroadcast = true;
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    private void StopSocket()
    {
        running = false;
        Peer = null;
        targetId = null;
        if (socket != null)
        {
            socket.Close();
            socket = null;
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] bytes = socket.Receive(ref sender);
                lock (queueLock)
                {
                    if (bytes.Length <= 2048 && queue.Count < 256)
                        queue.Enqueue(new ReceivedMessage { endpoint = sender, json = Encoding.UTF8.GetString(bytes) });
                }
            }
            catch
            {
            }
        }
    }

    private void ProcessQueue()
    {
        while (true)
        {
            ReceivedMessage received;
            lock (queueLock)
            {
                if (queue.Count == 0)
                {
                    return;
                }

                received = queue.Dequeue();
            }

            DiscoveryPacket packet;
            try { packet = JsonUtility.FromJson<DiscoveryPacket>(received.json); }
            catch (ArgumentException) { continue; }
            if (packet == null ||
                packet.protocol != Protocol ||
                string.IsNullOrEmpty(packet.instanceId) ||
                packet.instanceId == instanceId ||
                packet.role == role.ToString())
            {
                continue;
            }

            QuestRole peerRole;
            if (!Enum.TryParse(packet.role, true, out peerRole) ||
                peerRole != (role == QuestRole.Expert ? QuestRole.Trainee : QuestRole.Expert) ||
                packet.pointCloudPort < 1 || packet.pointCloudPort > 65535 ||
                packet.annotationPort < 1 || packet.annotationPort > 65535 ||
                packet.audioPort < 1 || packet.audioPort > 65535)
            {
                continue;
            }

            candidates[packet.instanceId] = new Candidate
            {
                target = packet.targetId,
                seen = Time.unscaledTime,
                peer = new QuestPeer
                {
                    instanceId = packet.instanceId,
                    role = peerRole,
                    host = received.endpoint.Address.ToString(),
                    pointCloudPort = packet.pointCloudPort,
                    annotationPort = packet.annotationPort,
                    audioPort = packet.audioPort,
                    supportsHands = packet.supportsHands,
                    headsetViewVersion = packet.headsetViewVersion == 1 && packet.headsetViewPort > 0 && packet.headsetViewPort <= 65535 ? 1 : 0,
                    headsetViewPort = packet.headsetViewPort
                }
            };
        }
    }

    private void Broadcast()
    {
        DiscoveryPacket packet = new DiscoveryPacket
        {
            protocol = Protocol,
            instanceId = instanceId,
            role = role.ToString(),
            targetId = targetId,
            pointCloudPort = pointCloudPort,
            annotationPort = annotationPort,
            audioPort = audioPort,
            supportsHands = true,
            headsetViewVersion = 1,
            headsetViewPort = headsetViewPort
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
        try { socket.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort)); }
        catch (SocketException) { }
        foreach (IPAddress subnet in GetSubnetBroadcasts())
        {
            try { socket.Send(bytes, bytes.Length, new IPEndPoint(subnet, discoveryPort)); }
            catch (SocketException) { /* Another interface may still reach the peer. */ }
        }
    }

    private static List<IPAddress> GetSubnetBroadcasts()
    {
        var broadcasts = new List<IPAddress>();
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null) continue;
                    byte[] ip = address.Address.GetAddressBytes();
                    byte[] mask = address.IPv4Mask.GetAddressBytes();
                    for (int i = 0; i < ip.Length; i++) ip[i] = (byte)(ip[i] | (mask[i] ^ 255));
                    broadcasts.Add(new IPAddress(ip));
                }
            }
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
        return broadcasts;
    }

    private struct ReceivedMessage
    {
        public IPEndPoint endpoint;
        public string json;
    }

    [Serializable]
    private sealed class DiscoveryPacket
    {
        public string protocol;
        public string instanceId;
        public string targetId;
        public string role;
        public int pointCloudPort;
        public int annotationPort;
        public int audioPort;
        public bool supportsHands;
        public int headsetViewVersion;
        public int headsetViewPort;
    }
}

internal sealed class QuestPeer
{
    public string instanceId;
    public QuestRole role;
    public string host;
    public int pointCloudPort;
    public int annotationPort;
    public int audioPort;
    public bool supportsHands;
    public int headsetViewVersion;
    public int headsetViewPort;
}

internal sealed class QuestDepthSource : MonoBehaviour
{
    public Transform referenceCamera;
    public Texture depthTextureOverride;
    public int stride = 8;
    public int maxPoints = 2200;
    public float minDepthMeters = 0.18f;
    public float maxDepthMeters = 5.5f;
    public bool depthIsForwardDistance = true;

    private readonly List<DepthPoint> points = new List<DepthPoint>(2400);
    private readonly string[] textureNames =
    {
        "_EnvironmentDepthTexture",
        "_EnvironmentDepthTextureLeft",
        "_EnvironmentDepthTextureRight",
        "_EnvironmentDepth",
        "_SceneDepthTexture",
        "_DepthTexture",
        "_CameraDepthTexture"
    };

    private Texture2D readableTexture;
    private Color[] pixels;
    private int width;
    private int height;
    private float nextUpdateTime;
    private float nextSourceSearchTime;
    private Texture depthTexture;

    public bool HasDepthFrame { get; private set; }
    public Vector3 RelativeHeadPosition { get; private set; }
    public Quaternion RelativeHeadRotation { get; private set; } = Quaternion.identity;

    private Vector3 startPosition;
    private Quaternion startRotation = Quaternion.identity;
    private bool hasStartPose;

    private void OnEnable()
    {
        EnsureEnvironmentDepthRuntime();
    }

    private void Update()
    {
        if (referenceCamera == null && Camera.main != null)
        {
            referenceCamera = Camera.main.transform;
        }

        UpdateHeadDelta();
        if (Time.unscaledTime >= nextUpdateTime)
        {
            nextUpdateTime = Time.unscaledTime + 0.1f;
            UpdateDepthFrame();
        }
    }

    public IReadOnlyList<DepthPoint> GetPoints()
    {
        return points;
    }

    public bool TryProjectWorldPoint(Vector3 worldPoint, out Vector3 localDepthPoint)
    {
        localDepthPoint = Vector3.zero;
        if (!HasDepthFrame || referenceCamera == null)
        {
            return false;
        }

        Vector3 local = referenceCamera.InverseTransformPoint(worldPoint);
        if (local.z <= 0.001f)
        {
            return false;
        }

        Vector2 uv = LocalDirectionToUv(local.normalized);
        float depth;
        if (!TrySampleDepth(uv, out depth))
        {
            return false;
        }

        Vector3 direction = local.normalized;
        float distance = depthIsForwardDistance ? depth / Mathf.Max(0.0001f, direction.z) : depth;
        localDepthPoint = direction * distance;
        return IsFinite(localDepthPoint);
    }

    private void UpdateDepthFrame()
    {
        Texture source = ResolveDepthTexture();
        if (source == null || !CopyDepthTexture(source))
        {
            HasDepthFrame = false;
            points.Clear();
            return;
        }

        points.Clear();
        int safeStride = Mathf.Max(1, stride);
        for (int y = 0; y < height; y += safeStride)
        {
            for (int x = 0; x < width; x += safeStride)
            {
                float depth;
                Vector2 uv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                if (!TrySampleDepth(uv, out depth))
                {
                    continue;
                }

                Vector3 local = UvToLocalPoint(uv, depth);
                if (!IsFinite(local))
                {
                    continue;
                }

                float t = Mathf.InverseLerp(minDepthMeters, maxDepthMeters, depth);
                points.Add(new DepthPoint
                {
                    localPosition = local,
                    color = Color32.Lerp(new Color32(255, 56, 36, 255), new Color32(44, 185, 255, 255), t)
                });

                if (points.Count >= maxPoints)
                {
                    HasDepthFrame = true;
                    return;
                }
            }
        }

        HasDepthFrame = points.Count > 0;
    }

    private Texture ResolveDepthTexture()
    {
        if (depthTextureOverride != null)
        {
            return depthTextureOverride;
        }

        if (depthTexture != null)
        {
            return depthTexture;
        }

        if (Time.unscaledTime < nextSourceSearchTime)
        {
            return null;
        }

        nextSourceSearchTime = Time.unscaledTime + 1f;
        for (int i = 0; i < textureNames.Length; i++)
        {
            Texture texture = Shader.GetGlobalTexture(textureNames[i]);
            if (texture != null)
            {
                depthTexture = texture;
                return depthTexture;
            }
        }

        depthTexture = FindDepthTextureOnSceneComponents();
        return depthTexture;
    }

    private Texture FindDepthTextureOnSceneComponents()
    {
        MonoBehaviour[] behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || !behaviour.gameObject.scene.IsValid())
            {
                continue;
            }

            Type type = behaviour.GetType();
            string typeName = type.Name.ToLowerInvariant();
            if (!typeName.Contains("depth") && !typeName.Contains("environment"))
            {
                continue;
            }

            Texture texture = ReadTextureMember(behaviour, type);
            if (texture != null)
            {
                return texture;
            }
        }

        return null;
    }

    private static Texture ReadTextureMember(object target, Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo[] fields = type.GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            if (typeof(Texture).IsAssignableFrom(fields[i].FieldType))
            {
                Texture texture = fields[i].GetValue(target) as Texture;
                if (texture != null)
                {
                    return texture;
                }
            }
        }

        PropertyInfo[] properties = type.GetProperties(flags);
        for (int i = 0; i < properties.Length; i++)
        {
            if (properties[i].CanRead &&
                properties[i].GetIndexParameters().Length == 0 &&
                typeof(Texture).IsAssignableFrom(properties[i].PropertyType))
            {
                try
                {
                    Texture texture = properties[i].GetValue(target, null) as Texture;
                    if (texture != null)
                    {
                        return texture;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private bool CopyDepthTexture(Texture source)
    {
        if (source.width <= 0 || source.height <= 0)
        {
            return false;
        }

        if (readableTexture == null || readableTexture.width != source.width || readableTexture.height != source.height)
        {
            if (readableTexture != null)
            {
                Destroy(readableTexture);
            }

            readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture temp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(source, temp);
            RenderTexture.active = temp;
            readableTexture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            readableTexture.Apply(false, false);
            pixels = readableTexture.GetPixels();
            width = source.width;
            height = source.height;
            return pixels != null && pixels.Length == width * height;
        }
        catch
        {
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temp);
        }
    }

    private bool TrySampleDepth(Vector2 uv, out float depth)
    {
        depth = 0f;
        if (pixels == null || width <= 0 || height <= 0)
        {
            return false;
        }

        int x = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(uv.x) * (width - 1)), 0, width - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (height - 1)), 0, height - 1);
        Color c = pixels[y * width + x];
        depth = c.r;
        return depth >= minDepthMeters && depth <= maxDepthMeters && IsFinite(depth);
    }

    private Vector3 UvToLocalPoint(Vector2 uv, float depth)
    {
        Camera camera = referenceCamera != null ? referenceCamera.GetComponent<Camera>() : null;
        Vector3 direction;
        if (camera != null)
        {
            direction = referenceCamera.InverseTransformDirection(camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f)).direction).normalized;
        }
        else
        {
            direction = UvToDirection(uv);
        }

        float distance = depthIsForwardDistance ? depth / Mathf.Max(0.0001f, direction.z) : depth;
        return direction * distance;
    }

    private Vector2 LocalDirectionToUv(Vector3 direction)
    {
        Camera camera = referenceCamera != null ? referenceCamera.GetComponent<Camera>() : null;
        if (camera != null)
        {
            Vector3 worldPoint = referenceCamera.TransformPoint(direction);
            Vector3 viewport = camera.WorldToViewportPoint(worldPoint);
            return new Vector2(viewport.x, viewport.y);
        }

        float tanX = Mathf.Tan(90f * Mathf.Deg2Rad * 0.5f);
        float tanY = Mathf.Tan(70f * Mathf.Deg2Rad * 0.5f);
        return new Vector2(
            0.5f + (direction.x / Mathf.Max(0.0001f, direction.z)) / tanX * 0.5f,
            0.5f + (direction.y / Mathf.Max(0.0001f, direction.z)) / tanY * 0.5f);
    }

    private static Vector3 UvToDirection(Vector2 uv)
    {
        float x = (uv.x - 0.5f) * 2f * Mathf.Tan(90f * Mathf.Deg2Rad * 0.5f);
        float y = (uv.y - 0.5f) * 2f * Mathf.Tan(70f * Mathf.Deg2Rad * 0.5f);
        return new Vector3(x, y, 1f).normalized;
    }

    private void UpdateHeadDelta()
    {
        if (referenceCamera == null)
        {
            return;
        }

        if (!hasStartPose)
        {
            hasStartPose = true;
            startPosition = referenceCamera.position;
            startRotation = referenceCamera.rotation;
        }

        RelativeHeadRotation = Quaternion.Inverse(startRotation) * referenceCamera.rotation;
        RelativeHeadPosition = Quaternion.Inverse(startRotation) * (referenceCamera.position - startPosition);
    }

    private void EnsureEnvironmentDepthRuntime()
    {
        string[] typeNames =
        {
            "EnvironmentDepthManager",
            "EnvironmentDepthTextureProvider",
            "Meta.XR.EnvironmentDepth.EnvironmentDepthManager",
            "Meta.XR.EnvironmentDepth.EnvironmentDepthTextureProvider",
            "Meta.XR.Depth.EnvironmentDepthManager",
            "Meta.XR.Depth.EnvironmentDepthTextureProvider"
        };

        for (int i = 0; i < typeNames.Length; i++)
        {
            Type type = FindType(typeNames[i]);
            if (type == null || !typeof(Component).IsAssignableFrom(type) || GetComponent(type) != null)
            {
                continue;
            }

            Component component = gameObject.AddComponent(type);
            EnableBoolMember(component, "enableEnvironmentDepth");
            EnableBoolMember(component, "environmentDepthEnabled");
            EnableBoolMember(component, "enableDepth");
            EnableBoolMember(component, "useEnvironmentDepth");
            break;
        }
    }

    private static void EnableBoolMember(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = target.GetType();
        FieldInfo field = type.GetField(name, flags);
        if (field != null && field.FieldType == typeof(bool))
        {
            field.SetValue(target, true);
        }

        PropertyInfo property = type.GetProperty(name, flags);
        if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
        {
            property.SetValue(target, true, null);
        }
    }

    private static Type FindType(string fullOrShortName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type exact = assemblies[i].GetType(fullOrShortName);
            if (exact != null)
            {
                return exact;
            }

            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch
            {
                continue;
            }

            for (int j = 0; j < types.Length; j++)
            {
                if (types[j] != null && types[j].Name == fullOrShortName)
                {
                    return types[j];
                }
            }
        }

        return null;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}

internal struct DepthPoint
{
    public Vector3 localPosition;
    public Color32 color;
}

internal sealed class QuestPointCloudSender : MonoBehaviour
{
    private QuestDepthSource depthSource;
    private QuestDiscovery discovery;
    private UdpClient socket;
    private IPEndPoint endpoint;
    private int pointCloudPort;
    private int sequence;
    private float nextSendTime;

    public void Configure(QuestDepthSource source, QuestDiscovery networkDiscovery, int port)
    {
        depthSource = source;
        discovery = networkDiscovery;
        pointCloudPort = port;
        socket = new UdpClient();
    }

    private void OnDisable()
    {
        if (socket != null)
        {
            socket.Close();
            socket = null;
        }
    }

    private void Update()
    {
        if (socket == null || depthSource == null || !depthSource.HasDepthFrame || Time.unscaledTime < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.unscaledTime + 0.1f;
        if (discovery == null || discovery.Peer == null || discovery.Peer.role != QuestRole.Expert)
        {
            return;
        }

        endpoint = new IPEndPoint(IPAddress.Parse(discovery.Peer.host), discovery.Peer.pointCloudPort);
        SendPointCloud();
    }

    private void SendPointCloud()
    {
        IReadOnlyList<DepthPoint> points = depthSource.GetPoints();
        if (points.Count == 0)
        {
            return;
        }

        sequence++;
        int maxPerPacket = 900;
        int chunks = Mathf.CeilToInt(points.Count / (float)maxPerPacket);
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            int start = chunk * maxPerPacket;
            int count = Mathf.Min(maxPerPacket, points.Count - start);
            byte[] packet = BuildPacket(points, start, count, chunk, chunks);
            socket.Send(packet, packet.Length, endpoint);
        }
    }

    private byte[] BuildPacket(IReadOnlyList<DepthPoint> points, int start, int count, int chunk, int chunks)
    {
        using (MemoryStream stream = new MemoryStream(64 + count * 10))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((byte)'H');
            writer.Write((byte)'Q');
            writer.Write((byte)'P');
            writer.Write((byte)'C');
            writer.Write(sequence);
            writer.Write((ushort)chunk);
            writer.Write((ushort)chunks);
            writer.Write((ushort)count);
            WriteVector3(writer, depthSource.RelativeHeadPosition);
            WriteQuaternion(writer, depthSource.RelativeHeadRotation);

            for (int i = 0; i < count; i++)
            {
                DepthPoint point = points[start + i];
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(point.localPosition.x * 1000f), short.MinValue, short.MaxValue));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(point.localPosition.y * 1000f), short.MinValue, short.MaxValue));
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(point.localPosition.z * 1000f), short.MinValue, short.MaxValue));
                writer.Write(point.color.r);
                writer.Write(point.color.g);
                writer.Write(point.color.b);
                writer.Write(point.color.a);
            }

            return stream.ToArray();
        }
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }
}

internal sealed class QuestPointCloudReceiver : MonoBehaviour
{
    private readonly object queueLock = new object();
    private readonly Queue<byte[]> queue = new Queue<byte[]>(64);
    private readonly List<DepthPoint> points = new List<DepthPoint>(14000);
    private readonly Dictionary<int, FrameAssembly> frames = new Dictionary<int, FrameAssembly>();
    private UdpClient socket;
    private Thread receiveThread;
    private volatile bool running;
    private Transform cameraTransform;
    private GameObject root;
    private ParticleSystem particles;
    private ParticleSystem.Particle[] particleBuffer;
    private Material particleMaterial;
    private int listenPort;
    private Vector3 traineeHeadPosition;
    private Quaternion traineeHeadRotation = Quaternion.identity;

    public Transform CloudRoot
    {
        get
        {
            EnsureRenderer();
            return root.transform;
        }
    }

    public float PointScale
    {
        get { return 1f; }
    }

    public void Configure(Transform camera, int port)
    {
        cameraTransform = camera;
        listenPort = port;
        EnsureRenderer();
        StartReceiver();
    }

    private void OnDisable()
    {
        running = false;
        if (socket != null)
        {
            socket.Close();
            socket = null;
        }
    }

    private void Update()
    {
        ProcessQueue();
    }

    private void LateUpdate()
    {
        if (root == null || cameraTransform == null)
        {
            return;
        }

        root.transform.position = cameraTransform.position + cameraTransform.forward * 1.35f + cameraTransform.up * -0.08f;
        root.transform.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up) * traineeHeadRotation;
        root.transform.position += root.transform.rotation * (traineeHeadPosition * 0.25f);
    }

    public bool TryFindClosestPoint(Ray worldRay, out Vector3 worldPoint, out Vector3 cloudLocalPoint)
    {
        worldPoint = Vector3.zero;
        cloudLocalPoint = Vector3.zero;
        if (root == null || points.Count == 0)
        {
            return false;
        }

        float bestScore = 0.045f;
        bool found = false;
        for (int i = 0; i < points.Count; i += 2)
        {
            Vector3 candidate = root.transform.TransformPoint(points[i].localPosition);
            Vector3 toCandidate = candidate - worldRay.origin;
            float forward = Vector3.Dot(toCandidate, worldRay.direction);
            if (forward < 0f)
            {
                continue;
            }

            Vector3 closest = worldRay.origin + worldRay.direction * forward;
            float distance = Vector3.Distance(candidate, closest);
            if (distance < bestScore)
            {
                bestScore = distance;
                worldPoint = candidate;
                cloudLocalPoint = points[i].localPosition;
                found = true;
            }
        }

        return found;
    }

    private void StartReceiver()
    {
        socket = new UdpClient(listenPort);
        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] packet = socket.Receive(ref sender);
                lock (queueLock)
                {
                    queue.Enqueue(packet);
                }
            }
            catch
            {
            }
        }
    }

    private void ProcessQueue()
    {
        while (true)
        {
            byte[] packet;
            lock (queueLock)
            {
                if (queue.Count == 0)
                {
                    return;
                }

                packet = queue.Dequeue();
            }

            PacketChunk chunk;
            if (!TryReadChunk(packet, out chunk))
            {
                continue;
            }

            FrameAssembly frame;
            if (!frames.TryGetValue(chunk.sequence, out frame))
            {
                frame = new FrameAssembly(chunk.chunkCount);
                frames[chunk.sequence] = frame;
            }

            frame.Add(chunk);
            if (frame.Complete)
            {
                points.Clear();
                points.AddRange(frame.points);
                traineeHeadPosition = frame.headPosition;
                traineeHeadRotation = frame.headRotation;
                frames.Clear();
                UpdateParticles();
            }
        }
    }

    private bool TryReadChunk(byte[] data, out PacketChunk chunk)
    {
        chunk = null;
        if (data == null || data.Length < 36)
        {
            return false;
        }

        using (MemoryStream stream = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            if (reader.ReadByte() != (byte)'H' || reader.ReadByte() != (byte)'Q' || reader.ReadByte() != (byte)'P' || reader.ReadByte() != (byte)'C')
            {
                return false;
            }

            chunk = new PacketChunk();
            chunk.sequence = reader.ReadInt32();
            chunk.chunkIndex = reader.ReadUInt16();
            chunk.chunkCount = reader.ReadUInt16();
            int count = reader.ReadUInt16();
            chunk.headPosition = ReadVector3(reader);
            chunk.headRotation = ReadQuaternion(reader);
            chunk.points = new List<DepthPoint>(count);

            for (int i = 0; i < count && stream.Position + 10 <= stream.Length; i++)
            {
                DepthPoint point = new DepthPoint
                {
                    localPosition = new Vector3(reader.ReadInt16() / 1000f, reader.ReadInt16() / 1000f, reader.ReadInt16() / 1000f),
                    color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte())
                };
                chunk.points.Add(point);
            }

            return true;
        }
    }

    private void EnsureRenderer()
    {
        if (root != null)
        {
            return;
        }

        root = new GameObject("Trainee Point Cloud");
        root.transform.SetParent(transform, false);
        particles = root.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 14000;
        main.startLifetime = 10f;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        Shader shader = Shader.Find("HealLink/Unlit");
        particleMaterial = new Material(shader != null ? shader : Shader.Find("HealLink/Unlit"));
        renderer.material = particleMaterial;
        particles.Play();
    }

    private void UpdateParticles()
    {
        EnsureRenderer();
        if (particleBuffer == null || particleBuffer.Length < points.Count)
        {
            particleBuffer = new ParticleSystem.Particle[points.Count];
        }

        for (int i = 0; i < points.Count; i++)
        {
            particleBuffer[i].position = points[i].localPosition;
            particleBuffer[i].startColor = points[i].color;
            particleBuffer[i].startSize = 0.012f;
            particleBuffer[i].startLifetime = 10f;
            particleBuffer[i].remainingLifetime = 10f;
        }

        particles.SetParticles(particleBuffer, points.Count);
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static Quaternion ReadQuaternion(BinaryReader reader)
    {
        return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private sealed class PacketChunk
    {
        public int sequence;
        public int chunkIndex;
        public int chunkCount;
        public Vector3 headPosition;
        public Quaternion headRotation;
        public List<DepthPoint> points;
    }

    private sealed class FrameAssembly
    {
        private readonly bool[] received;
        public readonly List<DepthPoint> points = new List<DepthPoint>(14000);
        public Vector3 headPosition;
        public Quaternion headRotation;
        private int count;

        public bool Complete
        {
            get { return count >= received.Length; }
        }

        public FrameAssembly(int chunkCount)
        {
            received = new bool[Mathf.Max(1, chunkCount)];
        }

        public void Add(PacketChunk chunk)
        {
            int index = Mathf.Clamp(chunk.chunkIndex, 0, received.Length - 1);
            if (received[index])
            {
                return;
            }

            received[index] = true;
            count++;
            headPosition = chunk.headPosition;
            headRotation = chunk.headRotation;
            points.AddRange(chunk.points);
        }
    }
}

internal sealed class QuestPointCloudInteraction : MonoBehaviour
{
    private QuestPointCloudReceiver cloud;
    private QuestAnnotationEndpoint annotations;
    private Transform cameraTransform;
    private GameObject marker;
    private bool leftPinchWasActive;
    private Vector3 lastLocalPoint;

    public void Configure(QuestPointCloudReceiver receiver, QuestAnnotationEndpoint endpoint, Transform camera)
    {
        cloud = receiver;
        annotations = endpoint;
        cameraTransform = camera;
        marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Depth Marker";
        marker.transform.localScale = Vector3.one * 0.035f;
        UnityEngine.Object.Destroy(marker.GetComponent<Collider>());
        marker.SetActive(false);
    }

    private void Update()
    {
        if (cloud == null || cameraTransform == null)
        {
            return;
        }

        Pose indexPose;
        bool hasFinger = QuestHands.TryGetJoint(false, XRHandJointID.IndexTip, out indexPose);
        Ray ray = hasFinger
            ? new Ray(cameraTransform.position, (indexPose.position - cameraTransform.position).normalized)
            : new Ray(cameraTransform.position, cameraTransform.forward);

        Vector3 worldPoint;
        Vector3 localPoint;
        bool hasDepthPoint = cloud.TryFindClosestPoint(ray, out worldPoint, out localPoint);
        marker.SetActive(hasDepthPoint);
        if (hasDepthPoint)
        {
            marker.transform.position = worldPoint;
            lastLocalPoint = localPoint;
        }

        bool pinching = QuestHands.IsPinching(true);
        if (pinching && hasDepthPoint)
        {
            if (!leftPinchWasActive)
            {
                annotations.SendStroke("begin", lastLocalPoint);
            }
            else
            {
                annotations.SendStroke("point", lastLocalPoint);
            }
        }
        else if (leftPinchWasActive)
        {
            annotations.SendStroke("end", lastLocalPoint);
        }

        leftPinchWasActive = pinching && hasDepthPoint;
    }
}

internal sealed class QuestAnnotationEndpoint : MonoBehaviour
{
    private readonly object queueLock = new object();
    private readonly Queue<string> queue = new Queue<string>(32);
    private readonly Dictionary<string, LineRenderer> lines = new Dictionary<string, LineRenderer>();
    private UdpClient receiver;
    private UdpClient sender;
    private Thread receiveThread;
    private volatile bool running;
    private QuestRole role;
    private QuestRelayTransport relay;
    private Transform traineeCamera;
    private QuestPointCloudReceiver expertCloud;
    private volatile IPEndPoint remote;
    private readonly LineRenderer[,] handLines = new LineRenderer[2, 5];
    private bool remoteSupportsHands;
    private float nextHandSend;
    private float lastHandFrame = -10f;
    private static readonly XRHandJointID[,] FingerJoints =
    {
        { XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
        { XRHandJointID.Wrist, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
        { XRHandJointID.Wrist, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
        { XRHandJointID.Wrist, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
        { XRHandJointID.Wrist, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip }
    };
    private int nextStrokeId;
    private string activeStrokeId;
    private Material lineMaterial;

    public void Configure(QuestRole selectedRole, int listenPort, Transform camera, QuestPointCloudReceiver cloud, QuestRelayTransport relayTransport = null)
    {
        role = selectedRole;
        traineeCamera = camera;
        expertCloud = cloud;
        relay = relayTransport;
        if (relay != null)
        {
            relay.HandsReceived += ReceiveRelayedHands;
            relay.ConnectionChanged += ClearRelayedHands;
            return;
        }
        sender = new UdpClient();
        receiver = new UdpClient(listenPort);
        running = true;
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    public void SetExpertCloud(QuestPointCloudReceiver cloud)
    {
        expertCloud = cloud;
    }

    public void SetRemote(string host, int port, bool supportsHands)
    {
        remoteSupportsHands = supportsHands;
        if (string.IsNullOrEmpty(host))
        {
            remote = null;
            return;
        }

        remote = new IPEndPoint(IPAddress.Parse(host), port);
    }

    private void SendHands()
    {
        if ((relay != null ? !relay.IsPaired : remote == null || !remoteSupportsHands) ||
            traineeCamera == null || Time.unscaledTime < nextHandSend)
            return;

        nextHandSend = Time.unscaledTime + 1f / 20f;
        byte[] bytes = Encoding.UTF8.GetBytes(CaptureHandFrame(traineeCamera));
        if (relay != null) { relay.SendHands(bytes); return; }
        try { sender.Send(bytes, bytes.Length, remote); }
        catch (SocketException) { }
    }

    internal static string CaptureHandFrame(Transform eye)
    {
        var packet = new AnnotationPacket { action = "hands", joints = new Vector3[50] };
        Transform trackingSpace = eye.parent;
        for (int hand = 0; hand < 2; hand++)
        for (int finger = 0; finger < 5; finger++)
        {
            bool tracked = true;
            int index = hand * 5 + finger;
            for (int joint = 0; joint < 5; joint++)
            {
                if (!QuestHands.TryGetJoint(hand == 0, FingerJoints[finger, joint], out Pose pose))
                {
                    tracked = false;
                    break;
                }

                Vector3 world = trackingSpace != null ? trackingSpace.TransformPoint(pose.position) : pose.position;
                packet.joints[index * 5 + joint] = eye.InverseTransformPoint(world);
            }
            if (tracked) packet.trackedFingers |= 1 << index;
        }

        return JsonUtility.ToJson(packet);
    }

    internal void PlayHandFrame(Transform eye, string json)
    {
        traineeCamera = eye;
        var packet = JsonUtility.FromJson<AnnotationPacket>(json);
        ShowHands(packet?.joints?.Length == 50 ? packet : null);
    }

    private void ShowHands(AnnotationPacket packet)
    {
        lastHandFrame = Time.unscaledTime;
        for (int hand = 0; hand < 2; hand++)
        for (int finger = 0; finger < 5; finger++)
        {
            int index = hand * 5 + finger;
            bool tracked = packet != null && (packet.trackedFingers & (1 << index)) != 0;
            LineRenderer line = handLines[hand, finger];
            if (tracked && line == null)
            {
                var obj = new GameObject("Expert hand finger");
                obj.transform.SetParent(traineeCamera, false);
                line = obj.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.sharedMaterial = GetLineMaterial();
                line.startColor = line.endColor = Color.cyan;
                line.startWidth = line.endWidth = 0.008f;
                line.numCapVertices = 4;
                line.positionCount = 5;
                handLines[hand, finger] = line;
            }
            if (line == null) continue;
            line.enabled = tracked;
            if (!tracked) continue;
            for (int joint = 0; joint < 5; joint++)
                line.SetPosition(joint, packet.joints[index * 5 + joint]);
        }
    }

    public void SendStroke(string action, Vector3 localPoint)
    {
        if (remote == null || sender == null)
        {
            return;
        }

        if (action == "begin" || string.IsNullOrEmpty(activeStrokeId))
        {
            activeStrokeId = "stroke-" + (++nextStrokeId);
        }

        AnnotationPacket packet = new AnnotationPacket
        {
            id = activeStrokeId,
            action = action,
            localPosition = localPoint,
            color = Color.red,
            width = 0.012f
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
        sender.Send(bytes, bytes.Length, remote);

        Apply(packet);
        if (action == "end")
        {
            activeStrokeId = null;
        }
    }

    private void ReceiveRelayedHands(byte[] bytes)
    {
        lock (queueLock)
        {
            if (queue.Count >= 32) queue.Dequeue();
            queue.Enqueue(Encoding.UTF8.GetString(bytes));
        }
    }

    private void ClearRelayedHands()
    {
        lock (queueLock) queue.Clear();
        ShowHands(null);
    }

    private void OnDisable()
    {
        if (relay != null)
        {
            relay.HandsReceived -= ReceiveRelayedHands;
            relay.ConnectionChanged -= ClearRelayedHands;
        }
        running = false;
        if (receiver != null)
        {
            receiver.Close();
            receiver = null;
        }

        if (sender != null)
        {
            sender.Close();
            sender = null;
        }
    }

    private void Update()
    {
        if (role == QuestRole.Expert) SendHands();
        if (role == QuestRole.Trainee && ((relay != null ? !relay.IsPaired : remote == null) || Time.unscaledTime - lastHandFrame > 0.35f))
            ShowHands(null);

        while (true)
        {
            string json;
            lock (queueLock)
            {
                if (queue.Count == 0)
                {
                    return;
                }

                json = queue.Dequeue();
            }

            AnnotationPacket packet;
            try { packet = JsonUtility.FromJson<AnnotationPacket>(json); }
            catch (ArgumentException) { continue; }
            if (packet == null || (relay != null ? !relay.IsPaired : remote == null)) continue;
            if (packet.action == "hands")
            {
                if (role == QuestRole.Trainee && traineeCamera != null && packet.joints?.Length == 50)
                    ShowHands(packet);
            }
            else if (!string.IsNullOrEmpty(packet.id))
                Apply(packet);
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint senderEndpoint = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] bytes = receiver.Receive(ref senderEndpoint);
                var peer = remote;
                if (peer == null || !peer.Address.Equals(senderEndpoint.Address) || bytes.Length > 8192)
                    continue;
                lock (queueLock)
                {
                    if (queue.Count >= 32) queue.Dequeue();
                    queue.Enqueue(Encoding.UTF8.GetString(bytes));
                }
            }
            catch
            {
            }
        }
    }

    private void Apply(AnnotationPacket packet)
    {
        Vector3 worldPoint = LocalToWorld(packet.localPosition);
        LineRenderer line;
        if (!lines.TryGetValue(packet.id, out line) || line == null || packet.action == "begin")
        {
            GameObject lineObject = new GameObject("Annotation " + packet.id);
            line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.startWidth = packet.width;
            line.endWidth = packet.width;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.material = GetLineMaterial();
            line.startColor = packet.color;
            line.endColor = packet.color;
            lines[packet.id] = line;
        }

        int index = line.positionCount;
        line.positionCount = index + 1;
        line.SetPosition(index, worldPoint);
    }

    private Vector3 LocalToWorld(Vector3 localPosition)
    {
        if (role == QuestRole.Expert && expertCloud != null)
        {
            return expertCloud.CloudRoot.TransformPoint(localPosition);
        }

        if (traineeCamera != null)
        {
            return traineeCamera.TransformPoint(localPosition);
        }

        return localPosition;
    }

    private Material GetLineMaterial()
    {
        if (lineMaterial != null)
        {
            return lineMaterial;
        }

        Shader shader = Shader.Find("HealLink/Unlit");
        lineMaterial = new Material(shader != null ? shader : Shader.Find("HealLink/Unlit"));
        return lineMaterial;
    }

    [Serializable]
    private sealed class AnnotationPacket
    {
        public string id;
        public string action;
        public Vector3 localPosition;
        public Color color;
        public float width;
        public Vector3[] joints;
        public int trackedFingers;
    }
}

internal sealed class QuestVoiceChat : MonoBehaviour
{
    private readonly object sampleLock = new object();
    private readonly Queue<float> playback = new Queue<float>(16000);
    private QuestRelayTransport relay;
    private UdpClient sender;
    private UdpClient receiver;
    private volatile IPEndPoint remote;
    private Thread receiveThread;
    private volatile bool running;
    private AudioClip microphoneClip;
    private AudioSource output;
    private int lastMicPosition;
    private int sequence;
    private float nextMicAttempt;
    private bool buffering = true;
    public bool PermissionPending { get; private set; }
    private volatile bool receivedVoice;
    public string Status => microphoneClip == null ? "Voice: waiting for microphone"
        : receivedVoice ? "Voice: receiving audio" : "Voice: microphone ready";

    public void Configure(int listenPort, bool requestPermission, QuestRelayTransport relayTransport = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (requestPermission && !Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            PermissionPending = true;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => PermissionPending = false;
            callbacks.PermissionDenied += _ => PermissionPending = false;
            callbacks.PermissionDeniedAndDontAskAgain += _ => PermissionPending = false;
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
        }
#endif
        relay = relayTransport;
        if (relay != null)
        {
            relay.AudioReceived += ReceiveAudio;
            relay.ConnectionChanged += ClearPlayback;
        }
        else
        {
            sender = new UdpClient();
            receiver = new UdpClient(listenPort);
            running = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
        }

        output = gameObject.AddComponent<AudioSource>();
        output.loop = true;
        output.playOnAwake = false;
        output.spatialBlend = 0f;
        output.clip = AudioClip.Create("Remote Voice", 16000, 1, 16000, true, FillAudio);
        output.Play();
    }

    public void SetRemote(string host, int port)
    {
        var endpoint = string.IsNullOrEmpty(host) ? null : new IPEndPoint(IPAddress.Parse(host), port);
        if (Equals(remote, endpoint)) return;
        lock (sampleLock)
        {
            remote = endpoint;
            playback.Clear();
            buffering = true;
            receivedVoice = false;
        }
        if (microphoneClip != null) lastMicPosition = Mathf.Max(0, Microphone.GetPosition(null));
    }

    private void ClearPlayback()
    {
        lock (sampleLock)
        {
            playback.Clear();
            buffering = true;
            receivedVoice = false;
        }
        if (microphoneClip != null) lastMicPosition = Mathf.Max(0, Microphone.GetPosition(null));
    }

    private void OnDisable()
    {
        if (relay != null)
        {
            relay.AudioReceived -= ReceiveAudio;
            relay.ConnectionChanged -= ClearPlayback;
        }
        running = false;
        if (receiver != null)
        {
            receiver.Close();
            receiver = null;
        }

        if (sender != null)
        {
            sender.Close();
            sender = null;
        }

        if (microphoneClip != null)
        {
            Microphone.End(null);
            microphoneClip = null;
        }
    }

    private void Update()
    {
        if (microphoneClip == null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) return;
#endif
            if (Time.unscaledTime < nextMicAttempt) return;
            nextMicAttempt = Time.unscaledTime + 1f;
            microphoneClip = Microphone.Start(null, true, 2, 16000);
            lastMicPosition = 0;
            if (microphoneClip == null) return;
        }

        if (relay != null ? !relay.IsPaired : remote == null || sender == null) return;
        int position = Microphone.GetPosition(null);
        if (position < 0) return;
        int available = (position - lastMicPosition + microphoneClip.samples) % microphoneClip.samples;
        const int samples = 320;
        if (available > 1600)
        {
            lastMicPosition = (position - samples + microphoneClip.samples) % microphoneClip.samples;
            available = samples;
        }
        while (available >= samples)
        {
            SendMicrophoneFrame(samples);
            available -= samples;
        }
    }

    private void SendMicrophoneFrame(int samples)
    {
        float[] data = new float[samples * microphoneClip.channels];
        if (!microphoneClip.GetData(data, lastMicPosition)) return;
        lastMicPosition = (lastMicPosition + samples) % microphoneClip.samples;

        using (MemoryStream stream = new MemoryStream(16 + samples * 2))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((byte)'H');
            writer.Write((byte)'Q');
            writer.Write((byte)'A');
            writer.Write((byte)'1');
            writer.Write(sequence++);
            writer.Write((ushort)samples);
            for (int i = 0; i < samples; i++)
            {
                short value = (short)Mathf.Clamp(Mathf.RoundToInt(data[i * microphoneClip.channels] * short.MaxValue), short.MinValue, short.MaxValue);
                writer.Write(value);
            }

            byte[] bytes = stream.ToArray();
            if (relay != null) { relay.SendAudio(bytes); return; }
            try { sender.Send(bytes, bytes.Length, remote); }
            catch (SocketException) { }
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] bytes = receiver.Receive(ref endpoint);
                var peer = remote;
                if (peer == null || !peer.Address.Equals(endpoint.Address) || bytes.Length < 10 || bytes.Length > 1290)
                    continue;
                DecodeAudio(bytes, peer);
            }
            catch
            {
            }
        }
    }

    private void ReceiveAudio(byte[] bytes) => DecodeAudio(bytes, null);

    private void DecodeAudio(byte[] bytes, IPEndPoint expectedPeer)
    {
        if (bytes == null || bytes.Length < 10 || bytes.Length > 1290) return;
        using (MemoryStream stream = new MemoryStream(bytes))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            if (reader.ReadByte() != (byte)'H' || reader.ReadByte() != (byte)'Q' || reader.ReadByte() != (byte)'A' || reader.ReadByte() != (byte)'1')
            {
                return;
            }

            reader.ReadInt32();
            int count = reader.ReadUInt16();
            if (count == 0 || bytes.Length != 10 + count * 2) return;
            lock (sampleLock)
            {
                if (expectedPeer != null && !Equals(expectedPeer, remote)) return;
                receivedVoice = true;
                for (int i = 0; i < count && stream.Position + 2 <= stream.Length; i++)
                {
                    playback.Enqueue(reader.ReadInt16() / 32768f);
                }

                while (playback.Count > 4800)
                {
                    playback.Dequeue();
                }
            }
        }
    }

    private void FillAudio(float[] data)
    {
        lock (sampleLock)
        {
            if (buffering && playback.Count < Mathf.Max(1600, data.Length))
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            buffering = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (playback.Count == 0) buffering = true;
                data[i] = buffering ? 0f : playback.Dequeue();
            }
        }
    }
}

