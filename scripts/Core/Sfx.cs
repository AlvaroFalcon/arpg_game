#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace ArpgGame.Core;

/// <summary>
/// Procedural sound effects and music — docs/design/menus.md -> "Audio". The
/// project ships no audio assets yet, so every sound is synthesised once at
/// startup into an <see cref="AudioStreamWav"/> (a few short envelopes over
/// sine/square/noise) and played from a small voice pool on the SFX bus. Two
/// looping music beds are synthesised the same way and play on the Music bus.
/// Placeholder-quality by design; swapping any of them for a real asset later
/// is a one-line change in <see cref="_Ready"/>.
///
/// Callers use the static <see cref="Play"/> helper, which no-ops when the
/// autoload is absent (headless test scenes).
/// </summary>
[GlobalClass]
public partial class Sfx : Node
{
    private const int Rate = 22050;
    private const int Voices = 20;

    private readonly Dictionary<string, AudioStreamWav> _sounds = new();
    private readonly Dictionary<string, ulong> _lastPlayed = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private AudioStreamPlayer _music = null!;
    private string _currentMusic = "";
    private readonly Random _rng = new(1234);

    private static readonly Dictionary<string, int> Requested = new();

    /// <summary>The sound most recently asked for, whether or not an audio device played it (tests).</summary>
    public static string LastRequested { get; private set; } = "";

    /// <summary>How many times a sound was asked for since the game started (tests).</summary>
    public static int RequestCount(string name) => Requested.GetValueOrDefault(name);

    public static void Play(Node context, string name, float volumeDb = 0f)
    {
        LastRequested = name;
        Requested[name] = Requested.GetValueOrDefault(name) + 1;
        // A delayed callback can outlive its node: a freed context just stays silent.
        if (!IsInstanceValid(context) || !context.IsInsideTree())
        {
            return;
        }

        context.GetTree().Root.GetNodeOrNull<Sfx>("Sfx")?.PlayNamed(name, volumeDb);
    }

    public static void Music(Node context, string name)
    {
        if (!IsInstanceValid(context) || !context.IsInsideTree())
        {
            return;
        }

        context.GetTree().Root.GetNodeOrNull<Sfx>("Sfx")?.PlayMusic(name);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < Voices; i++)
        {
            var player = new AudioStreamPlayer { Bus = GameSettings.SfxBus };
            AddChild(player);
            _pool.Add(player);
        }

        _music = new AudioStreamPlayer { Bus = GameSettings.MusicBus, VolumeDb = -8f };
        AddChild(_music);

        _sounds["shoot"] = Build(0.12f, t => Square(t, 520f - 1800f * t) * Env(t, 0.12f) * 0.35f);
        _sounds["spark"] = Build(0.1f, t => (Noise() * 0.5f + Square(t, 1400f)) * Env(t, 0.1f) * 0.3f);
        _sounds["nova"] = Build(0.35f, t => (Sine(t, 300f + 600f * t) * 0.5f + Noise() * 0.3f) * Env(t, 0.35f) * 0.45f);
        _sounds["hit"] = Build(0.07f, t => (Noise() * 0.7f + Square(t, 180f)) * Env(t, 0.07f) * 0.3f);
        _sounds["hurt"] = Build(0.2f, t => Square(t, 220f - 300f * t) * Env(t, 0.2f) * 0.45f);
        _sounds["explode"] = Build(0.45f, t => Noise() * Env(t, 0.45f) * Lowpass(0.25f) * 0.9f);
        _sounds["pickup"] = Build(0.08f, t => Sine(t, 1200f + 2400f * t) * Env(t, 0.08f) * 0.25f);
        _sounds["levelup"] = Build(0.7f, t => Arp(t, new[] { 523f, 659f, 784f, 1047f }, 0.12f) * Env(t, 0.7f) * 0.35f);
        _sounds["dodge"] = Build(0.18f, t => Noise() * Env(t, 0.18f) * (0.2f + 0.5f * t) * 0.5f);
        _sounds["death"] = Build(0.18f, t => (Noise() * 0.6f + Sine(t, 140f)) * Env(t, 0.18f) * 0.35f);
        _sounds["boss_warn"] = Build(0.9f, t => Square(t, 110f) * (Mathf.Sin(t * 40f) > 0 ? 1f : 0.3f) * Env(t, 0.9f) * 0.35f);
        _sounds["slam"] = Build(0.5f, t => (Sine(t, 60f - 30f * t) + Noise() * Lowpass(0.3f) * 0.8f) * Env(t, 0.5f) * 0.7f);
        // The co-op level-up countdown: a dry wood-block click each second, a
        // higher, brighter one in the last five (docs/design/menus.md -> "Level-up and results").
        _sounds["tick"] = Build(0.05f, t => (Sine(t, 1050f) * 0.7f + Sine(t, 2100f) * 0.3f) * Mathf.Exp(-t * 90f) * 0.45f);
        _sounds["tick_urgent"] = Build(0.07f, t => (Sine(t, 1550f) * 0.6f + Square(t, 3100f) * 0.15f) * Mathf.Exp(-t * 70f) * 0.55f);
        _sounds["ui"] = Build(0.05f, t => Square(t, 880f) * Env(t, 0.05f) * 0.2f);
        _sounds["victory"] = Build(1.4f, t => Arp(t, new[] { 392f, 523f, 659f, 784f, 1047f, 1319f }, 0.16f) * Env(t, 1.4f) * 0.35f);
        _sounds["defeat"] = Build(1.2f, t => Arp(t, new[] { 392f, 330f, 262f, 196f }, 0.25f) * Env(t, 1.2f) * 0.35f);

