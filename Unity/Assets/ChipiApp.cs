using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class ChipiApp : MonoBehaviour
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int ChipiAudioPlayback();
    [DllImport("__Internal")] private static extern void ChipiLocalStart(byte[] wav, int size, int useMemory);
    [DllImport("__Internal")] private static extern int ChipiLocalReadLatest([Out] byte[] result, int size);
    [DllImport("__Internal")] private static extern int ChipiLocalRemember([MarshalAs(UnmanagedType.LPUTF8Str)] string transcript,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string reply);
    [DllImport("__Internal")] private static extern int ChipiLocalRead([Out] byte[] result, int size);
    [DllImport("__Internal")] private static extern void ChipiLocalSpeak([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport("__Internal")] private static extern int ChipiLocalPlayback();
    [DllImport("__Internal")] private static extern void ChipiLocalCancel();
    [DllImport("__Internal")] private static extern int ChipiLocalModelAvailability();
    [DllImport("__Internal")] private static extern float ChipiLocalMouthOpening();
    [DllImport("__Internal")] private static extern void ChipiMicWatchBackground();
    [DllImport("__Internal")] private static extern int ChipiMicWasBackgrounded();
    [DllImport("__Internal")] private static extern void ChipiMicRequestPermission();
    [DllImport("__Internal")] private static extern int ChipiMicPermission();
    [DllImport("__Internal")] private static extern int ChipiMicStart(int seconds);
    [DllImport("__Internal")] private static extern void ChipiMicStop();
    [DllImport("__Internal")] private static extern int ChipiMicPosition();
    [DllImport("__Internal")] private static extern int ChipiMicRead(int offset, [Out] float[] samples, int count);
#endif

    private const int Rate = 16000;
    private const int CaptureSeconds = 15;
    private const int SpeechStartSeconds = 8;
    private const int MaxHistoryTurns = 3;
    private const int MaxHistoryMessageLength = 1000;
    private readonly List<HistoryTurn> conversationHistory = new();
    private string Endpoint => PlayerPrefs.GetString("Endpoint", "http://127.0.0.1:8080");
    private static readonly Color Ink = new(0.06f, 0.18f, 0.29f);
    private static readonly Color Blue = new(0.14f, 0.37f, 0.74f);
    private const float CharacterCardGap = 10f;
    private const float FooterHeight = 16f;
    private const float ControlAreaHeight = 168f;
    private Animation motion;
    private Camera characterView;
    private Transform characterTurn;
    private Light characterFill;
    private Material shadowMaterial;
    private Mesh shadowMesh;
    private Renderer mouth;
    private Renderer chest;
    private MaterialPropertyBlock chestExpression;
    private static readonly int ChestGlow = Shader.PropertyToID("_ChestGlow");
    private MaterialPropertyBlock expression;
    private float jaw, mouthRound, focus, chestPulse, motionEnergy, motionLean;
    private float nextBlink = 2.8f;
    private float micLevel;
    private float micPeak;
    private VoiceEndpoint voiceEndpoint;
    private int processedMicFrames;
    private int micWindowFrames;
    private float[] micSamples;
    private static readonly int Blink = Shader.PropertyToID("_Blink");
    private static readonly int Speaking = Shader.PropertyToID("_Speaking");
    private static readonly int MouthRound = Shader.PropertyToID("_MouthRound");
    private static readonly int JawOpen = Shader.PropertyToID("_JawOpen");
    private AudioSource speaker;
    private AudioClip recording, generated;
    private AudioClip streamingClip;
    private string device;
    private float deadline;
    private bool busy;
    private bool conversationActive;
    private bool microphoneMuted;
    private string diagnosticSessionId;
    private int diagnosticRequestNumber;
    private string captureId;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private bool mockMicBackgrounded;
#endif
    private byte[] retryAudio;
    private UnityWebRequest activeTurnRequest;
    private enum VoicePhase { Ready, RequestingMicrophone, Listening, Thinking, Speaking, Paused }
    private VoicePhase phase;
    private bool settingsOpen;
    private bool hasSavedConversation;
    private const int PlaybackBufferMs = 1000;
    private bool server;
#if UNITY_IOS && !UNITY_EDITOR
    private bool localSpeaking;

    [Serializable]
    private sealed class LocalReply
    {
        public string transcript;
        public string reply;
        public string error;
    }
#endif
    private string status = "";
    private string transcript = "";
    private string reply = "Hello, how can I help you?";
    private bool stylesReady;
    private GUIStyle body, small, dialogueText, dialogueSpeaker, primary, secondary, phaseStyle;
    private string displayedReply, displayedTranscript;
    private float dialogueOffset, touchStartY, touchStartOffset;
    private readonly float[] output = new float[256];
    private float peakSpeech;

    [Serializable]
    private sealed class Turn
    {
        public string transcript;
        public string reply;
        public string audioBase64;
        public string mode;
    }

    [Serializable]
    private sealed class HistoryTurn
    {
        public string transcript;
        public string reply;
    }

    [Serializable]
    private sealed class TurnHistory
    {
        public HistoryTurn[] turns;
    }

    [Serializable]
    private sealed class StreamEvent
    {
        public string type, transcript, reply, data, mode, stage;
        public int status, rate, textMs, ttsFirstMs;
        public bool timing;
    }

    private sealed class StreamDownload : DownloadHandlerScript
    {
        private readonly MemoryStream pending = new();
        private readonly object sync = new();
        private int total;

        protected override bool ReceiveData(byte[] data, int length)
        {
            if (data == null || length <= 0) return true;
            lock (sync)
            {
                total += length;
                if (total > 12_000_000) return false;
                pending.Write(data, 0, length);
                return true;
            }
        }

        public byte[] Take()
        {
            lock (sync)
            {
                byte[] bytes = pending.ToArray();
                pending.SetLength(0);
                return bytes;
            }
        }
    }

    private sealed class PcmBuffer
    {
        private readonly Queue<float[]> chunks = new();
        private readonly object sync = new();
        private int offset, available;
        private long renderedFrames, lastAudioFrame, emptyFrames;

        public int Available { get { lock (sync) return available; } }
        public long LastAudioFrame { get { lock (sync) return lastAudioFrame; } }
        public long EmptyFrames { get { lock (sync) return emptyFrames; } }

        public void Add(byte[] bytes)
        {
            var samples = new float[bytes.Length / 2];
            for (int index = 0; index < samples.Length; index++)
                samples[index] = (short)(bytes[index * 2] | bytes[index * 2 + 1] << 8) / 32768f;
            lock (sync)
            {
                chunks.Enqueue(samples);
                available += samples.Length;
            }
        }

        public void Read(float[] output)
        {
            Array.Clear(output, 0, output.Length);
            lock (sync)
            {
                int written = 0;
                while (written < output.Length && chunks.Count > 0)
                {
                    float[] chunk = chunks.Peek();
                    int count = Math.Min(output.Length - written, chunk.Length - offset);
                    Array.Copy(chunk, offset, output, written, count);
                    written += count;
                    offset += count;
                    available -= count;
                    if (offset == chunk.Length) { chunks.Dequeue(); offset = 0; }
                }
                if (written > 0) lastAudioFrame = renderedFrames + written;
                emptyFrames += output.Length - written;
                renderedFrames += output.Length;
            }
        }
    }

    private sealed class StreamTurn
    {
        private readonly MemoryStream line = new();
        public readonly PcmBuffer audio = new();
        public string transcript, reply, mode;
        public bool ready, complete, timing;
        public int textMs, ttsFirstMs;
        public int error, rate;
        public int samples;
        public string errorStage;

        public void Feed(byte[] bytes)
        {
            foreach (byte value in bytes)
            {
                if (value != '\n')
                {
                    if (line.Length >= 1_000_000) throw new FormatException();
                    line.WriteByte(value);
                    continue;
                }
                string json = Encoding.UTF8.GetString(line.ToArray());
                line.SetLength(0);
                StreamEvent item = JsonUtility.FromJson<StreamEvent>(json);
                if (item == null || complete || error != 0) throw new FormatException();
                switch (item.type)
                {
                    case "ready" when !ready:
                        ready = true;
                        timing = item.timing;
                        break;
                    case "metadata" when ready && transcript == null && item.rate >= 8000 && item.rate <= 48000 && !string.IsNullOrWhiteSpace(item.transcript) && !string.IsNullOrWhiteSpace(item.reply):
                        transcript = item.transcript;
                        reply = item.reply;
                        rate = item.rate;
                        mode = item.mode;
                        textMs = item.textMs;
                        break;
                    case "audio" when transcript != null:
                        byte[] pcm = Convert.FromBase64String(item.data);
                        if (pcm.Length == 0 || pcm.Length % 2 != 0 || samples + pcm.Length / 2 > 4_000_000)
                            throw new FormatException();
                        if (samples == 0) ttsFirstMs = item.ttsFirstMs;
                        samples += pcm.Length / 2;
                        audio.Add(pcm);
                        break;
                    case "complete" when transcript != null && samples > 0:
                        complete = true;
                        break;
                    case "error" when item.status >= 400 && item.status <= 599:
                        error = item.status;
                        errorStage = item.stage;
                        break;
                    default:
                        throw new FormatException();
                }
            }
        }

        public bool Finished => complete && line.Length == 0;
    }

    private void Awake()
    {
        Application.targetFrameRate = 60;
#if UNITY_IOS && !UNITY_EDITOR
        Diagnostic($"apple.model_availability code={ChipiLocalModelAvailability()}");
#else
        server = PlayerPrefs.GetInt("VoiceServerEnabled", 0) == 1;
#endif
        Screen.orientation = ScreenOrientation.Portrait;
        var view = new GameObject("Chipi camera").AddComponent<Camera>();
        view.gameObject.AddComponent<AudioListener>();
        view.backgroundColor = new Color(0.88f, 0.93f, 0.99f);
        view.clearFlags = CameraClearFlags.SolidColor;
        view.orthographic = true;
        view.orthographicSize = 2.03f;
        view.transform.position = new Vector3(0, 2.72f, -7);
        view.transform.LookAt(new Vector3(0, 1.72f, 0));
        characterView = view;
        UpdateCharacterViewport();
        RenderSettings.ambientLight = new Color(.67f, .71f, .8f);
        AddLight("Key", new Vector3(34, -32, 0), 1.25f);
        characterFill = AddLight("Fill", new Vector3(20, 131, 0), .65f);
        characterTurn = new GameObject("Character turn").transform;
        characterTurn.localRotation = Quaternion.Euler(0, -13, 0);
        AddContactShadow();
        var stage = new GameObject("Character orientation");
        stage.transform.SetParent(characterTurn, false);
        var model = Instantiate(Resources.Load<GameObject>("Chipi"), stage.transform);
        motion = model.GetComponent<Animation>();
        Transform faceDirection = null;
        foreach (var joint in model.GetComponentsInChildren<Transform>())
        {
            if (joint.name == "SpeechMouth") mouth = joint.GetComponent<Renderer>();
            if (joint.name == "FaceDirection") faceDirection = joint;
        }
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            if (renderer.name.StartsWith("Body |", StringComparison.Ordinal))
            {
                chest = renderer;
                break;
            }
        }
        expression = new MaterialPropertyBlock();
        chestExpression = new MaterialPropertyBlock();
        if (faceDirection != null && faceDirection.position.z > model.transform.position.z)
            stage.transform.Rotate(0, 180, 0);
        speaker = gameObject.AddComponent<AudioSource>();
        Debug.Log($"Chipi ready: {motion.GetClipCount()} clips, mouth present: {mouth != null}");
        if (Environment.GetEnvironmentVariable("CHIPI_RUN_DEMO") == "1" ||
            Array.IndexOf(Environment.GetCommandLineArgs(), "--chipi-demo") >= 0)
        {
            RunDemo();
            return;
        }
#if DEVELOPMENT_BUILD && UNITY_IOS && !UNITY_EDITOR
        if (Environment.GetEnvironmentVariable("CHIPI_CHECK_VOICE_COMPARISON") == "1")
        {
            StartCoroutine(CheckVoiceComparison());
            return;
        }
        if (Environment.GetEnvironmentVariable("CHIPI_CHECK_APPLE_TURN") == "1")
        {
            StartCoroutine(CheckAppleTurn());
            return;
        }
#endif
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    if (Environment.GetEnvironmentVariable("CHIPI_CHECK_MULTITURN") == "1")
    {
        StartCoroutine(CheckMultipleTurns());
        return;
    }
    string controlsCheck = Environment.GetEnvironmentVariable("CHIPI_CHECK_CONTROLS");
    if (!string.IsNullOrEmpty(controlsCheck))
    {
        StartCoroutine(CheckControls(controlsCheck));
        return;
    }
        if (Environment.GetEnvironmentVariable("CHIPI_CHECK_RECOVERY") == "1")
        {
            StartCoroutine(CheckRecovery());
            return;
        }
    string cancelCheck = Environment.GetEnvironmentVariable("CHIPI_CHECK_STREAM_CANCEL");
    if (!string.IsNullOrEmpty(cancelCheck))
    {
        StartCoroutine(CheckStreamCancellation(cancelCheck));
        return;
    }
        bool checkHistory = Environment.GetEnvironmentVariable("CHIPI_CHECK_HISTORY") == "1";
        if (checkHistory || Environment.GetEnvironmentVariable("CHIPI_CHECK_ROUND_TRIP") == "1")
        {
            StartCoroutine(CheckRoundTrip(checkHistory));
            return;
        }
#endif
#if UNITY_IOS && !UNITY_EDITOR
        var latest = new byte[8192];
        int size = ChipiLocalReadLatest(latest, latest.Length);
        if (size > 0)
        {
            var saved = JsonUtility.FromJson<LocalReply>(Encoding.UTF8.GetString(latest, 0, size));
            transcript = saved.transcript;
            reply = saved.reply;
            hasSavedConversation = true;
        }
        StartCoroutine(PlayAppleSample(hasSavedConversation ? "Welcome back. Ready to pick up where we left off?" : reply, ""));
#else
        StartCoroutine(Speak(Resources.Load<AudioClip>("Greeting"), ""));
#endif
    }

    private void AddContactShadow()
    {
        var shader = Resources.Load<Shader>("Shaders/ChipiContactShadow");
        if (shader == null) return;
        shadowMaterial = new Material(shader);
        var shadow = new GameObject("Chipi contact shadow");
        shadow.transform.SetParent(transform, false);
        shadow.transform.localPosition = new Vector3(0, .335f, 0);
        shadowMesh = new Mesh { name = "Contact shadow quad" };
        shadowMesh.vertices = new Vector3[] {
            new(-.825f, 0, -.875f), new(.825f, 0, -.875f),
            new(.825f, 0, .875f), new(-.825f, 0, .875f)
        };
        shadowMesh.uv = new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        shadowMesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
        shadow.AddComponent<MeshFilter>().sharedMesh = shadowMesh;
        shadow.AddComponent<MeshRenderer>().sharedMaterial = shadowMaterial;
    }

    private void OnDestroy()
    {
#if UNITY_IOS && !UNITY_EDITOR
        CancelLocalVoice();
#endif
        if (shadowMaterial != null) Destroy(shadowMaterial);
        if (shadowMesh != null) Destroy(shadowMesh);
    }

    private static Light AddLight(string label, Vector3 rotation, float intensity)
    {
        var light = new GameObject(label).AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.transform.rotation = Quaternion.Euler(rotation);
        return light;
    }

    private void Play(string clip)
    {
        if (motion != null && motion[clip] != null) motion.CrossFade(clip, .12f);
    }

    private static void Diagnostic(string message) => Debug.Log($"[CHIPI-DIAG] {message}");

    private string EnsureDiagnosticSession()
    {
        if (!conversationActive) return "standalone";
        if (string.IsNullOrEmpty(diagnosticSessionId))
        {
            diagnosticSessionId = Guid.NewGuid().ToString("D");
            diagnosticRequestNumber = 0;
            Diagnostic($"session.started id={diagnosticSessionId}");
        }
        return diagnosticSessionId;
    }

    private void Update()
    {
        if (recording == null) return;
        int frames = CapturePosition();
        UpdateMicLevel(frames);
        if (voiceEndpoint.Ended)
            StopCapture(voiceEndpoint.EndFrame(processedMicFrames, micWindowFrames), stopReason: "silence_endpoint");
        else if (Time.unscaledTime >= deadline)
            StopCapture(frames, voiceEndpoint.HasSpeech ? "" : "I didn’t hear speech. Tap to try again.",
                stopReason: voiceEndpoint.HasSpeech ? "capture_limit" : "speech_start_timeout");
    }

    private void UpdateMicLevel(int frames)
    {
        while (processedMicFrames + micWindowFrames <= frames)
        {
            if (!ReadCaptureWindow(processedMicFrames)) return;
            if (processedMicFrames == 0)
            {
                Debug.Log("Chipi microphone frames are arriving");
                status = "Start speaking when you’re ready.";
            }
            processedMicFrames += micWindowFrames;
            float total = 0;
            float energy = 0;
            foreach (float sample in micSamples)
            {
                total += sample;
                energy += sample * sample;
            }
            float mean = total / micSamples.Length;
            float volume = Mathf.Sqrt(Mathf.Max(0, energy / micSamples.Length - mean * mean));
            micPeak = Mathf.Max(micPeak, volume);
            micLevel = Mathf.Lerp(micLevel, Mathf.Clamp01(volume * 22), .28f);
            bool hadSpeech = voiceEndpoint.HasSpeech;
            voiceEndpoint.Observe(volume);
            if (!hadSpeech && voiceEndpoint.HasSpeech)
            {
                Debug.Log("Chipi microphone detected speech");
                Diagnostic($"vad.speech_detected capture={captureId} noise={voiceEndpoint.NoiseLevel:F4} startThreshold={voiceEndpoint.StartThreshold:F4} onsetFrames={voiceEndpoint.LongestOnset}");
                status = "Pause to send, or tap Send now.";
                deadline = Time.unscaledTime + CaptureSeconds;
            }
            if (voiceEndpoint.Ended) return;
        }
    }

    private void LateUpdate()
    {
        UpdateCharacterViewport();
        bool attentive = phase is VoicePhase.Listening or VoicePhase.Thinking or VoicePhase.Speaking;
        focus = Mathf.MoveTowards(focus, attentive ? 1 : 0, 3 * Time.deltaTime);
        float targetEnergy = phase switch
        {
            VoicePhase.Listening => .55f,
            VoicePhase.Thinking => .7f,
            VoicePhase.Speaking => 1f,
            _ => .3f
        };
        float targetLean = phase == VoicePhase.Listening ? 3f : phase == VoicePhase.Thinking ? -3f : 0f;
        motionEnergy = Mathf.MoveTowards(motionEnergy, targetEnergy, 2 * Time.deltaTime);
        motionLean = Mathf.MoveTowards(motionLean, targetLean, 8 * Time.deltaTime);
        float turn = Mathf.Lerp(-13, -5, focus);
        float sway = Mathf.Lerp(2.6f, 4.5f, motionEnergy);
        characterTurn.localPosition = new Vector3(0, .025f * motionEnergy * Mathf.Sin(Time.time * 2.2f), 0);
        characterTurn.localRotation = Quaternion.Euler(2f * motionEnergy * Mathf.Sin(Time.time * 1.8f),
            turn + sway * Mathf.Sin(Time.time * 1.05f),
            motionLean + 2.5f * motionEnergy * Mathf.Sin(Time.time * 1.7f));
        if (characterFill != null)
            characterFill.intensity = .65f + .12f * motionEnergy * (.5f + .5f * Mathf.Sin(Time.time * 2.2f));
        if (mouth == null) return;
        float open = 0;
        float round = 0;
        if (speaker.isPlaying)
        {
            speaker.GetOutputData(output, 0);
            float squares = 0;
            float edges = 0;
            float previous = output[0];
            foreach (float sample in output)
            {
                squares += sample * sample;
                float difference = sample - previous;
                edges += difference * difference;
                previous = sample;
            }
            float rms = Mathf.Sqrt(squares / output.Length);
            peakSpeech = Mathf.Max(peakSpeech, rms);
            open = Mathf.InverseLerp(.012f, .16f, rms);
            if (squares > .0001f)
                round = (1 - Mathf.InverseLerp(.11f, .4f, Mathf.Sqrt(edges / squares))) * Mathf.Sqrt(open);
        }
#if UNITY_IOS && !UNITY_EDITOR
        if (localSpeaking) open = ChipiLocalMouthOpening();
#endif
        float response = open > jaw ? 26f : 15f;
        jaw = Mathf.Lerp(jaw, open, 1 - Mathf.Exp(-Time.deltaTime * response));
        // The closed pose is used only within speech; the resting smile remains intact.
        expression.SetFloat(Speaking, phase == VoicePhase.Speaking ? 1 : 0);
        if (chest != null)
        {
            float pulse = phase switch
            {
                VoicePhase.Listening => .24f + micLevel * .60f,
                VoicePhase.Thinking => .32f + .60f * (.5f + .5f * Mathf.Sin(Time.time * 4f)),
                VoicePhase.Speaking => .12f + jaw * .28f,
                _ => 0
            };
            chestPulse = Mathf.MoveTowards(chestPulse, pulse, 3 * Time.deltaTime);
            chestExpression.SetFloat(ChestGlow, chestPulse);
            chest.SetPropertyBlock(chestExpression);
        }
        mouthRound = Mathf.Lerp(mouthRound, round, 1 - Mathf.Exp(-Time.deltaTime * 18f));
        expression.SetFloat(MouthRound, mouthRound);
        expression.SetFloat(JawOpen, jaw);
        float blinkTime = Time.time - nextBlink;
        if (blinkTime > .18f)
        {
            nextBlink = Time.time + UnityEngine.Random.Range(3.1f, 5.6f);
            blinkTime = -1;
        }
        float blink = blinkTime >= 0
            ? Mathf.Min(Mathf.Clamp01(blinkTime / .065f), Mathf.Clamp01((.18f - blinkTime) / .095f)) : 0;
        expression.SetFloat(Blink, blink);
        mouth.SetPropertyBlock(expression);
    }

    private IEnumerator BeginCapture()
    {
        busy = true;
        phase = VoicePhase.RequestingMicrophone;
        string session = EnsureDiagnosticSession();
        captureId = Guid.NewGuid().ToString("D");
        bool mockMicrophone = Environment.GetEnvironmentVariable("CHIPI_CHECK_MOCK_MIC") == "1";
        Diagnostic($"capture.requested session={session} capture={captureId} mock={mockMicrophone}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        if (Environment.GetEnvironmentVariable("CHIPI_CHECK_MOCK_MIC") == "1")
        {
            if (Environment.GetEnvironmentVariable("CHIPI_CHECK_MOCK_MIC_DELAY") == "1")
                yield return new WaitForSecondsRealtime(2);
            if (mockMicBackgrounded)
            {
                CloseConversation("Conversation paused.");
                yield break;
            }
            phase = VoicePhase.Listening;
            busy = false;
            Debug.Log("Chipi fixture microphone rearmed");
            yield break;
        }
#endif
#if UNITY_IOS && !UNITY_EDITOR
    ChipiMicWatchBackground();
        Diagnostic($"microphone.permission_requested capture={captureId}");
        ChipiMicRequestPermission();
        float permissionDeadline = Time.realtimeSinceStartup + 45;
        while (ChipiMicPermission() < 0 && Time.realtimeSinceStartup < permissionDeadline)
            yield return null;
        Diagnostic($"microphone.permission_result capture={captureId} state={ChipiMicPermission()} timedOut={ChipiMicPermission() < 0}");
        if (ChipiMicPermission() != 1)
        {
            Debug.Log("Chipi native microphone permission unavailable");
            CloseConversation("Allow microphone access in Settings to speak.");
            yield break;
        }
        if (ChipiMicWasBackgrounded() != 0)
        {
            CloseConversation("Conversation paused.");
            yield break;
        }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            bool waiting = true;
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => waiting = false;
            callbacks.PermissionDenied += _ => waiting = false;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            float expires = Time.realtimeSinceStartup + 30;
            while (waiting && Time.realtimeSinceStartup < expires) yield return null;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("Chipi microphone permission denied");
                status = "Allow microphone access in Settings to speak.";
                CloseConversation(status);
                yield break;
            }
        }
