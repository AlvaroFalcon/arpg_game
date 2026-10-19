#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.Run;

namespace ArpgGame.Tests;

/// <summary>Shared helpers for both ends of the co-op test.</summary>
public abstract partial class CoopTestDriver : Node
{
    protected abstract string LogPath { get; }

    protected abstract string Tag { get; }

    private readonly List<string> _lines = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Headless runs are otherwise uncapped: --quit-after counts frames, and a
        // real game runs at a frame rate, not as fast as the CPU allows.
        Engine.MaxFps = 60;
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        _ = Main();
    }

    protected abstract Task Main();

    protected void Log(string line)
    {
        string text = $"[{Tag}] {line}";
        GD.Print(text);
        _lines.Add(text);
        using FileAccess? f = FileAccess.Open(LogPath, FileAccess.ModeFlags.Write);
        f?.StoreString(string.Join("\n", _lines) + "\n");
    }

    protected async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    protected async Task Seconds(float seconds)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(seconds * 1000f);
        while (Time.GetTicksMsec() < until)
        {
            await Frames(1);
        }
    }

    protected async Task<bool> WaitFor(Func<bool> condition, float timeout)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(timeout * 1000f);
        while (Time.GetTicksMsec() < until)
        {
            if (condition())
            {
                return true;
            }

            await Frames(1);
        }

        return condition();
    }

    protected Profile? SetUpProfile(string name, string classId)
    {
        Profile? profile = Profile.Get(this);
        if (profile is null)
        {
            return null;
        }

        // Never touch the real save: an in-memory test character only.
        profile.Persist = false;
        profile.CreateTestCharacter(name, classId);
        return profile;
    }

    protected RunController? Run => GetTree().CurrentScene as RunController;

    protected static Dictionary<string, string> Fields(string message)
    {
        var map = new Dictionary<string, string>();
        foreach (string part in message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                map[kv[0]] = kv[1];
            }
        }

        return map;
    }

    protected static float F(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : float.NaN;

    protected static string Num(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>Hosts on a random free UDP port in 7790–8190 (tests: no router, the test code).</summary>
    protected async Task<(Error Error, int Port)> HostOnFreePort(NetSession session, Characters.CharacterData character)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        Error hosted = Error.Failed;
        int port = 0;
        for (int attempt = 0; attempt < 5 && hosted != Error.Ok; attempt++)
        {
            port = 7790 + rng.RandiRange(0, 400);
            hosted = session.Host(port, character, JoinCode.TestCode, openRouterPort: false);
        }

        await Frames(1);
        return (hosted, port);
    }

    /// <summary>Starts another headless Godot process on <paramref name="scene"/> with user args (returns its pid).</summary>
    protected static int StartProcess(string scene, int quitAfter, params string[] userArgs)
    {
        var args = new List<string> { "--headless", "--path", ProjectSettings.GlobalizePath("res://"), scene, "--quit-after", quitAfter.ToString(CultureInfo.InvariantCulture), "--" };
        args.AddRange(userArgs);
        return OS.CreateProcess(OS.GetExecutablePath(), args.ToArray());
    }

    protected static string ReadFile(string path)
    {
        using FileAccess? f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f?.GetAsText() ?? "";
    }

    /// <summary>The value of "--name=value" on the command line (after --), or null.</summary>
    protected static string? UserArg(string name)
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv[0] == name)
            {
                return kv.Length == 2 ? kv[1] : "";
            }
        }

        return null;
    }
}
