using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Starts the Trainee session when this scene runs. No networking in edit mode.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(QuestQuestApp))]
public sealed class TraineeSceneController : MonoBehaviour
{
    [SerializeField] private GameObject sceneOverview;

    private void Start()
    {
        var manager = FindAnyObjectByType<OVRManager>();
        if (manager != null) manager.isInsightPassthroughEnabled = true;
        var rig = FindAnyObjectByType<OVRCameraRig>();
        Camera eye = rig != null && rig.centerEyeAnchor != null
            ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
        if (eye != null)
        {
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = Color.clear;
            eye.allowHDR = false;
            eye.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        }
        if (sceneOverview != null) sceneOverview.SetActive(false);
        GetComponent<QuestQuestApp>().StartRole(QuestRole.Trainee);
    }
}