#endif
        yield return null;
#if UNITY_IOS && !UNITY_EDITOR
        if (ChipiMicWasBackgrounded() != 0)
        {
            CloseConversation("Conversation paused.");
            yield break;
        }
#endif
        int duration = SpeechStartSeconds + CaptureSeconds + 3;
#if UNITY_IOS && !UNITY_EDITOR
        int sampleRate = ChipiMicStart(duration);
        if (sampleRate == -1)
        {
            CloseConversation("The microphone is muted on this iPhone.");
            yield break;
        }
        recording = sampleRate > 0 ? AudioClip.Create("Chipi microphone", duration * sampleRate, 1, sampleRate, false) : null;
#else
        device = null;
        recording = Microphone.Start(device, false, duration, Rate);
#endif
        if (recording != null)
        {
            Debug.Log($"Chipi microphone opened: {recording.frequency} Hz, {recording.channels} channels");
            Diagnostic($"capture.opened capture={captureId} sampleRate={recording.frequency} channels={recording.channels} maxSeconds={CaptureSeconds}");
            status = "Waiting for microphone input…";
            phase = VoicePhase.Listening;
            micLevel = 0;
            micPeak = 0;
            voiceEndpoint = new VoiceEndpoint();
            processedMicFrames = 0;
            micWindowFrames = Mathf.Max(1, recording.frequency * VoiceEndpoint.FrameMilliseconds / 1000);
            micSamples = new float[micWindowFrames * recording.channels];
            deadline = Time.unscaledTime + SpeechStartSeconds;
            Play("Listening");
        }
        else
        {
            Debug.Log("Chipi microphone failed to open");
            Diagnostic($"capture.open_failed capture={captureId}");
            CloseConversation("Recording could not start. Check the microphone and try again.");
        }
        busy = false;
    }

    private int CapturePosition()
    {
#if UNITY_IOS && !UNITY_EDITOR
        return ChipiMicPosition();
#else
        return Microphone.GetPosition(device);
#endif
    }

    private bool ReadCaptureWindow(int offset)
    {
#if UNITY_IOS && !UNITY_EDITOR
        return ChipiMicRead(offset, micSamples, micSamples.Length) != 0;
#else
        return recording.GetData(micSamples, offset);
#endif
    }

    private void EndInput()
    {
#if UNITY_IOS && !UNITY_EDITOR
        ChipiMicStop();
#else
        Microphone.End(device);
#endif
    }

    private void StopCapture(int frames, string noSpeechMessage = "I didn’t hear speech. Tap to try again.", bool manual = false,
        string stopReason = "unspecified")
    {
        double captureStopped = Time.realtimeSinceStartupAsDouble;
        int pauseMs = voiceEndpoint.Ended && !manual ? voiceEndpoint.ObservedSilenceMilliseconds : -1;
        EndInput();
        var captured = recording;
        recording = null;
        phase = VoicePhase.Ready;
        int start = voiceEndpoint.FirstSpeechFrame(micWindowFrames);
        Diagnostic($"Chipi microphone stopped: capture={captureId}, reason={stopReason}, frames={frames}, read={processedMicFrames}, sampleRate={captured.frequency}, peak={micPeak:F4}, speech={voiceEndpoint.HasSpeech}, manual={manual}, endpoint={voiceEndpoint.Ended}, silenceMs={voiceEndpoint.ObservedSilenceMilliseconds}, warmupNoise={voiceEndpoint.WarmupNoise:F4}, noise={voiceEndpoint.NoiseLevel:F4}, startThreshold={voiceEndpoint.StartThreshold:F4}, silenceThreshold={voiceEndpoint.SilenceThreshold:F4}, longestOnset={voiceEndpoint.LongestOnset}");
        bool signal = voiceEndpoint.HasSpeech || (manual && micPeak >= .0015f);
        if (!signal || frames - start < captured.frequency / 5)
        {
            Diagnostic($"capture.rejected capture={captureId} signal={signal} durationMs={(int)((long)Math.Max(0, frames - start) * 1000 / captured.frequency)} reason={(signal ? "too_short" : "no_speech")}");
            Destroy(captured);
            string message = processedMicFrames == 0 ? "The microphone didn’t deliver audio. Check your input and retry."
                : string.IsNullOrEmpty(noSpeechMessage) ? "Recording was interrupted. Try again." : noSpeechMessage;
            if (conversationActive)
            {
                microphoneMuted = true;
                busy = false;
                phase = VoicePhase.Paused;
                status = message;
                Play("Idle");
            }
            else CloseConversation(message);
            return;
        }
        byte[] wav = Encode(captured, frames - start, start, nativeCapture: true);
        Diagnostic($"capture.encoded capture={captureId} wavBytes={wav.Length} audioMs={(int)((long)(frames - start) * 1000 / captured.frequency)}");
        Destroy(captured);
        StartCoroutine(Send(wav, captureStopped: captureStopped, pauseMs: pauseMs, diagnosticId: captureId));
    }

    private void OnApplicationPause(bool paused)
    {
        Diagnostic($"lifecycle.application_pause paused={paused} session={diagnosticSessionId ?? "none"} phase={phase} active={conversationActive}");
        if (paused && conversationActive && phase != VoicePhase.RequestingMicrophone)
            StopConversation("Conversation paused.");
    }

    private void CloseConversation(string message)
    {
        Diagnostic($"session.closed id={diagnosticSessionId ?? "none"} phase={phase} historyTurns={conversationHistory.Count}");
        conversationActive = false;
        conversationHistory.Clear();
        retryAudio = null;
        microphoneMuted = false;
        busy = false;
        status = message;
        phase = VoicePhase.Ready;
        diagnosticSessionId = null;
        diagnosticRequestNumber = 0;
        Play("Idle");
    }

    private void FailTurn(string message)
    {
        Diagnostic($"turn.failed phase={phase} retryAudioAvailable={retryAudio != null} active={conversationActive}");
        if (!conversationActive || retryAudio == null)
        {
            CloseConversation(message);
            return;
        }
        busy = false;
        status = message;
        phase = VoicePhase.Ready;
        Play("Idle");
    }

    private void StopConversation(string message)
    {
        Diagnostic($"session.stop_requested id={diagnosticSessionId ?? "none"} phase={phase}");
        activeTurnRequest?.Abort();
        activeTurnRequest = null;
        StopAllCoroutines();
#if UNITY_IOS && !UNITY_EDITOR
        CancelLocalVoice();
#endif
        speaker.Stop();
        if (streamingClip != null)
        {
            speaker.clip = null;
            Destroy(streamingClip);
            streamingClip = null;
        }
        if (recording != null)
        {
            EndInput();
            Destroy(recording);
            recording = null;
        }
        CloseConversation(message);
    }

    private void SetMicrophoneMuted(bool muted)
    {
        if (!conversationActive) return;
        Diagnostic($"microphone.mute_changed session={diagnosticSessionId ?? "none"} muted={muted} phase={phase}");
        microphoneMuted = muted;
        if (muted && phase == VoicePhase.RequestingMicrophone)
        {
            StopAllCoroutines();
            busy = false;
            phase = VoicePhase.Paused;
        }
        if (muted && phase == VoicePhase.Listening)
        {
            if (recording != null)
            {
                EndInput();
                Destroy(recording);
                recording = null;
            }
            busy = false;
            phase = VoicePhase.Paused;
        }
        if (muted && phase == VoicePhase.Paused)
        {
            status = "Microphone muted.";
            Play("Idle");
        }
        else if (!muted && phase == VoicePhase.Paused)
            StartCoroutine(BeginCapture());
    }

    private void InterruptReply()
    {
        if (!conversationActive || phase is not (VoicePhase.Speaking or VoicePhase.Thinking)) return;
        Diagnostic($"turn.interrupted session={diagnosticSessionId ?? "none"} phase={phase} muted={microphoneMuted}");
        activeTurnRequest?.Abort();
        activeTurnRequest = null;
        StopAllCoroutines();
#if UNITY_IOS && !UNITY_EDITOR
        CancelLocalVoice();
#endif
        speaker.Stop();
        if (streamingClip != null)
        {
            speaker.clip = null;
            Destroy(streamingClip);
            streamingClip = null;
        }
        retryAudio = null;
        busy = false;
        if (microphoneMuted)
        {
            phase = VoicePhase.Paused;
            status = "Microphone muted.";
            Play("Idle");
        }
        else StartCoroutine(BeginCapture());
    }

    private void SpeakAgain()
    {
        if (!conversationActive || retryAudio == null || phase != VoicePhase.Ready) return;
        retryAudio = null;
        microphoneMuted = false;
        StartCoroutine(BeginCapture());
    }

    private IEnumerator RearmConversation()
    {
        Diagnostic($"capture.rearm_requested session={diagnosticSessionId ?? "none"} active={conversationActive} muted={microphoneMuted}");
        if (conversationActive && !microphoneMuted)
        {
            yield return new WaitForSecondsRealtime(.25f);
            if (conversationActive && !microphoneMuted)
            {
                yield return BeginCapture();
                yield break;
            }
        }
        if (conversationActive && microphoneMuted)
        {
            phase = VoicePhase.Paused;
            status = "Microphone muted.";
        }
        else phase = VoicePhase.Ready;
        busy = false;
        Play("Idle");
        Diagnostic($"capture.rearm_skipped session={diagnosticSessionId ?? "none"} active={conversationActive} muted={microphoneMuted} phase={phase}");
    }

    private IEnumerator Send(byte[] wav, string endpointOverride = null, bool checkHistory = false,
        double captureStopped = -1, int pauseMs = -1, string diagnosticId = null)
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (endpointOverride == null)
        {
            yield return SendLocal(wav);
            yield break;
        }
