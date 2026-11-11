using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class HealLinkSceneDesign
{
    static HealLinkSceneDesign() { EditorApplication.delayCall += ApplyPending; }
    static void ApplyPending()
    {
        if (!File.Exists("Library/HealLinkDesign.pending") || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Build();
    }
    [MenuItem("HealLink/Build Scene UI")]
    public static void Build()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/QuestQuest.unity") return;
        var app = Object.FindAnyObjectByType<QuestQuestApp>();
        if (app == null) return;
        Directory.CreateDirectory("Assets/Materials/HealLinkUI");
        var serialized = new SerializedObject(app);
        var oldRoot = serialized.FindProperty("roleSelectionRoot").objectReferenceValue as GameObject;
        var oldStatus = serialized.FindProperty("statusText").objectReferenceValue as TextMesh;
        if(oldRoot != null) Object.DestroyImmediate(oldRoot);
        if(oldStatus != null) Object.DestroyImmediate(oldStatus.gameObject);
        var root = new GameObject("Role Selection");
        Transform view = Camera.main != null ? Camera.main.transform : app.transform;
        root.transform.SetPositionAndRotation(view.position + view.forward * 0.95f - view.up * .08f, view.rotation);
        Panel(root.transform,"Panel",Vector3.zero,new Vector3(1.12f,.73f,.012f),new Color(.025f,.045f,.075f));
        Label(root.transform,"Brand","HEALLINK",new Vector3(0,.28f,-.016f),.018f,new Color(.35f,.8f,1));
        Label(root.transform,"Heading","Choose your role",new Vector3(0,.195f,-.016f),.025f,Color.white);
        Card(root.transform,"Trainee",-.265f,new Color(.07f,.28f,.48f),"Share your surroundings");
        Card(root.transform,"Expert",.265f,new Color(.04f,.33f,.29f),"Guide and annotate");
        Label(root.transform,"Hint","Tap or pinch to select a role",new Vector3(0,-.26f,-.016f),.012f,new Color(.65f,.75f,.85f));
        var status = Label(null,"Status","Ready  /  Choose a role",Vector3.zero,.014f,new Color(.6f,.76f,.88f));
        status.transform.SetPositionAndRotation(view.position + view.forward * 1.25f - view.up * .58f,view.rotation);
        serialized.FindProperty("roleSelectionRoot").objectReferenceValue=root;
        serialized.FindProperty("statusText").objectReferenceValue=status;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=5; status.gameObject.layer=5; EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        if(File.Exists("Library/HealLinkDesign.pending")) File.Delete("Library/HealLinkDesign.pending");
        Selection.activeGameObject=root;
        if(SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(root.transform.position,view.rotation,1.2f);
        // Independent editor render: no Play mode, networking or microphone.
        var go=new GameObject("UI validation camera"); var camera=go.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(view.position,view.rotation);
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.012f,.02f,.035f);
        camera.cullingMask=1 << 5; camera.fieldOfView=60; camera.nearClipPlane=.01f;
        var rt=new RenderTexture(1400,1000,24);camera.targetTexture=rt;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=rt;
        var image=new Texture2D(1400,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1400,1000),0,0);image.Apply();
        File.WriteAllBytes("Logs/HealLink-UI.png",image.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(go);Object.DestroyImmediate(image);Object.DestroyImmediate(rt);
        Debug.Log("HealLink: scene UI saved and preview rendered.");
    }
    static GameObject Panel(Transform parent,string name,Vector3 pos,Vector3 size,Color color)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        string path="Assets/Materials/HealLinkUI/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("HealLink/Solid"));AssetDatabase.CreateAsset(mat,path);}mat.color=color;EditorUtility.SetDirty(mat);
        go.GetComponent<Renderer>().sharedMaterial=mat;return go;
    }
    static void Card(Transform parent,string name,float x,Color color,string description)
    {
        var card=new GameObject(name);card.transform.SetParent(parent,false);card.transform.localPosition=new Vector3(x,-.055f,-.025f);
        var shape=Panel(card.transform,"Card"+name,Vector3.zero,new Vector3(.49f,.27f,.012f),color);
        // Renderer and collider on role root are used by the existing interaction logic.
        card.AddComponent<MeshFilter>().sharedMesh=shape.GetComponent<MeshFilter>().sharedMesh;
        card.AddComponent<MeshRenderer>().sharedMaterial=shape.GetComponent<MeshRenderer>().sharedMaterial;
        card.transform.localScale=new Vector3(.49f,.27f,.012f);Object.DestroyImmediate(shape);
        card.AddComponent<BoxCollider>();
        var text=Label(card.transform,"Label",name,new Vector3(0,.15f,-.7f),.023f,Color.white);
        text.transform.localScale=new Vector3(1/.49f,1/.27f,1/.012f);
        text=Label(card.transform,"Description",description,new Vector3(0,-.23f,-.7f),.011f,new Color(.8f,.9f,.96f));
        text.transform.localScale=new Vector3(1/.49f,1/.27f,1/.012f);
    }
    static TextMesh Label(Transform parent,string name,string value,Vector3 pos,float size,Color color)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=pos;
        var text=go.AddComponent<TextMesh>();text.text=value;text.fontSize=64;text.characterSize=size * 0.32f;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=color;
        text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");go.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
        return text;
    }
}

