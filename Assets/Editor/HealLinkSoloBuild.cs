using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class HealLinkSoloBuild
{
    [MenuItem("HealLink/Build Solo Headset Test APK")]
    public static void Run()
    {
        string oldId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        string oldName = PlayerSettings.productName;
        bool oldBundle = EditorUserBuildSettings.buildAppBundle;
        try
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "de.heallink.questquest.solotest");
            PlayerSettings.productName = "HealLink Solo Test";
            EditorUserBuildSettings.buildAppBundle = false;
            string output = Path.GetFullPath("Builds/HealLink-Solo-Test.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SoloTestMenu.unity", "Assets/Scenes/SoloTestTrainee.unity",
                    "Assets/Scenes/SoloTestExpert.unity" },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Solo headset build failed: " + report.summary.result);
            UnityEngine.Debug.Log("HEALLINK_SOLO_APK_READY " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, oldId);
            PlayerSettings.productName = oldName;
            EditorUserBuildSettings.buildAppBundle = oldBundle;
        }
    }
}