#endif
        string session = EnsureDiagnosticSession();
        diagnosticId ??= Guid.NewGuid().ToString("D");
        int requestNumber = ++diagnosticRequestNumber;
        busy = true;
        if (conversationActive) retryAudio = wav;
        Play("Idle");
        phase = VoicePhase.Thinking;
        bool rememberTurn = conversationActive || checkHistory;
        status = "";
        var parts = new List<IMultipartFormSection> {
            new MultipartFormFileSection("audio", wav, "recording.wav", "audio/wav")
        };
        if (rememberTurn && conversationHistory.Count > 0)
            parts.Add(new MultipartFormDataSection("history", JsonUtility.ToJson(new TurnHistory { turns = conversationHistory.ToArray() })));
        string destination = endpointOverride ?? Endpoint;
        using var request = UnityWebRequest.Post(destination + "/api/turn?stream=1", parts);
        var download = new StreamDownload();
        request.downloadHandler = download;
        request.SetRequestHeader("X-Chipi-Turn-Id", diagnosticId);
#if UNITY_IOS && DEVELOPMENT_BUILD && !CHIPI_SIMULATOR_BUILD
        if (destination == PlayerPrefs.GetString("DeviceEndpoint", ""))
            request.SetRequestHeader("X-Chipi-Device-Token", PlayerPrefs.GetString("DeviceToken", ""));
