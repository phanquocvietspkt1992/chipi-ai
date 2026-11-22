using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class ChipiModelImport : AssetPostprocessor
{
    private Material OnAssignMaterialModel(Material material, Renderer renderer)
    {
        if (assetPath == "Assets/Resources/Chipi.fbx" && material.name.StartsWith("ChipiProjection"))
            return AssetDatabase.LoadAssetAtPath<Material>("Assets/ChipiProjection.mat");
        return null;
    }

    private void OnPreprocessModel()
    {
        if (assetPath != "Assets/Resources/Chipi.fbx") return;
        var model = (ModelImporter)assetImporter;
        model.animationType = ModelImporterAnimationType.Legacy;
        model.importAnimation = true;
        model.importCameras = false;
        model.importLights = false;
    }

    private void OnPostprocessAnimation(GameObject model, AnimationClip clip)
    {
        if (assetPath != "Assets/Resources/Chipi.fbx") return;
        clip.wrapMode = clip.name.EndsWith("Idle") || clip.name.EndsWith("Listening") || clip.name.EndsWith("Talking")
            ? WrapMode.Loop : WrapMode.Once;
    }
}

public static class ChipiBuild
{
    public static void CheckVoiceEndpoint()
    {
        foreach (float background in new[] { .002f, .0065f, .012f })
        {
            var endpoint = new VoiceEndpoint();
            for (int frame = 0; frame < 50; frame++) endpoint.Observe(background);
            if (endpoint.HasSpeech) throw new InvalidOperationException($"Background detected as speech: {background}");
            for (int frame = 0; frame < 30; frame++) endpoint.Observe(.06f);
            if (!endpoint.HasSpeech) throw new InvalidOperationException($"Speech not detected over background: {background}");
            for (int frame = 0; frame < 40; frame++) endpoint.Observe(background);
            if (endpoint.Ended) throw new InvalidOperationException($"Short pause ended speech: {background}");
            for (int frame = 0; frame < 10; frame++) endpoint.Observe(.06f);
            for (int frame = 0; frame < VoiceEndpoint.SilenceMilliseconds / VoiceEndpoint.FrameMilliseconds; frame++)
                endpoint.Observe(background);
            if (!endpoint.Ended) throw new InvalidOperationException($"Background prevented end of speech: {background}");
        }
        var immediate = new VoiceEndpoint();
        for (int frame = 0; frame < 30; frame++) immediate.Observe(.06f);
        if (!immediate.HasSpeech) throw new InvalidOperationException("Immediate speech was not detected.");
        for (int frame = 0; frame < VoiceEndpoint.SilenceMilliseconds / VoiceEndpoint.FrameMilliseconds; frame++)
            immediate.Observe(.0065f);
        if (!immediate.Ended) throw new InvalidOperationException("Immediate speech did not end over background noise.");
        var quieter = new VoiceEndpoint();
        for (int frame = 0; frame < 30; frame++) quieter.Observe(.06f);
        for (int frame = 0; frame < 70; frame++) quieter.Observe(.008f);
        if (quieter.Ended) throw new InvalidOperationException("Quieter continued speech ended early.");
        var calibratedQuiet = new VoiceEndpoint();
        for (int frame = 0; frame < 50; frame++) calibratedQuiet.Observe(.0032f);
        for (int frame = 0; frame < 30; frame++) calibratedQuiet.Observe(.06f);
        for (int frame = 0; frame < 70; frame++) calibratedQuiet.Observe(.0075f);
        if (calibratedQuiet.Ended) throw new InvalidOperationException("Calibrated quieter speech ended early.");
        var noisyQuietSpeech = new VoiceEndpoint();
        for (int frame = 0; frame < 50; frame++) noisyQuietSpeech.Observe(.0244f);
        if (noisyQuietSpeech.HasSpeech) throw new InvalidOperationException("Steady noisy background was detected as speech.");
        for (int frame = 0; frame < 30; frame++) noisyQuietSpeech.Observe(.0432f);
        if (!noisyQuietSpeech.HasSpeech) throw new InvalidOperationException("Quiet speech over noisy background was missed.");
        for (int frame = 0; frame < VoiceEndpoint.SilenceMilliseconds / VoiceEndpoint.FrameMilliseconds; frame++)
            noisyQuietSpeech.Observe(.0244f);
        if (!noisyQuietSpeech.Ended) throw new InvalidOperationException("Quiet speech did not end over noisy background.");
        var noiseShift = new VoiceEndpoint();
        for (int frame = 0; frame < 50; frame++) noiseShift.Observe(.0032f);
        for (int frame = 0; frame < 30; frame++) noiseShift.Observe(.06f);
        if (!noiseShift.HasSpeech) throw new InvalidOperationException("Speech was not detected before background changed.");
        for (int frame = 0; frame < VoiceEndpoint.SilenceMilliseconds / VoiceEndpoint.FrameMilliseconds; frame++)
            noiseShift.Observe(.0065f);
        if (!noiseShift.Ended) throw new InvalidOperationException("A small rise in background noise prevented the turn ending.");
        var fixture = Resources.Load<AudioClip>("DemoReply");
        if (fixture == null || fixture.channels != 1) throw new InvalidOperationException("Mono speech fixture missing.");
        var audio = new float[fixture.samples];
        if (!fixture.GetData(audio, 0)) throw new InvalidOperationException("Could not read speech fixture.");
        foreach (float background in new[] { .002f, .0065f, .012f })
            CheckSpeechFixture(audio, fixture.frequency, background, .2f, true);
        CheckSpeechFixture(audio, fixture.frequency, .002f, .1f, false);
        CheckSpeechFixture(audio, fixture.frequency, .0065f, .2f, false);
        var tenSeconds = new float[fixture.frequency * 10];
        for (int index = 0; index < tenSeconds.Length; index++) tenSeconds[index] = audio[index % audio.Length];
        foreach (float background in new[] { .0065f, .012f })
        {
            int delay = CheckSpeechFixture(tenSeconds, fixture.frequency, background, .2f, true);
            if (delay > VoiceEndpoint.SilenceMilliseconds + VoiceEndpoint.FrameMilliseconds)
                throw new InvalidOperationException($"Speech end was delayed: noise={background}, ms={delay}");
            Debug.Log($"Chipi 10-second speech fixture: noise={background}, endpoint delay={delay} ms");
        }
        Debug.Log("Chipi voice endpoint checks: PASS");
    }

