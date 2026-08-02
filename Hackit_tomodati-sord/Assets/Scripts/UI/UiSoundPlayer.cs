using UnityEngine;

/// <summary>UI操作と錬成演出用の短い効果音を実行時に生成する。</summary>
public static class UiSoundPlayer
{
    const int SampleRate = 44100;
    static AudioClip _move, _confirm, _cancel, _forge, _draw;

    public static AudioSource AddSource(GameObject owner, float volume = 0.7f)
    {
        AudioSource source = owner.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = volume;
        return source;
    }

    public static void Move(AudioSource source)
    {
        if (_move == null) _move = BuildTone("UiMove", 0.055f, 660f, 930f, 0.24f, 0.02f);
        Play(source, _move, 0.52f);
    }

    public static void Confirm(AudioSource source)
    {
        if (_confirm == null) _confirm = BuildTone("UiConfirm", 0.18f, 520f, 1040f, 0.35f, 0.03f, 1.5f);
        Play(source, _confirm, 0.78f);
    }

    public static void Cancel(AudioSource source)
    {
        if (_cancel == null) _cancel = BuildTone("UiCancel", 0.13f, 430f, 210f, 0.28f, 0.025f);
        Play(source, _cancel, 0.62f);
    }

    public static void Forge(AudioSource source)
    {
        if (_forge == null) _forge = BuildForgeClip();
        Play(source, _forge, 0.72f);
    }

    public static void DrawSword(AudioSource source)
    {
        if (_draw == null) _draw = BuildDrawClip();
        Play(source, _draw, 0.95f);
    }

    static void Play(AudioSource source, AudioClip clip, float scale)
    {
        if (source != null && clip != null) source.PlayOneShot(clip, scale);
    }

    static AudioClip BuildTone(string name, float duration, float startHz, float endHz,
        float amplitude, float attack, float harmonic = 0f)
    {
        int count = Mathf.CeilToInt(duration * SampleRate);
        float[] data = new float[count];
        float phase = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float p = i / (float)Mathf.Max(1, count - 1);
            phase += 2f * Mathf.PI * Mathf.Lerp(startHz, endHz, p) / SampleRate;
            float envelope = Mathf.Min(1f, t / Mathf.Max(0.001f, attack)) * Mathf.Pow(1f - p, 2.2f);
            float wave = Mathf.Sin(phase) + (harmonic > 0f ? Mathf.Sin(phase * harmonic) * 0.32f : 0f);
            data[i] = wave * envelope * amplitude;
        }
        return CreateClip(name, data);
    }

    static AudioClip BuildForgeClip()
    {
        int count = Mathf.CeilToInt(0.48f * SampleRate);
        float[] data = new float[count];
        uint noise = 0x1234abcd;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float p = i / (float)(count - 1);
            float swell = Mathf.Sin(p * Mathf.PI);
            noise = noise * 1664525u + 1013904223u;
            float spark = ((noise >> 9) / 8388608f - 1f) * 0.035f;
            float hum = Mathf.Sin(2f * Mathf.PI * (95f + 85f * p) * t) * 0.18f;
            float shimmer = Mathf.Sin(2f * Mathf.PI * (620f + 760f * p) * t) * 0.10f;
            data[i] = (hum + shimmer + spark) * swell;
        }
        return CreateClip("ForgeStart", data);
    }

    static AudioClip BuildDrawClip()
    {
        int count = Mathf.CeilToInt(0.62f * SampleRate);
        float[] data = new float[count];
        uint noise = 0x7f4a7c15;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float p = i / (float)(count - 1);
            noise = noise * 1103515245u + 12345u;
            float n = (noise >> 9) / 8388608f - 1f;
            float whoosh = n * Mathf.Sin(Mathf.Clamp01(p / 0.72f) * Mathf.PI) * 0.20f;
            float metalEnvelope = p < 0.22f ? p / 0.22f : Mathf.Exp(-(p - 0.22f) * 4.5f);
            float metal = (Mathf.Sin(2f * Mathf.PI * 980f * t) + Mathf.Sin(2f * Mathf.PI * 1470f * t) * 0.48f) * metalEnvelope * 0.16f;
            float impact = Mathf.Sin(2f * Mathf.PI * 82f * t) * Mathf.Exp(-t * 15f) * 0.24f;
            data[i] = Mathf.Clamp(whoosh + metal + impact, -0.95f, 0.95f);
        }
        return CreateClip("SwordDrawReveal", data);
    }

    static AudioClip CreateClip(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
