using UnityEngine;

/// <summary>Starts the Expert session when this scene runs. No networking in edit mode.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(QuestQuestApp))]
public sealed class ExpertSceneController : MonoBehaviour
{
    [SerializeField] private GameObject sceneOverview;

    private void Start()
    {
        var manager = FindAnyObjectByType<OVRManager>();
        if(manager != null) manager.isInsightPassthroughEnabled = false;
        if(Camera.main != null) { Camera.main.clearFlags = CameraClearFlags.SolidColor; Camera.main.backgroundColor = new Color(.015f,.025f,.04f,1); }
        if (sceneOverview != null) sceneOverview.SetActive(false);
        GetComponent<QuestQuestApp>().StartRole(QuestRole.Expert);
    }
}