    private static int CheckSpeechFixture(float[] audio, int rate, float background, float speechScale, bool warmup)
    {
        var endpoint = new VoiceEndpoint();
        int frameSize = rate * VoiceEndpoint.FrameMilliseconds / 1000;
        int warmupSamples = warmup ? rate : 0;
        int totalSamples = warmupSamples + audio.Length + rate * 2;
        int endedAt = -1;
        for (int start = 0; start + frameSize <= totalSamples; start += frameSize)
        {
            float sum = 0, energy = 0;
            for (int offset = 0; offset < frameSize; offset++)
            {
                int position = start + offset;
                int speechPosition = position - warmupSamples;
                float sample = Mathf.Sin(position * .43f) * background * 1.414214f;
                if (speechPosition >= 0 && speechPosition < audio.Length) sample += audio[speechPosition] * speechScale;
                sum += sample;
                energy += sample * sample;
            }
            float mean = sum / frameSize;
            endpoint.Observe(Mathf.Sqrt(Mathf.Max(0, energy / frameSize - mean * mean)));
            if (start + frameSize < warmupSamples + audio.Length && endpoint.Ended)
                throw new InvalidOperationException($"Speech fixture ended early: noise={background}, scale={speechScale}");
            if (endpoint.Ended) { endedAt = start + frameSize; break; }
        }
        if (!endpoint.HasSpeech || !endpoint.Ended)
            throw new InvalidOperationException($"Speech fixture failed: noise={background}, scale={speechScale}, warmup={warmup}, speech={endpoint.HasSpeech}, ended={endpoint.Ended}, silenceMs={endpoint.ObservedSilenceMilliseconds}");
        if (endpoint.FirstSpeechFrame(frameSize) > warmupSamples)
            throw new InvalidOperationException($"Speech fixture lost its beginning: noise={background}, scale={speechScale}, warmup={warmup}, first={endpoint.FirstSpeechFrame(frameSize)}, origin={warmupSamples}");
        return (endedAt - warmupSamples - audio.Length) * 1000 / rate;
    }