#endif
        request.timeout = 60;
        activeTurnRequest = request;
        double requestStarted = Time.realtimeSinceStartupAsDouble;
        double readyReceived = -1;
        double firstAudioReceived = -1;
        double playbackStarted = -1;
        Diagnostic($"turn.request_started id={diagnosticId} session={session} number={requestNumber} protocol=stream wavBytes={wav.Length} historyTurns={conversationHistory.Count}");
        var operation = request.SendWebRequest();
        var streamed = new StreamTurn();
        using var legacy = new MemoryStream();
        bool playing = false, invalid = false, metadataLogged = false;
        while (true)
        {
            string contentType = request.GetResponseHeader("Content-Type") ?? "";
            if (contentType.Length > 0 || operation.isDone)
            {
                byte[] received = download.Take();
                if (contentType.StartsWith("application/x-ndjson", StringComparison.OrdinalIgnoreCase))
                {
                    try { streamed.Feed(received); }
                    catch (Exception exception) when (exception is FormatException or ArgumentException)
                    {
                        invalid = true;
                        Diagnostic($"turn.stream_parse_failed id={diagnosticId} exception={exception.GetType().Name}");
                        request.Abort();
                    }
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (streamed.ready && readyReceived < 0)
                    {
                        readyReceived = now;
                        Diagnostic($"turn.ready id={diagnosticId} timing={streamed.timing}");
                    }
                    if (streamed.transcript != null && !metadataLogged)
                    {
                        metadataLogged = true;
                        Diagnostic($"turn.metadata id={diagnosticId} mode={streamed.mode} rate={streamed.rate} transcriptChars={streamed.transcript.Length} replyChars={streamed.reply.Length}");
                    }
                    if (streamed.samples > 0 && firstAudioReceived < 0)
                    {
                        firstAudioReceived = now;
                        Diagnostic($"turn.first_audio id={diagnosticId} pcmSamples={streamed.samples} rate={streamed.rate}");
                    }
                    if (!invalid && streamed.transcript != null)
                    {
                        transcript = streamed.transcript;
                        reply = streamed.reply;
                    }
                    if (streamed.error != 0) request.Abort();
                    if (!playing && streamed.error == 0 && !invalid &&
                        (streamed.rate > 0 && streamed.audio.Available >= streamed.rate * PlaybackBufferMs / 1000 || streamed.complete && streamed.audio.Available > 0))
                    {
#if UNITY_IOS && !UNITY_EDITOR
                        if (ChipiAudioPlayback() == 0) Debug.LogWarning("Chipi could not configure the playback audio session");
#endif
                        streamingClip = AudioClip.Create("Chipi streaming reply", 4_000_000 + streamed.rate * 60, 1, streamed.rate, true, streamed.audio.Read);
                        speaker.clip = streamingClip;
                        peakSpeech = 0;
                        speaker.Play();
                        playbackStarted = Time.realtimeSinceStartupAsDouble;
                        LogReplyTiming(diagnosticId, streamed, requestStarted, readyReceived, firstAudioReceived, captureStopped, pauseMs);
                        phase = VoicePhase.Speaking;
                        status = streamed.mode == "demo" ? "Server demo · sample transcript" : "AI-generated voice";
                        transcript = streamed.transcript;
                        reply = streamed.reply;
                        Play("Talking");
                        playing = true;
                        Debug.Log($"Chipi streaming voice started after {Time.realtimeSinceStartup - requestStarted:F2}s (turn={diagnosticId})");
                        Diagnostic($"playback.stream_started id={diagnosticId} afterMs={(int)((Time.realtimeSinceStartupAsDouble - requestStarted) * 1000)}");
                    }
                }
                else legacy.Write(received, 0, received.Length);
            }
            if (operation.isDone || invalid || streamed.error != 0) break;
            yield return null;
        }
        activeTurnRequest = null;
        string responseType = request.GetResponseHeader("Content-Type") ?? "none";
        Debug.Log($"Chipi streaming request finished: HTTP {request.responseCode}, {request.result}, streamError={streamed.error}, invalid={invalid}, ms={(int)((Time.realtimeSinceStartupAsDouble - requestStarted) * 1000)} (turn={diagnosticId}, errorStage={streamed.errorStage ?? "none"})");
        Diagnostic($"turn.request_finished id={diagnosticId} http={request.responseCode} result={request.result} streamError={streamed.error} errorStage={streamed.errorStage ?? "none"} invalid={invalid} complete={streamed.complete} contentType={responseType} pcmSamples={streamed.samples} elapsedMs={(int)((Time.realtimeSinceStartupAsDouble - requestStarted) * 1000)}");
        if (request.responseCode == 501)
        {
            yield return SendLegacy(wav, endpointOverride, checkHistory, diagnosticId);
            yield break;
        }
        if (request.result == UnityWebRequest.Result.Success && responseType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            Turn turn;
            try
            {
                turn = JsonUtility.FromJson<Turn>(Encoding.UTF8.GetString(legacy.ToArray()));
                if (generated != null) Destroy(generated);
                generated = Decode(Convert.FromBase64String(turn.audioBase64));
            }
            catch (Exception)
            {
                FailTurn("Chipi received unreadable audio.");
                yield break;
            }
            transcript = turn.transcript;
            reply = turn.reply;
            Diagnostic($"playback.legacy_response id={diagnosticId} mode={turn.mode} transcriptChars={turn.transcript?.Length ?? 0} replyChars={turn.reply?.Length ?? 0} audioSamples={generated.samples}");
            yield return Speak(generated, turn.mode == "demo" ? "Server demo · sample transcript" : "AI-generated voice");
        }
        else
        {
            if (invalid || streamed.error != 0 || request.result != UnityWebRequest.Result.Success || !streamed.Finished)
            {
                speaker.Stop();
                if (streamingClip != null) { speaker.clip = null; Destroy(streamingClip); streamingClip = null; }
                int responseCode = streamed.error != 0 ? streamed.error : (int)request.responseCode;
                Diagnostic($"turn.failed id={diagnosticId} stage={streamed.errorStage ?? "client_or_transport"} status={responseCode} result={request.result} invalid={invalid} finished={streamed.Finished}");
#if UNITY_IOS && !UNITY_EDITOR
                if (!invalid && !playing && streamed.error == 429 && streamed.errorStage == "speech_synthesis" &&
                    !string.IsNullOrWhiteSpace(streamed.reply))
                {
                    transcript = streamed.transcript;
                    reply = streamed.reply;
                    bool spoken = false;
                    yield return SpeakWithLocalVoice(reply, requestStarted, success => spoken = success);
                    Diagnostic($"playback.system_fallback id={diagnosticId} completed={spoken}");
                    if (spoken)
                    {
                        retryAudio = null;
                        RememberTurn(rememberTurn);
                        yield return RearmConversation();
                        yield break;
                    }
                    FailTurn("Chipi has a reply, but couldn’t read it aloud.");
                    yield break;
                }
#endif
                FailTurn(invalid || responseCode == 200 ? "Chipi received unreadable audio."
                    : VoiceError(responseCode, streamed.errorStage));
                yield break;
            }
            int previousAvailable = streamed.audio.Available;
            double progressDeadline = Time.realtimeSinceStartupAsDouble + 5;
            while (streamed.audio.Available > 0 && speaker.isPlaying)
            {
                int remaining = streamed.audio.Available;
                if (remaining < previousAvailable)
                {
                    previousAvailable = remaining;
                    progressDeadline = Time.realtimeSinceStartupAsDouble + 5;
                }
                if (Time.realtimeSinceStartupAsDouble >= progressDeadline) break;
                yield return null;
            }
            long lastAudioFrame = streamed.audio.LastAudioFrame;
            double tailDeadline = Time.realtimeSinceStartupAsDouble + 5 +
                Math.Max(0, lastAudioFrame - speaker.timeSamples) / (double)streamed.rate;
            while (streamed.audio.Available == 0 && speaker.isPlaying && speaker.timeSamples < lastAudioFrame &&
                   Time.realtimeSinceStartupAsDouble < tailDeadline)
                yield return null;
            if (streamed.audio.Available > 0 || !speaker.isPlaying || speaker.timeSamples < lastAudioFrame)
            {
                Debug.LogWarning($"[CHIPI-DIAG] playback.tail_incomplete id={diagnosticId} available={streamed.audio.Available} played={speaker.timeSamples} lastFrame={lastAudioFrame} isPlaying={speaker.isPlaying}");
                speaker.Stop();
                if (streamingClip != null) { speaker.clip = null; Destroy(streamingClip); streamingClip = null; }
                FailTurn("Audio playback was interrupted. Try again.");
                yield break;
            }
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            yield return new WaitForSecondsRealtime((float)(bufferLength * bufferCount) / AudioSettings.outputSampleRate + .1f);
            Debug.Log($"Chipi playback frames: received={streamed.samples}, lastAudio={streamed.audio.LastAudioFrame}, " +
                $"position={speaker.timeSamples}, empty={streamed.audio.EmptyFrames}, rate={streamed.rate} (turn={diagnosticId})");
            Diagnostic($"playback.stream_drained id={diagnosticId} received={streamed.samples} lastAudio={streamed.audio.LastAudioFrame}, " +
                $"position={speaker.timeSamples}, empty={streamed.audio.EmptyFrames}, rate={streamed.rate}, " +
                $"ms={(int)((Time.realtimeSinceStartupAsDouble - playbackStarted) * 1000)}");
            speaker.Stop();
            if (streamingClip != null) { speaker.clip = null; Destroy(streamingClip); streamingClip = null; }
            transcript = streamed.transcript;
            reply = streamed.reply;
            Debug.Log($"Chipi streaming voice finished: peak RMS {peakSpeech:F4} (turn={diagnosticId})");
            Diagnostic($"playback.stream_finished id={diagnosticId} peakRms={peakSpeech:F4}");
        }
        retryAudio = null;
        RememberTurn(rememberTurn);
        yield return RearmConversation();
    }

