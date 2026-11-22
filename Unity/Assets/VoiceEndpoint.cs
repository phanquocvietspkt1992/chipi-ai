using System;

public sealed class VoiceEndpoint
{
    public const int FrameMilliseconds = 20;
    public const int SilenceMilliseconds = 1160;
    public const int TrailingAudioMilliseconds = 240;
    private const int OnsetFrames = 5;
    private const int OnsetGapFrames = 2;
    private const int PreRollFrames = 50;
    private const int NoiseWarmupFrames = 10;
    private const float StrongSpeechLevel = .025f;
    private const float MinSilenceThreshold = .0068f;
    private float noise = .0025f;
    private float speechLevel;
    private int noiseSamples;
    private int onset;
    private int onsetGap;
    private int windows;
    private int firstSpeechWindow;
    private int silentFrames;

    public bool HasSpeech { get; private set; }
    public float NoiseLevel => noise;
    public float StartThreshold => Math.Max(.006f, noise * 1.6f);
    public float SilenceThreshold => Math.Max(Math.Max(MinSilenceThreshold, noise * 1.3f),
        noiseSamples >= 5 ? 0 : Math.Min(.007f, speechLevel * .25f));
    public float WarmupNoise { get; private set; } = .0025f;
    public int LongestOnset { get; private set; }
    public int ObservedSilenceMilliseconds => silentFrames * FrameMilliseconds;
    public bool Ended => HasSpeech && silentFrames * FrameMilliseconds >= SilenceMilliseconds;

    public void Observe(float level)
    {
        if (Ended) return;
        windows++;
        if (!HasSpeech && windows <= NoiseWarmupFrames && level < StrongSpeechLevel)
        {
            noise += (level - noise) * .28f;
            WarmupNoise = noise;
            noiseSamples++;
            onset = 0;
            onsetGap = 0;
            return;
        }
        float threshold = HasSpeech ? SilenceThreshold : StartThreshold;
        if (level >= threshold)
        {
            if (!HasSpeech)
            {
                onset++;
                LongestOnset = Math.Max(LongestOnset, onset);
                if (onset >= OnsetFrames)
                {
                    firstSpeechWindow = Math.Max(0, windows - PreRollFrames);
                    HasSpeech = true;
                }
            }
            onsetGap = 0;
            if (HasSpeech)
            {
                speechLevel = Math.Max(level, speechLevel * .995f);
                silentFrames = 0;
            }
            return;
        }

        if (HasSpeech) silentFrames++;
        else
        {
            if (++onsetGap > OnsetGapFrames) onset = 0;
            noise += (level - noise) * .065f;
        }
    }

    public int FirstSpeechFrame(int framesPerWindow) => firstSpeechWindow * framesPerWindow;

    public int EndFrame(int processed, int framesPerWindow)
    {
        int tail = TrailingAudioMilliseconds / FrameMilliseconds;
        return Math.Max(0, processed - Math.Max(0, silentFrames - tail) * framesPerWindow);
    }
}