    public static void Prepare()
    {
        const string materialPath = "Assets/ChipiProjection.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Chipi/Concept Projection"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.mainTexture = Resources.Load<Texture2D>("ChipiConcept");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        var path = "Assets/Resources/Chipi.fbx";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!model.GetComponentsInChildren<Transform>().Any(joint => joint.name == "SpeechMouth"))
            throw new InvalidOperationException("SpeechMouth missing");
        if (!model.GetComponentsInChildren<Renderer>().Any(renderer => renderer.sharedMaterials.Contains(material)))
            throw new InvalidOperationException("Approved concept material missing from model");
        var animation = model.GetComponent<Animation>();
        if (animation == null) throw new InvalidOperationException("Imported animation component missing");
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
            Debug.Log($"Chipi clip: {clip.name}, {clip.length:F2}s");
        foreach (var name in new[] { "Idle", "Greeting", "Listening", "Talking", "Happy" })
            if (animation[name] == null) throw new InvalidOperationException("Missing animation " + name);
        if (Resources.Load<AudioClip>("DemoReply") == null)
            throw new InvalidOperationException("Demo reply audio missing");
        var scenePath = "Assets/Chipi.unity";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        new GameObject("Chipi App").AddComponent<ChipiApp>();
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        PlayerSettings.productName = "Chipi";
        PlayerSettings.companyName = "Chipi";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.chipi.prototype");
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        AssetDatabase.SaveAssets();
    }

    public static void IOS()
    {
        Prepare();
        CheckVoiceEndpoint();
        IncrementVersion();
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.chipi.prototype");
        PlayerSettings.iOS.targetOSVersionString = "16.0";
        bool simulator = Environment.GetEnvironmentVariable("CHIPI_IOS_SIMULATOR") == "1";
        PlayerSettings.iOS.sdkVersion = simulator ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
        if (simulator) PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
        PlayerSettings.iOS.microphoneUsageDescription = "Chipi uses the microphone to hear your English practice.";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Chipi.unity" },
            locationPathName = Path.GetFullPath(simulator ? "../build/Chipi-iOS-Simulator" : "../build/Chipi-iOS"),
            target = BuildTarget.iOS,
            extraScriptingDefines = simulator ? new[] { "CHIPI_SIMULATOR_BUILD" } : Array.Empty<string>(),
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("iOS export failed: " + report.summary.result);
    }

    public static void Android()
    {
        Prepare();
        CheckVoiceEndpoint();
        IncrementVersion();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Chipi.unity" },
            locationPathName = Path.GetFullPath("../build/Chipi-dev.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Android build failed: " + report.summary.result);
        Debug.Log($"Chipi APK built: {report.summary.totalSize} bytes");
    }

    private static void IncrementVersion()
    {
        if (!Version.TryParse(PlayerSettings.bundleVersion, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new InvalidOperationException("Chipi version must have three numeric components.");
        PlayerSettings.bundleVersion = $"{version.Major}.{version.Minor}.{checked(version.Build + 1)}";
        AssetDatabase.SaveAssets();
        Debug.Log($"Chipi build version: {PlayerSettings.bundleVersion}");
    }
}
