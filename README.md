# HealLink Quest 3 ↔ Quest 3

Successor to [HealLink-HL2-Quest3](https://github.com/MarcSchlicker/HealLink-HL2-Quest3) .

This Unity project connects two Quest 3 headsets on the same LAN. A Trainee shares the actual headset view, including passthrough, through an explicit system screen-sharing prompt. An Expert receives that view and sends hand movements back. Both roles exchange microphone audio.

- Unity editor used for the verified build: 6000.6.7f1.
- Multiplayer scenes: QuestQuest, Trainee and Expert.
- Android package: de.heallink.questquest, version 0.2.0.
- Start with [documentation and verification status](docs/INDEX.md).
- Build through HealLink → Build Multiplayer APK in Unity.
- Both headsets need the same multiplayer version.

This is a prototype. Automated pairing/transport checks and an APK build pass. Full acceptance with two worn headsets remains open. Traffic is unencrypted and intended for a trusted test LAN. See [testing](docs/TESTING.md) and [architecture](docs/MULTIPLAYER.md) for the exact scope.

The earlier repositories document the HoloLens-to-Quest generation. This successor uses Quest-to-Quest pairing and system headset-view sharing. Embedded Meta SDK packages retain their original licences.

---

## Aktueller Multiplayer-Stand

Version 0.2.0 integriert die Headset-Bildschirmfreigabe aus dem Solo-Test in die LAN-Kopplung. Bedienung, Architektur und belegter Teststand stehen in [docs/INDEX.md](docs/INDEX.md). Die folgenden älteren Notizen beschreiben teilweise frühere Prototypen.

# HealLink QuestQuest

HealLink QuestQuest is a clean two-Quest Unity project.

When the app starts, the user chooses one of two roles:

- **Trainee** captures Quest environment depth, turns it into a point cloud, and streams it over the local network.
- **Expert** receives and renders the Trainee point cloud, can mark depth points with pinch interaction, and sends annotations back.

Both Quests must be on the same local network. Discovery is automatic through UDP broadcast.

## Depth Source

The Trainee role requires Quest environment depth. The runtime enables the Meta Environment Depth provider when that provider is present in the installed Meta XR SDK, then reads the environment depth texture exposed by the SDK. If a Meta SDK version exposes the texture through a different component, assign that texture to `QuestDepthSource.depthTextureOverride` in the inspector.

Point-cloud packets are only sent while valid Quest depth frames are available.

## Unity Version

Target Unity `6000.6.7f1`. Install this editor with Android Build Support through Unity Hub. Import, build and Meta XR compatibility must still be verified in that version.

## Random LAN pairing

Discovery protocol v2 pairs only opposite roles. Experts randomly propose to an available Trainee after collecting announcements; Trainees randomly accept one proposing Expert. Streaming starts after reciprocal announcements. Occupied devices stay reserved. Six seconds without partner announcements releases the pair and restarts matching. Audio and annotation destinations clear during search. All devices need this build.

Allow UDP 5099 (discovery), 5088 (depth), 5089 (annotations), 5090 (voice). Wi-Fi client isolation must be disabled. Only running HealLink instances are discovered.

Device checks still required: opposite-role pair; same-role devices keep searching; two Experts and two Trainees form exclusive pairs; late join does not replace partners; disconnect and reconnect recover after timeout. Check voice, depth and annotations on both Quests.

## Validation (2026-10-27)

`dotnet build Assembly-CSharp.csproj --no-restore -v quiet -clp:ErrorsOnly` passed against the installed Unity 6000.3.14f1 references, with two assembly-reference warnings. This is not a Unity 6000.6.7f1 build or APK.

`python Tests/Pairing/run.py` passed: 100 seeded four-device simulations verify convergence, exclusive pairing, timeout and replacement; same-role devices remain unpaired. The runner extracts the current matching method from the runtime source before compiling its harness. These tests simulate announcements and time; they do not test UDP transport, packet loss, Android lifecycle or Quest hardware.

## Scene UI

Open Assets/Scenes/QuestQuest.unity. Role Selection contains Trainee and Expert with TextMesh renderers and BoxColliders; Status is a separate TextMesh. These are saved scene objects, selectable and editable without Play. QuestQuest App references these objects and reuses them at runtime. The temporary editor preview has been removed. Runtime role selection still follows the headset.

## Scene design verification (2026-10-29)

QuestQuest.unity was rebuilt and saved through Unity 6000.6.7f1 itself. Persistent scene objects include a dark panel, blue Trainee card, green Expert card, role descriptions, instructions and a separate status label. Materials are saved in Assets/Materials/HealLinkUI. Runtime selection reuses the scene card renderers and colliders.

Verified the saved scene visually in the running Unity Editor and an editor camera render (Logs/HealLink-UI.png). The legacy TextMesh serialization warning was resolved by native serialization. Switched the editor workspace to Default layout after a MainToolbarWindow layout loop, then cleared historical console messages to check recurrence. This does not constitute headset or Play-mode testing. Input Manager deprecation and Meta recommended setup items remain separate follow-up work.

HealLink > Build Scene UI regenerates the default UI and replaces existing role/status objects; normal editing should use the saved scene hierarchy instead.

## Role scenes and Quest 3 passthrough

The role selection loads Trainee or Expert as a separate scene (single-scene load). TraineeSceneController starts depth capture, point-cloud sending, annotation reception and voice; ExpertSceneController starts point-cloud reception, pinch annotation and voice. Both use the shared QuestQuestApp runtime and opposite-role discovery. The role overview is visible in the editor and hides when the role starts, leaving the live status and working space.

Only Trainee uses local OVRPassthroughLayer Underlay, enables OVRManager passthrough and clears its camera transparently. Expert disables local passthrough and displays the Trainee camera on the saved Remote Trainee Camera Display object. Trainee requests HEADSET_CAMERA permission at runtime. Quest hardware validation is required.

## Comparison with the HL2–Quest3 reference

Compared against ../HealLink-HL2-Quest3-/README.md and the Quest client's QuestHandDataSender.cs and rgbd_align_ahat.cs:

| Capability | Quest–Quest state |
| --- | --- |
| Opposite-role discovery and pairing | Implemented; matching simulation tested, two-device test pending |
| Depth environment sharing | Quest depth-to-point-cloud code exists; real sensor acquisition/calibration unverified |
| Spatial pinch strokes | Sender/receiver code exists; end-to-end spatial alignment unverified |
| Voice | Raw UDP PCM prototype, not the reference WebRTC implementation |
| Local Quest passthrough | Scene configuration added; headset test pending |
| RGB camera view of remote trainee | New TCP/JPEG prototype via Meta PassthroughCameraAccess; two-device test pending |
| Remote expert hands | Both hands are shown as joint skeletons when both apps support hand sharing. The preview is head-relative and does not align the two rooms. |
| Reference HL2 PV/AHAT alignment and depth filtering | Not ported; requires Quest-specific sensor integration |

This is not yet full feature parity with the HL2–Quest3 application. Passthrough display is separate from camera capture/streaming.

### Remote camera prototype

QuestCameraStream sends the left Trainee camera at 640 x 480, up to 10 fps, JPEG quality 65, over TCP 5091. The Expert listens; only the currently paired IP is accepted. Latest-frame buffers are bounded, disconnects retry, and stale received imagery is hidden after three seconds. The screen is a monoscopic camera view, not a copy of the full stereoscopic compositor passthrough. Allow TCP 5091 in addition to the existing UDP ports. Network traffic is unencrypted and intended for a trusted test LAN.

Role scenes were copied from the Unity-serialized menu and updated offline because automatic approval review could not launch the Unity validation process. A full scene import, shader render and two-device camera test remain pending. The image/point-cloud registration and drawing over the video are not yet calibrated. The camera API is hardware-dependent; no camera stream can be verified from this desktop alone.

## URP migration (2026-10-29)

URP 17.6.0 is declared in Packages/manifest.json. Assets/Settings/HealLinkURP.asset and HealLinkRenderer.asset derive from the installed Unity 6000.6.7f1 package templates. GraphicsSettings and all six QualitySettings tiers reference the URP asset. HDR is disabled, MSAA is 4x, and alpha output is preserved for passthrough. HealLink materials and runtime shader lookups now use URP-tagged, stereo-aware shaders.

Static checks passed for the package declaration, pipeline/renderer links, all quality tiers and unique serialized IDs in the three scenes. Unity launch was not executed: automatic approval review failed due to a usage limit. Therefore no full import, shader compilation, URP visual or APK validation is claimed.

To finish and validate in Unity: let packages import, then select **HealLink > Finish URP Migration and Validate Scenes**. This saves modified scenes after Unity's save prompt, assigns the font material, adds URP camera data, checks missing scripts and shader errors, saves all three scenes and reopens QuestQuest. Successful completion writes HEALLINK_URP_MIGRATION_SUCCESS to the console. If compilation prevents the menu from appearing, the first compiler error must be resolved before proceeding. Quest hardware tests remain required.

### UI occlusion correction

Panel, CardTrainee and CardExpert now use HealLink/Solid (opaque queue, depth writes). Text and line/particle effects retain the transparent shader. RemoteCamera also writes depth. This addresses the dark panel drawing over cards/text after the first URP conversion. Material references and shader states were checked statically; a fresh editor visual check remains pending.