        // Quests (docs/design/quests.md -> "The quest toast"): a rising two-note chime when
        // one unlocks, a four-note arpeggio when its objective is met, a coin ring on claiming.
        _sounds["quest_new"] = Build(0.55f, t => (Arp(t, new[] { 659f, 988f }, 0.12f) * 0.8f + Sine(t, 1976f) * 0.15f) * Env(t, 0.55f) * 0.35f);
        _sounds["quest_done"] = Build(0.8f, t => (Arp(t, new[] { 523f, 659f, 784f, 1047f }, 0.08f) + Sine(t, 262f) * 0.2f) * Env(t, 0.8f) * 0.32f);
        _sounds["quest_claim"] = Build(0.6f, t => (Sine(t, 1800f + 500f * Mathf.Sin(t * 70f)) * 0.5f + Noise() * 0.08f) * Env(t, 0.6f) * 0.3f);

        _sounds["music_menu"] = BuildMusic(new[] { 110f, 87.3f, 98f, 82.4f }, 0.5f, false);
        _sounds["music_run"] = BuildMusic(new[] { 110f, 110f, 87.3f, 98f }, 0.25f, true);
        _sounds["music_boss"] = BuildMusic(new[] { 82.4f, 87.3f, 82.4f, 98f }, 0.18f, true);
    }

    private void PlayNamed(string name, float volumeDb)
    {
        if (!_sounds.TryGetValue(name, out AudioStreamWav? stream))
        {
            return;
        }

        // Throttle: a hundred simultaneous hits should not be a hundred voices.
        ulong now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(name, out ulong last) && now - last < 45)
        {
            return;
        }

        _lastPlayed[name] = now;
        foreach (AudioStreamPlayer player in _pool)
        {
            if (player.Playing)
            {
                continue;
            }

            player.Stream = stream;
            player.VolumeDb = volumeDb;
            player.PitchScale = 0.92f + (float)_rng.NextDouble() * 0.16f;
            player.Play();
            return;
        }
    }

    private void PlayMusic(string name)
    {
        if (name == _currentMusic || !_sounds.TryGetValue(name, out AudioStreamWav? stream))
        {
            return;
        }

        _currentMusic = name;
        _music.Stream = stream;
        _music.Play();
    }

    // --- synthesis ---------------------------------------------------------

    private static float Sine(float t, float f) => Mathf.Sin(Mathf.Tau * f * t);

    private static float Square(float t, float f) => Mathf.Sin(Mathf.Tau * f * t) >= 0f ? 1f : -1f;

    private float _lp;

    private float Noise() => (float)_rng.NextDouble() * 2f - 1f;

    /// <summary>One-pole lowpass over the most recent noise sample.</summary>
    private float Lowpass(float a)
    {
        _lp += a * (Noise() - _lp);
        return _lp / Mathf.Max(0.05f, a);
    }

    private static float Env(float t, float length) =>
        Mathf.Min(1f, t / 0.005f) * Mathf.Pow(Mathf.Max(0f, 1f - t / length), 2f);

    private static float Arp(float t, float[] notes, float step)
    {
        int i = Mathf.Min(notes.Length - 1, (int)(t / step));
        return Square(t, notes[i]) * 0.5f + Sine(t, notes[i] * 2f) * 0.5f;
    }

    private static AudioStreamWav Build(float seconds, Func<float, float> sample)
    {
        int count = (int)(seconds * Rate);
        byte[] data = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            float v = Mathf.Clamp(sample(i / (float)Rate), -1f, 1f);
            short s = (short)(v * 30000);
            data[i * 2] = (byte)(s & 0xff);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }

        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }

    /// <summary>A looping 4-bar bed: a bass drone per bar plus a minor arpeggio
    /// at <paramref name="step"/> seconds per note, optional kick.</summary>
    private AudioStreamWav BuildMusic(float[] roots, float step, bool drums)
    {
        const float bar = 2f;
        float total = bar * roots.Length;
        AudioStreamWav wav = Build(total, t =>
        {
            int b = Mathf.Min(roots.Length - 1, (int)(t / bar));
            float root = roots[b];
            float tb = t - b * bar;
            float bass = Sine(t, root) * 0.35f + Sine(t, root * 0.5f) * 0.25f;
            float[] arp = { 1f, 1.2f, 1.5f, 2f, 1.5f, 1.2f };
            int n = (int)(tb / step) % arp.Length;
            float tn = tb % step;
            float lead = Square(t, root * 2f * arp[n]) * Mathf.Exp(-tn * 9f) * 0.12f;
            float kick = 0f;
            if (drums)
            {
                float tk = tb % 0.5f;
                kick = Sine(tk, 70f - 60f * tk) * Mathf.Exp(-tk * 14f) * 0.5f;
            }

            return (bass + lead + kick) * 0.6f;
        });
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopBegin = 0;
        wav.LoopEnd = (int)(total * Rate);
        return wav;
    }
}
