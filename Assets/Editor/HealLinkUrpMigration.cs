using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HealLinkUrpMigration
{
    [MenuItem("HealLink/Finish URP Migration and Validate Scenes")]
    public static void Run()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory("Assets/Settings");
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/HealLinkRenderer.asset");
        if(renderer==null) { renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/Settings/HealLinkRenderer.asset"); }
        var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/HealLinkURP.asset");
        if(pipeline==null) { pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,"Assets/Settings/HealLinkURP.asset"); }
        pipeline.supportsHDR=false;pipeline.msaaSampleCount=4;
        pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;
        GraphicsSettings.defaultRenderPipeline=pipeline;
        int oldQuality=QualitySettings.GetQualityLevel();
        for(int i=0;i<QualitySettings.names.Length;i++) { QualitySettings.SetQualityLevel(i);QualitySettings.renderPipeline=pipeline; }
        QualitySettings.SetQualityLevel(oldQuality);
        Shader shader=Shader.Find("HealLink/Unlit");
        if(shader==null) throw new Exception("Missing HealLink URP shader");
        foreach(string guid in AssetDatabase.FindAssets("t:Material",new[]{"Assets/Materials/HealLinkUI"}))
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if(material.name=="RemoteCamera") continue;
            Color color=material.HasProperty("_Color")?material.GetColor("_Color"):Color.white;
            material.shader=material.name == "SceneFont" ? shader : Shader.Find("HealLink/Solid");material.SetColor("_Color",color);EditorUtility.SetDirty(material);
        }
        var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        const string fontPath="Assets/Materials/HealLinkUI/SceneFont.mat";
        var fontMaterial=AssetDatabase.LoadAssetAtPath<Material>(fontPath);
        if(fontMaterial==null) {fontMaterial=new Material(shader);AssetDatabase.CreateAsset(fontMaterial,fontPath);}
        fontMaterial.mainTexture=font.material.mainTexture;EditorUtility.SetDirty(fontMaterial);
        foreach(string name in new[]{"QuestQuest","Trainee","Expert"})
        {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity",OpenSceneMode.Single);
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var text in root.GetComponentsInChildren<TextMesh>(true)) text.GetComponent<Renderer>().sharedMaterial=fontMaterial;
                foreach(var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;
                }
                foreach(var t in root.GetComponentsInChildren<Transform>(true))
                    if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0) throw new Exception("Missing script: "+name+"/"+t.name);
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Debug.Log("HEALLINK_VALIDATED_SCENE "+name);
        }
        EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();
        foreach(var s in new[]{shader,Shader.Find("HealLink/RemoteCamera")})
            if(s==null || ShaderUtil.ShaderHasError(s)) throw new Exception("HealLink shader error");
        EditorSceneManager.OpenScene("Assets/Scenes/QuestQuest.unity",OpenSceneMode.Single);
        Debug.Log("HEALLINK_URP_MIGRATION_SUCCESS");
    }
}