#if UNITY_IOS && !UNITY_EDITOR
    private void CancelLocalVoice()
    {
        localSpeaking = false;
        ChipiLocalCancel();
    }

    private IEnumerator SendLocal(byte[] wav)
    {
        EnsureDiagnosticSession();
        retryAudio = wav;
        busy = true;
        phase = VoicePhase.Thinking;
        status = "";
        Play("Idle");
        double started = Time.realtimeSinceStartupAsDouble;
        ChipiLocalStart(wav, wav.Length, conversationActive ? 1 : 0);
        var result = new byte[8192];
        int size = 0;
        while (Time.realtimeSinceStartupAsDouble - started < 60 && (size = ChipiLocalRead(result, result.Length)) == 0)
            yield return null;

        LocalReply answer = size > 0 ? JsonUtility.FromJson<LocalReply>(Encoding.UTF8.GetString(result, 0, size)) : null;
        if (!string.IsNullOrWhiteSpace(answer?.transcript))
        {
            transcript = answer.transcript;
            reply = "";
        }
        if (answer == null || !string.IsNullOrEmpty(answer.error) || string.IsNullOrWhiteSpace(answer.reply))
        {
            CancelLocalVoice();
            FailTurn(answer?.error ?? "On-device conversation timed out. Try again.");
            yield break;
        }

        reply = answer.reply;
        bool spoken = false;
        yield return SpeakWithLocalVoice(reply, started, success => spoken = success);
        if (!spoken)
        {
            FailTurn("On-device playback was interrupted. Try again.");
            yield break;
        }
        retryAudio = null;
        bool saved = !conversationActive || ChipiLocalRemember(transcript, reply) != 0;
        if (conversationActive && saved) hasSavedConversation = true;
        if (!saved)
        {
            microphoneMuted = true;
            phase = VoicePhase.Paused;
            busy = false;
            status = "This exchange couldn’t be saved. You can resume speaking.";
            Play("Idle");
            yield break;
        }
        yield return RearmConversation();
    }

#if DEVELOPMENT_BUILD
    private IEnumerator CheckVoiceComparison()
    {
        string[] lines =
        {
            "Hello, how can I help you?",
            "Nice! You’d say, I bought a new phone yesterday.",
            "That sounds tiring. I hope you get some rest tonight."
        };
        foreach (string line in lines)
        {
            reply = line;
            yield return PlayAppleSample(line, "");
            if (!string.IsNullOrEmpty(status))
            {
                Diagnostic("voice.comparison FAIL");
                yield break;
            }
        }
        Diagnostic("voice.comparison PASS");
    }

    private IEnumerator CheckAppleTurn()
    {
        yield return PlayAppleSample(reply, "");
        bool greetingPlayed = string.IsNullOrEmpty(status);
        var clip = Resources.Load<AudioClip>("DemoReply");
        yield return SendLocal(Encode(clip, clip.samples));
        bool passed = greetingPlayed && !string.IsNullOrWhiteSpace(transcript) &&
            string.IsNullOrEmpty(status) && phase == VoicePhase.Ready;
        Diagnostic($"apple.fixture_turn {(passed ? "PASS" : "FAIL")} status={status}");
    }
#endif

    private IEnumerator PlayAppleSample(string text, string label)
    {
        busy = true;
        bool spoken = false;
        yield return SpeakWithLocalVoice(text, Time.realtimeSinceStartupAsDouble, success => spoken = success);
        busy = false;
        phase = VoicePhase.Ready;
        status = spoken ? label : "Chipi’s voice couldn’t play.";
        Play("Idle");
        Diagnostic($"apple.sample_finished completed={spoken}");
    }

    private IEnumerator SpeakWithLocalVoice(string text, double started, Action<bool> completed)
    {
        ChipiAudioPlayback();
        ChipiLocalSpeak(text);
        double queued = Time.realtimeSinceStartupAsDouble;
        int state;
        while ((state = ChipiLocalPlayback()) == 1 && Time.realtimeSinceStartupAsDouble - queued < 30)
            yield return null;
        if (state == 2)
        {
            phase = VoicePhase.Speaking;
            status = "";
            localSpeaking = true;
            Play("Talking");
            Diagnostic($"turn.local_playback_started replyMs={(Time.realtimeSinceStartupAsDouble - started) * 1000:F0}");
            while ((state = ChipiLocalPlayback()) == 2 && Time.realtimeSinceStartupAsDouble - queued < 45)
                yield return null;
        }
        CancelLocalVoice();
        completed(state == 3);
    }
