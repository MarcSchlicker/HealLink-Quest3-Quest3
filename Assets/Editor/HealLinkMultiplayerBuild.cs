using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class HealLinkMultiplayerBuild
{
    [MenuItem("HealLink/Build Multiplayer APK")]
    public static void Run()
    {
        string oldId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        string oldName = PlayerSettings.productName;
        string oldVersion = PlayerSettings.bundleVersion;
        int oldCode = PlayerSettings.Android.bundleVersionCode;
        bool oldBundle = EditorUserBuildSettings.buildAppBundle;
        try
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "de.heallink.questquest");
            PlayerSettings.productName = "HealLink QuestQuest";
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = 2;
            EditorUserBuildSettings.buildAppBundle = false;
            string output = Path.GetFullPath("Builds/HealLink-Multiplayer-0.2.0.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/QuestQuest.unity", "Assets/Scenes/Trainee.unity",
                    "Assets/Scenes/Expert.unity" },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Multiplayer build failed: " + report.summary.result);
            UnityEngine.Debug.Log("HEALLINK_MULTIPLAYER_APK_READY " + output);
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, oldId);
            PlayerSettings.productName = oldName;
            PlayerSettings.bundleVersion = oldVersion;
            PlayerSettings.Android.bundleVersionCode = oldCode;
            EditorUserBuildSettings.buildAppBundle = oldBundle;
        }
    }
}
