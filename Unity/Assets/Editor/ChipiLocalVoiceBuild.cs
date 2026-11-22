#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

internal static class ChipiLocalVoiceBuild
{
    [PostProcessBuild]
    private static void Configure(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        var plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSSpeechRecognitionUsageDescription",
            "Chipi transcribes your English practice on this iPhone.");
        plist.WriteToFile(plistPath);

        var projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projectPath);
        var framework = project.GetUnityFrameworkTargetGuid();
        project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
        project.AddFrameworkToProject(framework, "FoundationModels.framework", true);
        project.SetBuildProperty(project.GetUnityMainTargetGuid(), "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
        project.WriteToFile(projectPath);
    }
}
#endif