#endif

    private IEnumerator SendLegacy(byte[] wav, string endpointOverride = null, bool checkHistory = false, string diagnosticId = null)
    {
        diagnosticId ??= Guid.NewGuid().ToString("D");
        busy = true;
        Play("Idle");
        phase = VoicePhase.Thinking;
        bool rememberTurn = conversationActive || checkHistory;
        Debug.Log($"Chipi waiting for voice reply (context: {conversationHistory.Count} turns)");
        status = "";
        var parts = new List<IMultipartFormSection> {
            new MultipartFormFileSection("audio", wav, "recording.wav", "audio/wav")
        };
        if (rememberTurn && conversationHistory.Count > 0)
        {
            var history = new TurnHistory { turns = conversationHistory.ToArray() };
            parts.Add(new MultipartFormDataSection("history", JsonUtility.ToJson(history)));
        }
        string destination = endpointOverride ?? Endpoint;
        using var request = UnityWebRequest.Post(destination + "/api/turn", parts);
        request.SetRequestHeader("X-Chipi-Turn-Id", diagnosticId);
#if UNITY_IOS && DEVELOPMENT_BUILD && !CHIPI_SIMULATOR_BUILD
        if (destination == PlayerPrefs.GetString("DeviceEndpoint", ""))
            request.SetRequestHeader("X-Chipi-Device-Token", PlayerPrefs.GetString("DeviceToken", ""));
#endif
        request.timeout = 60;
        activeTurnRequest = request;
        yield return request.SendWebRequest();
        activeTurnRequest = null;
        Diagnostic($"turn.legacy_finished id={diagnosticId} http={request.responseCode} result={request.result} audioBytes={wav.Length}");
        if (request.result != UnityWebRequest.Result.Success)
        {
            status = VoiceError((int)request.responseCode);
            FailTurn(status);
            yield break;
        }
        Turn turn;
        try
        {
            turn = JsonUtility.FromJson<Turn>(request.downloadHandler.text);
            if (generated != null) Destroy(generated);
            generated = Decode(Convert.FromBase64String(turn.audioBase64));
        }
        catch (Exception)
        {
            status = "Chipi received unreadable audio.";
            FailTurn(status);
            yield break;
        }
        transcript = turn.transcript;
        reply = turn.reply;
        yield return Speak(generated, turn.mode == "demo" ? "Server demo · sample transcript" : "AI-generated voice");
        retryAudio = null;
        RememberTurn(rememberTurn);
        yield return RearmConversation();
    }

    private void RememberTurn(bool rememberTurn)
    {
        if (rememberTurn && !string.IsNullOrWhiteSpace(transcript) && !string.IsNullOrWhiteSpace(reply))
        {
            if (conversationHistory.Count >= MaxHistoryTurns) conversationHistory.RemoveAt(0);
            conversationHistory.Add(new HistoryTurn
            {
                transcript = LimitHistoryMessage(transcript),
                reply = LimitHistoryMessage(reply)
            });
        }
    }

    private static string VoiceError(int responseCode, string stage = null)
    {
        return responseCode switch
        {
            422 => "I didn't catch that. Speak clearly and try again.",
            429 when stage == "speech_synthesis" => "Chipi’s cloud voice is temporarily limited.",
            429 when stage == "transcription" => "The AI provider is limiting requests. Try again later.",
            429 => "The voice server is busy. Try again in a moment.",
            502 => "The voice provider is unavailable. Try the offline demo.",
            0 => "Can't connect to the voice server. Try the offline demo.",
            _ => $"Voice server returned {responseCode}. Try again."
        };
    }

    private static string LimitHistoryMessage(string message)
    {
        if (message.Length <= MaxHistoryMessageLength) return message;
        int length = MaxHistoryMessageLength;
        if (char.IsHighSurrogate(message[length - 1])) length--;
        return message[..length];
    }

    private IEnumerator Speak(AudioClip clip, string label)
    {
        busy = true;
        if (clip != null)
        {
            phase = VoicePhase.Speaking;
            status = label;
            peakSpeech = 0;
#if UNITY_IOS && !UNITY_EDITOR
            if (ChipiAudioPlayback() == 0)
                Debug.LogWarning("Chipi could not configure the playback audio session");
#endif
            speaker.clip = clip;
            speaker.Play();
            Debug.Log($"Chipi voice started: {clip.length:F2}s");
            Play("Talking");
            while (speaker.isPlaying) yield return null;
            Debug.Log($"Chipi voice finished: peak RMS {peakSpeech:F4}");
            phase = VoicePhase.Ready;
            if (!conversationActive)
            {
                Play("Happy");
                if (motion != null && motion["Happy"] != null)
                    yield return new WaitForSeconds(motion["Happy"].length);
            }
        }
        busy = false;
        phase = VoicePhase.Ready;
        Play("Idle");
    }

    private static byte[] Encode(AudioClip clip, int frames, int start = 0, bool nativeCapture = false)
    {
        var samples = new float[frames * clip.channels];
#if UNITY_IOS && !UNITY_EDITOR
        if (nativeCapture)
            ChipiMicRead(start, samples, samples.Length);
        else
#endif
        clip.GetData(samples, start);
        int wavFrames = Math.Min(Rate * (CaptureSeconds + 1), (int)((long)frames * Rate / clip.frequency));
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(0x46464952); writer.Write(36 + wavFrames * 2);
        writer.Write(0x45564157); writer.Write(0x20746d66); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(Rate);
        writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(0x61746164); writer.Write(wavFrames * 2);
        for (int frame = 0; frame < wavFrames; frame++)
        {
            float source = (float)frame * clip.frequency / Rate;
            int first = Mathf.Min((int)source, frames - 1);
            int second = Mathf.Min(first + 1, frames - 1);
            float mixed = 0;
            for (int channel = 0; channel < clip.channels; channel++)
                mixed += Mathf.Lerp(samples[first * clip.channels + channel],
                    samples[second * clip.channels + channel], source - first);
            writer.Write((short)(Mathf.Clamp(mixed / clip.channels, -1, 1) * 32767));
        }
        return buffer.ToArray();
    }

    private static AudioClip Decode(byte[] wav)
    {
        using var reader = new BinaryReader(new MemoryStream(wav));
        if (reader.ReadInt32() != 0x46464952) throw new FormatException();
        reader.ReadInt32();
        if (reader.ReadInt32() != 0x45564157) throw new FormatException();
        int rate = 0, channels = 0;
        var stream = reader.BaseStream;
        while (stream.Position + 8 <= stream.Length)
        {
            int id = reader.ReadInt32(), size = reader.ReadInt32();
            long end = stream.Position + size;
            if (size < 0 || end > stream.Length) throw new FormatException();
            if (id == 0x20746d66 && size >= 16)
            {
                if (reader.ReadInt16() != 1) throw new FormatException();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadInt16();
                if (reader.ReadInt16() != 16) throw new FormatException();
            }
            if (id == 0x61746164 && channels is 1 or 2 && rate >= 8000 && rate <= 48000 && size > 0 && size % (2 * channels) == 0)
            {
                var samples = new float[size / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
                var clip = AudioClip.Create("Chipi reply", samples.Length / channels, channels, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            stream.Position = end + (size & 1);
        }
        throw new FormatException();
    }

    private static GUIStyle Label(int size, Color color, bool bold = false)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal
        };
        style.normal.textColor = color;
        return style;
    }

    private void SetServerEnabled(bool enabled)
    {
        server = enabled;
        PlayerPrefs.SetInt("VoiceServerEnabled", enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private IEnumerator CheckMultipleTurns()
    {
        string endpoint = Environment.GetEnvironmentVariable("CHIPI_CHECK_ENDPOINT") ?? Endpoint;
        var fixture = Resources.Load<AudioClip>("DemoReply");
        byte[] audio = Encode(fixture, fixture.samples);
        conversationActive = true;
        for (int turn = 1; turn <= 3; turn++)
        {
            yield return Send(audio, endpoint);
            if (!conversationActive || phase != VoicePhase.Listening || conversationHistory.Count != turn)
            {
                Debug.LogError($"Chipi multi-turn check: FAIL at turn {turn}");
                StopConversation("Check failed.");
                yield break;
            }
        }
        StopConversation("Conversation ended.");
        StartCoroutine(CheckNewSession(endpoint, audio));
    }

    private IEnumerator CheckNewSession(string endpoint, byte[] audio)
    {
        bool cleared = !conversationActive && conversationHistory.Count == 0;
        conversationActive = true;
        yield return Send(audio, endpoint);
        bool passed = cleared && conversationActive && phase == VoicePhase.Listening && conversationHistory.Count == 1;
        Debug.Log($"Chipi multi-turn check: {(passed ? "PASS" : "FAIL")}");
        StopConversation("Conversation ended.");
    }

    private IEnumerator CheckControls(string mode)
    {
        string endpoint = Environment.GetEnvironmentVariable("CHIPI_CHECK_ENDPOINT") ?? Endpoint;
        conversationActive = true;
        conversationHistory.Add(new HistoryTurn { transcript = "fixture previous learner", reply = "fixture previous tutor" });
        if (mode.StartsWith("visual-", StringComparison.Ordinal))
        {
            reply = "That sounds interesting. What did you enjoy most about the experience, and what would you like to try next?";
            transcript = "I have been practicing English every day, but sometimes I need a little more time to find the right words.";
            if (mode == "visual-speaking") phase = VoicePhase.Speaking;
            else if (mode == "visual-muted") { phase = VoicePhase.Paused; microphoneMuted = true; }
            else { phase = VoicePhase.Ready; retryAudio = new byte[1]; status = VoiceError(429); }
            yield return null;
            yield return null;
            Debug.Log($"Chipi controls visual fixture {mode}: ready");
            yield break;
        }
        if (mode == "permission-background")
        {
            StartCoroutine(BeginCapture());
            bool pending = phase == VoicePhase.RequestingMicrophone;
            OnApplicationPause(true);
            mockMicBackgrounded = true;
            yield return VerifyPermissionStopped(mode, pending);
            yield break;
        }
        if (mode == "permission-end" || mode == "permission-mute")
        {
            StartCoroutine(BeginCapture());
            bool pending = phase == VoicePhase.RequestingMicrophone;
            if (mode == "permission-end") StopConversation("Conversation ended.");
            else SetMicrophoneMuted(true);
            StartCoroutine(VerifyPermissionStopped(mode, pending));
            yield break;
        }
        if (mode == "permission-dialog")
        {
            StartCoroutine(BeginCapture());
            bool pending = phase == VoicePhase.RequestingMicrophone;
            OnApplicationPause(true);
            OnApplicationPause(false);
            yield return new WaitForSecondsRealtime(3);
            bool resumed = pending && conversationActive && phase == VoicePhase.Listening && !microphoneMuted;
            Debug.Log($"Chipi controls check permission-dialog: {(resumed ? "PASS" : "FAIL")}");
            StopConversation("Conversation ended.");
            yield break;
        }
        if (mode == "listening")
        {
            yield return BeginCapture();
            SetMicrophoneMuted(true);
            bool paused = phase == VoicePhase.Paused && microphoneMuted && conversationHistory.Count == 1;
            SetMicrophoneMuted(false);
            bool resumed = phase == VoicePhase.Listening && !microphoneMuted && conversationHistory.Count == 1;
            Debug.Log($"Chipi controls check {mode}: {(paused && resumed ? "PASS" : "FAIL")}");
            StopConversation("Conversation ended.");
            yield break;
        }
        if (mode == "speak-again")
        {
            var sample = Resources.Load<AudioClip>("DemoReply");
            retryAudio = Encode(sample, sample.samples);
            microphoneMuted = true;
            phase = VoicePhase.Ready;
            SpeakAgain();
            bool resumed = phase == VoicePhase.Listening && !microphoneMuted && retryAudio == null && conversationHistory.Count == 1;
            Debug.Log($"Chipi controls check {mode}: {(resumed ? "PASS" : "FAIL")}");
            StopConversation("Conversation ended.");
            yield break;
        }
        if (mode == "no-speech")
        {
            recording = AudioClip.Create("Chipi silent fixture", Rate, 1, Rate, false);
            voiceEndpoint = new VoiceEndpoint();
            processedMicFrames = 0;
            phase = VoicePhase.Listening;
            StopCapture(0);
            bool paused = conversationActive && phase == VoicePhase.Paused && microphoneMuted && conversationHistory.Count == 1;
            SetMicrophoneMuted(false);
            bool resumed = phase == VoicePhase.Listening && !microphoneMuted && conversationHistory.Count == 1;
            Debug.Log($"Chipi controls check {mode}: {(paused && resumed ? "PASS" : "FAIL")}");
            StopConversation("Conversation ended.");
            yield break;
        }
        var fixture = Resources.Load<AudioClip>("DemoReply");
        StartCoroutine(Send(Encode(fixture, fixture.samples), endpoint));
        if (mode == "cancel-response")
        {
            InterruptReply();
            StartCoroutine(VerifyInterruptedReply(mode));
            yield break;
        }
        if (mode == "thinking") SetMicrophoneMuted(true);
        else
        {
            float startDeadline = Time.realtimeSinceStartup + 15;
            while (phase != VoicePhase.Speaking && conversationActive && Time.realtimeSinceStartup < startDeadline)
                yield return null;
            if (phase != VoicePhase.Speaking)
            {
                Debug.LogError($"Chipi controls check {mode}: FAIL (no playback)");
                StopConversation("Check failed.");
                yield break;
            }
            if (mode == "interrupt-muted") SetMicrophoneMuted(true);
            if (mode == "interrupt" || mode == "interrupt-muted")
            {
                InterruptReply();
                StartCoroutine(VerifyInterruptedReply(mode));
                yield break;
            }
            SetMicrophoneMuted(true);
        }
        float finishDeadline = Time.realtimeSinceStartup + 25;
        while (conversationActive && phase != VoicePhase.Paused && Time.realtimeSinceStartup < finishDeadline)
            yield return null;
        bool stayedMuted = phase == VoicePhase.Paused && microphoneMuted && recording == null &&
            !speaker.isPlaying && conversationHistory.Count == 2;
        SetMicrophoneMuted(false);
        bool resumedAfterReply = phase == VoicePhase.Listening && !microphoneMuted && conversationHistory.Count == 2;
        Debug.Log($"Chipi controls check {mode}: {(stayedMuted && resumedAfterReply ? "PASS" : "FAIL")}");
        StopConversation("Conversation ended.");
    }

    private IEnumerator VerifyPermissionStopped(string mode, bool pending)
    {
        yield return new WaitForSecondsRealtime(3);
        bool muted = mode == "permission-mute";
        bool stopped = pending && conversationActive == muted && phase == (muted ? VoicePhase.Paused : VoicePhase.Ready) &&
            recording == null && activeTurnRequest == null && !speaker.isPlaying && conversationHistory.Count == (muted ? 1 : 0);
        if (muted)
        {
            SetMicrophoneMuted(false);
            yield return new WaitForSecondsRealtime(3);
            stopped = stopped && conversationActive && phase == VoicePhase.Listening && !microphoneMuted;
        }
        Debug.Log($"Chipi controls check {mode}: {(stopped ? "PASS" : "FAIL")}");
        if (conversationActive) StopConversation("Conversation ended.");
    }

    private IEnumerator VerifyInterruptedReply(string mode)
    {
        yield return new WaitForSecondsRealtime(6);
        bool muted = mode == "interrupt-muted";
        bool interrupted = conversationActive && phase == (muted ? VoicePhase.Paused : VoicePhase.Listening) &&
            microphoneMuted == muted && activeTurnRequest == null && streamingClip == null && retryAudio == null &&
            !speaker.isPlaying && conversationHistory.Count == 1;
        if (muted) SetMicrophoneMuted(false);
        bool resumed = phase == VoicePhase.Listening && !microphoneMuted;
        Debug.Log($"Chipi controls check {mode}: {(interrupted && resumed ? "PASS" : "FAIL")}");
        StopConversation("Conversation ended.");
    }

    private IEnumerator CheckRecovery()
    {
        string endpoint = Environment.GetEnvironmentVariable("CHIPI_CHECK_ENDPOINT") ?? Endpoint;
        var fixture = Resources.Load<AudioClip>("DemoReply");
        byte[] audio = Encode(fixture, fixture.samples);
        conversationActive = true;
        conversationHistory.Add(new HistoryTurn { transcript = "fixture previous learner", reply = "fixture previous tutor" });
        yield return Send(audio, endpoint);
        bool retained = conversationActive && phase == VoicePhase.Ready && !busy && retryAudio != null &&
            conversationHistory.Count == 1 && recording == null && !speaker.isPlaying;
        if (retained) yield return Send(retryAudio, endpoint);
        bool recovered = retained && conversationActive && phase == VoicePhase.Listening && retryAudio == null &&
            conversationHistory.Count == 2 && conversationHistory[0].transcript == "fixture previous learner";
        StopConversation("Conversation ended.");
        Debug.Log($"Chipi recovery check: {(recovered ? "PASS" : "FAIL")}");
    }

    private IEnumerator CheckStreamCancellation(string mode)
    {
        string endpoint = Environment.GetEnvironmentVariable("CHIPI_CHECK_ENDPOINT") ?? Endpoint;
        var fixture = Resources.Load<AudioClip>("DemoReply");
        conversationActive = true;
        StartCoroutine(Send(Encode(fixture, fixture.samples), endpoint));
        if (mode == "thinking")
        {
            StopConversation("Conversation ended.");
            StartCoroutine(VerifyStreamCancellation());
            yield break;
        }
        float expires = Time.realtimeSinceStartup + 15;
        while (phase != VoicePhase.Speaking && conversationActive && Time.realtimeSinceStartup < expires)
            yield return null;
        if (phase != VoicePhase.Speaking)
        {
            Debug.LogError("Chipi cancellation check did not reach playback");
            StopConversation("Check failed.");
            yield break;
        }
        if (mode == "pause") OnApplicationPause(true);
        else StopConversation("Conversation ended.");
        StartCoroutine(VerifyStreamCancellation());
    }

    private IEnumerator VerifyStreamCancellation()
    {
        yield return new WaitForSecondsRealtime(6);
        bool stopped = !conversationActive && phase == VoicePhase.Ready && recording == null &&
            activeTurnRequest == null && streamingClip == null && !speaker.isPlaying && conversationHistory.Count == 0;
        Debug.Log($"Chipi streaming cancellation check: {(stopped ? "PASS" : "FAIL")}");
    }

    private IEnumerator CheckRoundTrip(bool checkHistory)
    {
        // Exercise network and playback with a WAV fixture instead of opening the microphone.
        var clip = Resources.Load<AudioClip>("DemoReply");
        AudioClip extended = null;
        if (Environment.GetEnvironmentVariable("CHIPI_CHECK_LONG_TURN") == "1")
        {
            var source = new float[clip.samples * clip.channels];
            if (!clip.GetData(source, 0)) throw new InvalidOperationException("Could not read the voice fixture.");
            var samples = new float[clip.frequency * 10 * clip.channels];
            for (int index = 0; index < samples.Length; index++) samples[index] = source[index % source.Length];
            extended = AudioClip.Create("Chipi long fixture", samples.Length / clip.channels, clip.channels, clip.frequency, false);
            extended.SetData(samples, 0);
            clip = extended;
        }
        byte[] wav = Encode(clip, clip.samples);
        Debug.Log($"Chipi round trip fixture duration: {clip.length:F2}s");
        if (extended != null) Destroy(extended);
        string localEndpoint = Environment.GetEnvironmentVariable("CHIPI_CHECK_ENDPOINT") ?? Endpoint;
        yield return Send(wav, localEndpoint, checkHistory);
        if (checkHistory && conversationHistory.Count == 1)
            yield return Send(wav, localEndpoint, true);
        if (checkHistory)
        {
            Debug.Log($"Chipi history check completed: {conversationHistory.Count} remembered turns");
            CloseConversation(status);
            Debug.Log($"Chipi history after end: {conversationHistory.Count} turns");
            yield return Send(wav, localEndpoint, true);
            Debug.Log($"Chipi new session history check: {conversationHistory.Count} remembered turns");
            CloseConversation(status);
        }
        Debug.Log($"Chipi round trip finished: {status}, state {phase}");
    }
#endif

    private void RunDemo()
    {
        transcript = "Demo: I like apples.";
        reply = "Nice! What is your favorite fruit?";
#if UNITY_IOS && !UNITY_EDITOR
        StartCoroutine(PlayAppleSample(reply, "Voice sample"));
#else
        StartCoroutine(Speak(Resources.Load<AudioClip>("DemoReply"), "Offline voice demo"));
#endif
    }

    private static void Surface(Rect rect, Color color, float radius)
    {
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, radius);
    }

    private static void MicIcon(Vector2 center, bool stop)
    {
        if (stop)
        {
            Surface(new Rect(center.x - 7, center.y - 7, 14, 14), Color.white, 3);
            return;
        }
        Surface(new Rect(center.x - 4, center.y - 10, 8, 14), Color.white, 4);
        Surface(new Rect(center.x - 8, center.y - 2, 2, 6), Color.white, 1);
        Surface(new Rect(center.x + 6, center.y - 2, 2, 6), Color.white, 1);
        Surface(new Rect(center.x - 7, center.y + 4, 14, 2), Color.white, 1);
        Surface(new Rect(center.x - 1, center.y + 5, 2, 5), Color.white, 1);
        Surface(new Rect(center.x - 5, center.y + 9, 10, 2), Color.white, 1);
    }

    private static float PortraitScale(Rect safe) =>
        Mathf.Min(safe.width / 390f, safe.height / 760f);

    private static float CardHeight(float height) =>
        Mathf.Clamp(height * .46f, 325f, 390f) + 20f;

    private static float CardTop(float height) =>
        height - FooterHeight - ControlAreaHeight - CardHeight(height);

    private void UpdateCharacterViewport()
    {
        Rect safe = Screen.safeArea;
        float scale = PortraitScale(safe);
        if (scale <= 0 || Screen.height <= 0) return;
        float height = safe.height / scale;
        float bottom = safe.yMax - (CardTop(height) - CharacterCardGap) * scale;
        float top = safe.yMax;
        var viewport = new Rect(0, bottom / Screen.height, 1, (top - bottom) / Screen.height);
        if (characterView.rect != viewport) characterView.rect = viewport;
    }

    private void DrawConversationCard(Rect card)
    {
        const float messageGap = 10;
        const float sideInset = 18;
        Surface(card, Color.white, 22);
        var viewport = new Rect(card.x + 12, card.y + 12, card.width - 24, card.height - 24);
        var learner = new GUIContent(transcript ?? "");
        var chipi = new GUIContent(reply ?? "");
        bool hasLearner = !string.IsNullOrWhiteSpace(transcript);
        bool hasChipi = !string.IsNullOrWhiteSpace(reply);
        float messageWidth = viewport.width - sideInset - 8;
        float learnerHeight = hasLearner ? DialogueMessageHeight(learner, messageWidth) : 0;
        float replyTop = learnerHeight + (hasLearner && hasChipi ? messageGap : 0);
        float replyHeight = hasChipi ? DialogueMessageHeight(chipi, messageWidth) : 0;
        float contentHeight = replyTop + replyHeight;
        float maxOffset = Mathf.Max(0, contentHeight - viewport.height);
        if (displayedReply != reply || displayedTranscript != transcript)
        {
            displayedReply = reply;
            displayedTranscript = transcript;
            // Keep the start of Chipi's reply visible when a long transcript fills the card.
            dialogueOffset = hasChipi ? Mathf.Min(replyTop, maxOffset) : 0;
        }
        UpdateDialogueScroll(viewport, maxOffset);

        GUI.BeginGroup(viewport);
        if (hasLearner)
            DrawDialogueMessage(new Rect(sideInset, -dialogueOffset, messageWidth, learnerHeight),
                "You", learner, new Color(.91f, .94f, .98f));
        if (hasChipi)
            DrawDialogueMessage(new Rect(0, replyTop - dialogueOffset, messageWidth, replyHeight),
                "Chipi", chipi, new Color(.95f, .97f, 1f));
        if (maxOffset > 0)
        {
            float thumbHeight = Mathf.Min(viewport.height, Mathf.Max(20, viewport.height * viewport.height / contentHeight));
            float thumbY = (viewport.height - thumbHeight) * dialogueOffset / maxOffset;
            Surface(new Rect(viewport.width - 3, thumbY, 3, thumbHeight), new Color(.53f, .65f, .82f), 1.5f);
        }
        GUI.EndGroup();
    }

    private float DialogueMessageHeight(GUIContent message, float width) =>
        47 + dialogueText.CalcHeight(message, width - 24);

    private void DrawDialogueMessage(Rect bubble, string speakerName, GUIContent message, Color background)
    {
        Surface(bubble, background, 14);
        GUI.Label(new Rect(bubble.x + 12, bubble.y + 8, bubble.width - 24, 24), speakerName, dialogueSpeaker);
        GUI.Label(new Rect(bubble.x + 12, bubble.y + 36, bubble.width - 24, bubble.height - 47), message, dialogueText);
    }

    private void UpdateDialogueScroll(Rect viewport, float maxOffset)
    {
        var input = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive, viewport);
        switch (input.GetTypeForControl(control))
        {
            case EventType.MouseDown when maxOffset > 0 && input.button == 0 && viewport.Contains(input.mousePosition):
                GUIUtility.hotControl = control;
                touchStartY = input.mousePosition.y;
                touchStartOffset = dialogueOffset;
                input.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == control:
                dialogueOffset = touchStartOffset + touchStartY - input.mousePosition.y;
                input.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == control:
                GUIUtility.hotControl = 0;
                input.Use();
                break;
            case EventType.Ignore when GUIUtility.hotControl == control:
                GUIUtility.hotControl = 0;
                break;
            case EventType.ScrollWheel when maxOffset > 0 && viewport.Contains(input.mousePosition):
                dialogueOffset += input.delta.y * 16;
                input.Use();
                break;
        }
        dialogueOffset = Mathf.Clamp(dialogueOffset, 0, maxOffset);
    }

    private void OnGUI()
    {
        if (!stylesReady)
        {
            body = Label(19, Ink, true);
            small = Label(13, Ink);
            dialogueText = Label(20, Ink);
            dialogueText.alignment = TextAnchor.UpperLeft;
            dialogueSpeaker = Label(14, Blue, true);
            dialogueSpeaker.alignment = TextAnchor.UpperLeft;
            primary = Label(18, Color.white, true);
            secondary = Label(14, Blue, true);
            phaseStyle = Label(14, Blue, true);
            stylesReady = true;
        }
        Rect safe = Screen.safeArea;
        float scale = PortraitScale(safe);
        if (scale <= 0) return;
        float height = safe.height / scale;
        var background = new Color(.88f, .93f, .99f);
        Rect viewport = characterView.pixelRect;
        float top = Screen.height - viewport.yMax;
        Surface(new Rect(0, 0, Screen.width, settingsOpen ? Screen.height : top + 1), background, 0);
        Surface(new Rect(0, Screen.height - viewport.yMin - 1, Screen.width, viewport.yMin + 1), background, 0);
        GUI.matrix = Matrix4x4.TRS(new Vector3(safe.center.x - 195 * scale, Screen.height - safe.yMax, 0),
            Quaternion.identity, new Vector3(scale, scale, 1));
        GUI.Label(new Rect(125, height - FooterHeight, 140, FooterHeight), "Chipi · v" + Application.version, small);

        if (settingsOpen)
        {
            DrawSettings();
            GUI.matrix = Matrix4x4.identity;
            return;
        }

        float cardTop = CardTop(height);
        DrawConversationCard(new Rect(16, cardTop, 358, CardHeight(height)));
        height -= FooterHeight;

        string phaseLabel = phase switch
        {
            VoicePhase.RequestingMicrophone => "Opening microphone…",
            VoicePhase.Listening => "Listening…",
            VoicePhase.Thinking => "Thinking…",
            VoicePhase.Speaking => "Chipi is speaking…",
            VoicePhase.Paused => "Microphone muted",
            _ => "Ready to practice"
        };
        GUI.Label(new Rect(24, height - 161, 342, 20), phaseLabel, phaseStyle);
        GUI.Label(new Rect(24, height - 139, 342, 32), status, small);

        bool listening = phase == VoicePhase.Listening;
        var talk = new Rect(24, height - 104, 342, 52);
        Color buttonColor = listening ? new Color(.68f, .18f, .27f) :
            busy ? new Color(.28f, .39f, .57f) : new Color(.11f, .32f, .72f);
        Surface(talk, buttonColor, 26);
        var iconCenter = new Vector2(talk.x + 30, talk.center.y);
        if (listening)
        {
            float radius = 12 + micLevel * 10;
            Surface(new Rect(iconCenter.x - radius, iconCenter.y - radius, radius * 2, radius * 2),
                new Color(1, 1, 1, .14f + micLevel * .24f), radius);
        }
        MicIcon(iconCenter, listening);
        bool retrying = conversationActive && phase == VoicePhase.Ready && retryAudio != null;
        GUI.Label(new Rect(talk.x + 48, talk.y, talk.width - 96, talk.height),
            listening ? "Send now" : retrying ? "Retry" : conversationActive && phase == VoicePhase.Speaking ? "Interrupt"
                : conversationActive && phase == VoicePhase.Thinking ? "Cancel response"
                : conversationActive && phase == VoicePhase.Paused ? "Resume" : conversationActive ? "End conversation"
                : phase == VoicePhase.Ready ? (hasSavedConversation ? "Continue conversation" : "Start conversation") : phaseLabel, primary);
        if ((!busy || conversationActive) && GUI.Button(talk, GUIContent.none, GUIStyle.none))
        {
            if (recording != null)
            {
                int frames = CapturePosition();
                UpdateMicLevel(frames);
                StopCapture(frames, manual: true);
            }
            else if (retrying) StartCoroutine(Send(retryAudio));
            else if (conversationActive && phase is VoicePhase.Speaking or VoicePhase.Thinking) InterruptReply();
            else if (conversationActive && phase == VoicePhase.Paused) SetMicrophoneMuted(false);
            else if (conversationActive) StopConversation("Conversation ended.");
            else StartConversation();
        }

        if (conversationActive)
        {
            if (phase == VoicePhase.Paused)
            {
                if (GUI.Button(new Rect(28, height - 46, 334, 44), "End conversation", secondary))
                    StopConversation("Conversation ended.");
            }
            else
            {
                if (GUI.Button(new Rect(28, height - 46, 164, 44), retrying ? "Speak again" : microphoneMuted ? "Unmute" : "Mute", secondary))
                {
                    if (retrying) SpeakAgain();
                    else SetMicrophoneMuted(!microphoneMuted);
                }
                if (GUI.Button(new Rect(198, height - 46, 164, 44), "End conversation", secondary))
                    StopConversation("Conversation ended.");
            }
        }
        else if (!conversationActive && !busy && recording == null)
        {
            if (GUI.Button(new Rect(28, height - 46, 169, 44), "Try voice sample", secondary)) RunDemo();
            if (GUI.Button(new Rect(198, height - 46, 164, 44), "Voice settings", secondary)) settingsOpen = true;
        }
        GUI.matrix = Matrix4x4.identity;
    }

    private void LogReplyTiming(string diagnosticId, StreamTurn turn, double request, double readyAt, double audioAt,
        double captureStopped, int pauseMs)
    {
        static int Ms(double seconds) => Mathf.Max(0, (int)Math.Round(seconds * 1000));
        double now = Time.realtimeSinceStartupAsDouble;
        int prepare = captureStopped < 0 ? -1 : Ms(request - captureStopped);
        int ready = Ms(readyAt - request);
        int delivery = Math.Max(0, Ms(audioAt - readyAt) - turn.textMs - turn.ttsFirstMs);
        int playback = Ms(now - audioAt);
        int total = Math.Max(0, pauseMs) + Math.Max(0, prepare) + Ms(now - request);
        Diagnostic($"turn.timing id={diagnosticId} pauseMs={pauseMs} prepareMs={prepare} readyMs={ready} " +
            $"text={turn.textMs} ttsFirst={turn.ttsFirstMs} delivery={delivery} " +
            $"playback={playback} total={total} providerTiming={turn.timing}");
    }

    private void StartConversation()
    {
#if UNITY_IOS && !UNITY_EDITOR
        int availability = ChipiLocalModelAvailability();
        if (availability != 0)
        {
            status = availability switch
            {
                1 => "Conversation requires iOS 26 or later.",
#if CHIPI_SIMULATOR_BUILD
                _ => "Apple Intelligence is unavailable here. Try the voice sample.",
#else
                2 => "This iPhone doesn’t support Apple Intelligence.",
                3 => "Turn on Apple Intelligence in Settings.",
                4 => "Apple Intelligence isn’t ready yet. Try again later.",
                _ => "Apple Intelligence is unavailable. Try again later."
#endif
            };
            return;
        }
#else
        if (!server)
        {
            status = "Set up a voice server, or try the demo.";
            settingsOpen = true;
            return;
        }
#endif
        conversationHistory.Clear();
        conversationActive = true;
        StartCoroutine(BeginCapture());
    }

    private void DrawSettings()
    {
        float top = 28;
        Surface(new Rect(24, top, 342, 262), Color.white, 22);
        GUI.Label(new Rect(36, top + 14, 318, 36), "Voice settings", body);
#if UNITY_IOS && !UNITY_EDITOR
        GUI.Label(new Rect(42, top + 58, 306, 88),
            "Chipi uses Apple Intelligence for replies and one offline English voice for speech.", small);
#else
        GUI.Label(new Rect(42, top + 58, 306, 49),
            server ? "Voice server is enabled." : "Connect to a test server to use the microphone.", small);
        if (GUI.Button(new Rect(42, top + 112, 306, 42), server ? "Change voice server" : "Connect voice server", secondary))
        {
            SetServerEnabled(true);
            settingsOpen = false;
        }
        if (server && GUI.Button(new Rect(42, top + 154, 306, 40), "Disconnect", secondary))
            SetServerEnabled(false);
#endif
        if (GUI.Button(new Rect(42, top + 202, 306, 44), "Done", secondary))
            settingsOpen = false;
    }
}
