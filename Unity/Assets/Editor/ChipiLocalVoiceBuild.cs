#if UNITY_IOS
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

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
        var app = project.GetUnityMainTargetGuid();
        project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
        project.AddFrameworkToProject(framework, "FoundationModels.framework", true);
        AddSpeechPackage(project, framework, app, "https://github.com/FluidInference/FluidAudio.git",
            "e0c6103bc87a88d25276cad491c86e757f58c865", "FluidAudio");
        AddModel(project, app, path, "ChipiPocketVoice", "../../build/pocket-voice/models", ".revision",
            "Run python3 scripts/prepare_chipi_pocket_voice.py before exporting iOS.");
        if (PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK)
        {
            AddSpeechPackage(project, framework, app, "https://github.com/Jud/kokoro-coreml.git",
                "3853a83381d2fc62fa01b33fc86ce1c79f284082", "KokoroCoreML");
            AddModel(project, app, path, "ChipiNeuralVoice", "../../build/neural-voice/models-2026-03-23",
                ".chipi-model-release", "Run scripts/prepare_chipi_neural_voice.sh for the simulator comparison.");
        }
        project.SetBuildProperty(app, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
        project.WriteToFile(projectPath);
    }

    private static void AddSpeechPackage(PBXProject project, string framework, string app, string url,
        string revision, string product)
    {
        string package = project.AddRemotePackageReferenceAtRevision(url, revision);
        project.AddRemotePackageFrameworkToProject(framework, product, package, false);
        // Link in the app target to include SwiftPM resource bundles.
        project.AddRemotePackageFrameworkToProject(app, product, package, false);
    }

    private static void AddModel(PBXProject project, string app, string exportPath, string folder,
        string modelPath, string marker, string failureMessage)
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, modelPath));
        if (!File.Exists(Path.Combine(source, marker)))
            throw new InvalidOperationException(failureMessage);
        CopyDirectory(source, Path.Combine(exportPath, folder));
        string resource = project.AddFolderReference(folder, folder);
        project.AddFileToBuild(app, resource);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            if (!Path.GetFileName(file).StartsWith("."))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
#endif
