#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Core;
using ArpgGame.Net;
using ArpgGame.Run;

namespace ArpgGame.Tests;

/// <summary>
/// Headless co-op security test — docs/design/multiplayer.md -> "Security".
///   ./scripts/run_all_tests.sh SecuritySmokeTest
///
/// This process hosts and attacks itself, the way a stranger or a broken
/// friend's client would: raw ENet peers that never send a code (slot holding),
/// an oversized auth packet, a pre-auth junk flood, a peer with the code that
/// never says hello but sends run traffic, calls to host-only RPCs, malformed
/// characters, connection churn. Then it turns hostile itself: a second Godot
/// process joins as a client (<c>-- --sec-role=client</c>) and this host feeds
/// it a poisoned lobby, monster spawns of non-monster scenes, NaN/infinite
/// numbers, 20 000 never-ending effects, an attempt to remove the client's own
/// player and a run end with an infinite clock. The client must survive, and
/// the profile it saves must still be a valid, finite save file.
///
/// Judged by its RESULT line (mirrored to user://test_results/SecuritySmokeTest.txt).
/// </summary>
public partial class SecuritySmokeTest : Node
{
    public override void _Ready()
    {
        string role = "host";
        int port = 0;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            if (kv.Length == 2 && kv[0] == "sec-role")
            {
                role = kv[1];
            }
            else if (kv.Length == 2 && kv[0] == "sec-port")
            {
                port = kv[1].ToInt();
            }
        }

        Node driver = role == "client"
            ? new SecurityTestClient { Port = port, Name = "SecurityTestDriver" }
            : new SecurityTestHost { Name = "SecurityTestDriver" };
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
    }
}

/// <summary>A bare ENet connection that speaks no Godot protocol unless told to:
/// what a stranger scanning the port has.</summary>
public sealed class RawPeer
{
    public readonly ENetMultiplayerPeer Peer = new();

    public int Received { get; private set; }

    public bool Connected => Peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

    public bool Gone => Peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected;

    public RawPeer(int port) => Peer.CreateClient("127.0.0.1", port);

    public void Poll()
    {
        if (Gone)
        {
            return;
        }

        Peer.Poll();
        while (!Gone && Peer.GetAvailablePacketCount() > 0)
        {
            Peer.GetPacket();
            Received++;
        }
    }

    /// <summary>SceneMultiplayer's authentication message: SYS (7), AUTH (0), payload.</summary>
    public void SendAuth(byte[] payload)
    {
        var packet = new byte[payload.Length + 2];
        packet[0] = 7;
        payload.CopyTo(packet, 2);
        Send(packet);
    }

    public void Send(byte[] packet)
    {
        if (!Connected)
        {
            return;
        }

        Peer.SetTargetPeer(1);
        Peer.TransferChannel = 0;
        Peer.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        Peer.PutPacket(packet);
    }

    public void Close()
    {
        if (!Gone)
        {
            Peer.Close();
        }
    }
}

/// <summary>Stands in for the run scene on the hostile host: only notices the client's "ready".</summary>
public sealed class ReadyWatcher : IRunLink
{
    public bool Ready { get; private set; }

    public void OnSnapshot(byte[] data)
    {
    }

    public void OnHostEvents(byte[] data)
    {
    }

    public void OnClientInput(long peer, byte[] data)
    {
    }

    public void OnClientEvents(long peer, byte[] data) => Ready |= data.Length > 0 && data[0] == RiftProtocol.ClientReady;

    public void OnPeerLeft(long peer)
    {
    }

    public void OnHostLost(string reason)
    {
    }
}

public partial class SecurityTestHost : CoopTestDriver
{
    protected override string LogPath => "user://test_results/SecuritySmokeTest.txt";

    protected override string Tag => "SEC";

    private const string ClientLogPath = "user://test_results/SecuritySmokeTest_client.txt";
    public const string ClientProfilePath = "user://test_results/security_client_profile.json";

    private bool _failed;
    private int _port;
    private int _pid = -1;
    private int _intruders;
    private readonly List<RawPeer> _raw = new();

    private void Check(string name, bool ok, string detail)
    {
        Log($"{name}: {(ok ? "PASS" : "FAIL")} — {detail}");
        _failed |= !ok;
    }

    public override void _Process(double delta)
    {
        foreach (RawPeer raw in _raw)
        {
            raw.Poll();
        }
    }

    protected override async Task Main()
    {
        await Frames(2);
        Log("Ready.");
        try
        {
            await Scenario();
        }
        catch (Exception e)
        {
            Check("NO-EXCEPTIONS", false, e.ToString());
        }

        if (_pid > 0)
        {
            OS.Kill(_pid);
        }

        foreach (RawPeer raw in _raw)
        {
            raw.Close();
        }

        Log($"=== RESULT: {(_failed ? "FAIL" : "PASS")} ===");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private RawPeer Raw()
    {
        var raw = new RawPeer(_port);
        _raw.Add(raw);
        return raw;
    }

    private async Task<NetSession> NewIntruder()
    {
        var holder = new Node { Name = $"SecIntruder{++_intruders}" };
        GetTree().Root.AddChild(holder);
        GetTree().SetMultiplayer(new SceneMultiplayer(), holder.GetPath());
        var intruder = new NetSession { Name = "NetSession" };
        holder.AddChild(intruder);
        await Frames(1);
        return intruder;
    }

    private async Task DropIntruder(NetSession intruder)
    {
        Node holder = intruder.GetParent();
        intruder.Leave();
        GetTree().SetMultiplayer(null, holder.GetPath());
        holder.QueueFree();
        await Frames(2);
    }

    private async Task<bool> HostAgain(NetSession session, Profile profile)
    {
        foreach (RawPeer raw in _raw)
        {
            raw.Close();
        }

        _raw.Clear();
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        Error hosted = Error.Failed;
        for (int attempt = 0; attempt < 5 && hosted != Error.Ok; attempt++)
        {
            _port = 8200 + rng.RandiRange(0, 600);
            hosted = session.Host(_port, profile.Active, JoinCode.TestCode, openRouterPort: false);
        }

        await Frames(2);
        return hosted == Error.Ok;
    }

    private async Task Scenario()
    {
        NetSession? session = NetSession.Get(this);
        Profile? profile = SetUpProfile("SecHost", "warrior");
        if (session is null || profile is null)
        {
            Check("AUTOLOADS", false, "NetSession or Profile autoload missing");
            return;
        }

        UnitChecks(profile);

        if (!await HostAgain(session, profile))
        {
            Check("HOST-STARTS", false, "could not host");
            return;
        }

        await SlotChecks(session, profile);
        await HostAgain(session, profile);
        await AuthPayloadChecks(session, profile);
        await HostAgain(session, profile);
        await AuthenticatedAbuseChecks(session, profile);
        await HostAgain(session, profile);
        await ChurnCheck(session, profile);
        await HostAgain(session, profile);
        await HostileHostCheck(session, profile);
        session.Leave();
    }

    // --- logic -----------------------------------------------------------------------------

    private void UnitChecks(Profile profile)
    {
        // IPv6: one home connection owns a /64; counting per address would be free to dodge.
        var limiter = new AttemptLimiter();
        for (int i = 1; i <= NetLimits.FailedAttempts; i++)
        {
            limiter.Fail($"2001:db8:1:2::{i:x}", 10);
        }

        Check("LIMITER-IPV6-PER-64", limiter.IsBlocked("2001:db8:1:2::ffff", 11) && !limiter.IsBlocked("2001:db8:1:3::1", 11)
                && AttemptLimiter.Key("::ffff:1.2.3.4") == "1.2.3.4",
            "five failures from five addresses of one /64 block the whole /64; another /64 and v4-mapped keys behave");

        // Rotating addresses must neither reset a block nor grow memory without bound.
        var rotate = new AttemptLimiter();
        for (int i = 0; i < NetLimits.FailedAttempts; i++)
        {
            rotate.Fail("9.9.9.9", 100);
        }

        for (int i = 0; i < 5000; i++)
        {
            rotate.Fail($"10.{i / 65536 % 256}.{i / 256 % 256}.{i % 256}", 100 + i * 0.001);
        }

        Check("LIMITER-ROTATION", rotate.IsBlocked("9.9.9.9", 110) && rotate.Tracked <= 2 * AttemptLimiter.MaxTracked && rotate.GloballyBlocked(110)
                && !rotate.GloballyBlocked(100 + 5 + NetLimits.FailedWindowSeconds + 1),
            $"after 5000 one-shot addresses: 9.9.9.9 still blocked, {rotate.Tracked} entries tracked (cap {2 * AttemptLimiter.MaxTracked}), global ceiling hit and drains after a minute");

        // A poisoned lobby list from a host: capped, clamped, bad characters dropped.
        string valid = Profile.SerializeCharacter(profile.Active);
        var junk = new List<object>();
        for (int i = 0; i < 50; i++)
        {
            junk.Add(new { PeerId = 1000 + (i % 40), Name = new string('n', 5000), ClassName = "x", Level = 99999, Character = i == 0 ? valid : "{\"Level\":999}", ViewWidth = 1e30, ViewHeight = -5 });
        }

        List<LobbyMember>? lobby = NetSession.SanitizeLobby(JsonSerializer.Serialize(junk));
        Check("CLIENT-LOBBY-SANITISED", lobby is { Count: PartyRules.MaxPlayers } && lobby.All(m => m.Name.Length <= NetLimits.NameChars && m.ViewWidth <= 7680f && m.ViewHeight >= 240f && m.Level <= 999)
                && lobby[0].Character == valid && lobby.Skip(1).All(m => m.Character == "") && NetSession.SanitizeLobby("[[[[") is null && NetSession.SanitizeLobby("null") is null,
            $"50 members → {lobby?.Count}; names cut, views clamped, invalid characters dropped, junk JSON ignored");

        // Gear from a host: checked like a character's items.
        EquipmentItemSample(out string goodItem);
        var bad = JsonNode.Parse(goodItem)!;
        bad["BaseStats"] = new JsonArray(new JsonArray(9999, 0, 5, 0));
        var badSlot = JsonNode.Parse(goodItem)!;
        badSlot["Slot"] = "999";
        Check("ITEM-VALIDATION", Profile.ValidateItemJson(goodItem) is null && Profile.ValidateItemJson(bad.ToJsonString()) is not null
                && Profile.ValidateItemJson(badSlot.ToJsonString()) is not null && Profile.ValidateItemJson("{") is not null && Profile.ValidateItemJson(new string('[', 5000)) is not null,
            $"a rolled item passes; stat id 9999, slot '999', broken and deeply nested JSON are refused");

        // Characters: NaN can't even be written in JSON; out-of-range enums are refused.
        var character = JsonNode.Parse(valid)!;
        string nanJson = valid.Replace("\"Experience\":", "\"Experience\":NaN,\"X\":");
        var withBadItem = JsonNode.Parse(valid)!;
        withBadItem["Items"] = new JsonArray(JsonNode.Parse(bad.ToJsonString()));
        string deep = "{\"Gems\":" + new string('[', 200) + new string(']', 200) + "}";
        Check("CHARACTER-HOSTILE-SHAPES", Profile.ValidateCharacterJson(nanJson, out _, out _) is not null
                && Profile.ValidateCharacterJson(withBadItem.ToJsonString(), out _, out _) is not null
                && Profile.ValidateCharacterJson(deep, out _, out _) is not null
                && Profile.ValidateCharacterJson(character.ToJsonString(), out _, out _) is null,
            "NaN literal, unknown stat id in gear and 200-deep nesting refused; the real character passes");

        // The save: a NaN anywhere used to truncate profile.json to nothing.
        var scratch = new Profile { Persist = true, FilePath = "user://test_results/security_profile_atomic.json" };
        scratch.CreateTestCharacter("Atomic", "rogue");
        scratch.Save();
        string before = ReadFile(scratch.FilePath);
        scratch.Active.Experience = float.NaN;
        scratch.Active.BestSurvivalTime = float.PositiveInfinity;
        scratch.Save();
        string after = ReadFile(scratch.FilePath);
        scratch.Free();
        Check("PROFILE-SAVE-NEVER-TRUNCATES", before.Length > 100 && after == before && !FileAccess.FileExists(scratch.FilePath + ".tmp"),
            $"a save with NaN experience is refused and the {before.Length}-byte file on disk is unchanged");
    }

    private static void EquipmentItemSample(out string json)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = 7;
        Items.EquipmentItem item = Items.ItemFactory.RollRandom(20, Items.DropSource.RiftReward, 2f, rng);
        json = Profile.SerializeItem(item);
        item.Free();
    }

    // --- before the code: slots and the auth step --------------------------------------------

    private async Task SlotChecks(NetSession session, Profile profile)
    {
        // Three strangers hold connections without ever sending a code. With the old
        // ENet cap (3 clients) a friend could no longer even connect.
        var holders = new List<RawPeer> { Raw(), Raw(), Raw() };
        await WaitFor(() => holders.All(h => h.Connected), 5f);
        NetSession friend = await NewIntruder();
        friend.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
        bool joined = await WaitFor(() => friend.Joined, 8f);
        Check("SLOTS-HELD-FRIEND-STILL-JOINS", holders.All(h => h.Connected) && joined && session.Members.Count == 2,
            $"3 code-less connections held; friend joined={joined}, lobby={session.Members.Count}");
        await DropIntruder(friend);

        // A fourth waits; a fifth from the same address is ignored outright.
        int log = session.SecurityLog.Count;
        holders.Add(Raw());
        await WaitFor(() => holders[3].Connected, 5f);
        RawPeer fifth = Raw();
        await WaitFor(() => fifth.Gone || session.SecurityLog.Skip(log).Any(l => l.Contains("waiting for their code")), 5f);
        Check("PENDING-PER-ADDRESS-CAP", session.SecurityLog.Skip(log).Any(l => l.Contains($"already {NetLimits.PendingPerAddress} connections waiting")),
            $"host log: {session.SecurityLog.LastOrDefault()}");

        // Holding without a code ends in a timeout that counts as a failed attempt.
        log = session.SecurityLog.Count;
        bool dropped = await WaitFor(() => holders.All(h => h.Gone), (float)NetLimits.AuthTimeout + 4f);
        int timeouts = session.SecurityLog.Skip(log).Count(l => l.Contains("no join code within"));
        Check("NO-CODE-TIMEOUT-COUNTS", dropped && timeouts >= 4,
            $"all holders dropped={dropped} after {NetLimits.AuthTimeout} s; {timeouts} logged as failed attempts");

        // Five failures from 127.0.0.1 now (4 timeouts + this wrong code) → blocked.
        NetSession wrong = await NewIntruder();
        wrong.Join("127.0.0.1", _port, profile.Active, "ZZZZZZ");
        await WaitFor(() => !wrong.Active, 8f);
        await DropIntruder(wrong);
        NetSession late = await NewIntruder();
        late.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
        await WaitFor(() => !late.Active || late.Joined, 8f);
        Check("SLOT-HOLDER-ENDS-BLOCKED", !late.Joined && session.SecurityLog.Any(l => l.StartsWith("ignored") && l.Contains("too many wrong codes")),
            $"even the right code is ignored from a blocked address: joined={late.Joined}, '{late.Status}'");
        await DropIntruder(late);
    }

    private async Task AuthPayloadChecks(NetSession session, Profile profile)
    {
        // Oversized "codes": the engine reassembles each, then the host refuses it by size.
        var sizes = new List<string>();
        foreach (int size in new[] { 65, 64 * 1024, 256 * 1024, 1024 * 1024 })
        {
            int log0 = session.SecurityLog.Count;
            RawPeer big = Raw();
            await WaitFor(() => big.Connected, 5f);
            ulong start0 = Time.GetTicksMsec();
            big.SendAuth(new byte[size]);
            // Either refused by size once it arrives, or (ENet paces a big reliable
            // packet over many round trips) dropped by the auth timeout first.
            bool refused0 = await WaitFor(() => session.SecurityLog.Skip(log0).Any(l => l.Contains("not an ARPG co-op client") || l.Contains("no join code")), (float)NetLimits.AuthTimeout + 3f);
            string how = session.SecurityLog.Skip(log0).Any(l => l.Contains("not an ARPG co-op client")) ? "refused by size" : refused0 ? "dropped by the auth timeout before it arrived" : "still connected";
            sizes.Add($"{size} B: {how} after {Time.GetTicksMsec() - start0} ms");
            big.Close();
            await HostAgain(session, profile); // those count as failures from 127.0.0.1
        }

        Check("OVERSIZED-AUTH-REFUSED", sizes.All(x => !x.Contains("still connected")) && sizes[0].Contains("by size") && sizes[1].Contains("by size"), string.Join("; ", sizes));
        int log;
        ulong start;

        // Pre-auth junk: 5000 reliable packets that aren't auth messages. The engine
        // drops each one (with an error line); the host must keep serving.
        RawPeer junk = Raw();
        await WaitFor(() => junk.Connected, 5f);
        byte[] packet = { 0, 1, 2, 3, 4, 5, 6, 7 };
        start = Time.GetTicksMsec();
        for (int i = 0; i < 5000; i++)
        {
            junk.Send(packet);
        }

        await Seconds(1.5f);
        ulong frameStart = Time.GetTicksMsec();
        await Frames(5);
        ulong fiveFrames = Time.GetTicksMsec() - frameStart;
        NetSession friend = await NewIntruder();
        friend.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
        bool joined = await WaitFor(() => friend.Joined, 8f);
        Check("PRE-AUTH-FLOOD-SURVIVED", joined && session.Members.Count == 2 && session.IsHost,
            $"5000 junk packets from a code-less peer; host still admits a friend (joined={joined}), 5 frames took {fiveFrames} ms");
        await DropIntruder(friend);

        // Auth spam after the answer: one code per connection, then ignored.
        RawPeer spam = Raw();
        await WaitFor(() => spam.Connected, 5f);
        log = session.SecurityLog.Count;
        for (int i = 0; i < 50; i++)
        {
            spam.SendAuth(new byte[] { 0x41, 0x52, 0x50, 0x47, 2, 0, (byte)'Z', (byte)'Z', (byte)'Z', (byte)'Z', (byte)'Z', (byte)'Z' });
        }

        await WaitFor(() => spam.Gone, 6f);
        int answered = session.SecurityLog.Skip(log).Count(l => l.Contains("Wrong join code"));
        Check("AUTH-ONE-GUESS-PER-CONNECTION", answered == 1 && spam.Gone, $"50 codes on one connection → {answered} judged, connection closed={spam.Gone}");
    }

    // --- with the code: a broken or hostile friend --------------------------------------------

    private async Task AuthenticatedAbuseChecks(NetSession session, Profile profile)
    {
        // The code, but no hello: run traffic is ignored, the slot freed by the hello timeout.
        NetSession silent = await NewIntruder();
        silent.TestSkipHello = true;
        silent.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
        await WaitFor(() => silent.Status.Contains("joining the party") && silent.Active, 6f);
        await Frames(10);
        for (int i = 0; i < 50; i++)
        {
            silent.SendInput(new byte[14]);
            silent.SendClientEvents(new byte[] { RiftProtocol.ClientReady, RiftProtocol.ClientPick, 0, 0 });
        }

        // Host-only RPCs from a client: the engine refuses them (authority mode).
        silent.RpcId(1, "RpcStartRun");
        silent.RpcId(1, "RpcLobby", "[]");
        silent.RpcId(1, "RpcRejected", "go away");
        silent.RpcId(1, "RpcSnapshot", new byte[64]);
        await Seconds(1f);
        Check("NO-HELLO-TRAFFIC-IGNORED", session.Members.Count == 1 && !session.InRun && session.IsHost,
            $"input, events and host-only RPCs before a hello: lobby={session.Members.Count}, host in run={session.InRun}, still hosting={session.IsHost}");
        bool timedOut = await WaitFor(() => !silent.Active, (float)NetSession.HelloTimeout + 3f);
        Check("NO-HELLO-TIMEOUT", timedOut, $"a peer with the code that never says hello is dropped after {NetSession.HelloTimeout} s: gone={timedOut}");
        await DropIntruder(silent);

        // Malformed hellos: each refused with a reason, never listed, host unharmed.
        string valid = Profile.SerializeCharacter(profile.Active);
        var withBadItem = JsonNode.Parse(valid)!;
        withBadItem["Items"] = new JsonArray(JsonNode.Parse("{\"Base\":\"x\",\"BaseStats\":[[9999,0,1,0]],\"Affixes\":[]}"));
        string[] hellos =
        {
            "not json at all",
            "{\"Character\":" + JsonSerializer.Serialize(valid) + ",\"ViewWidth\":1e39}",
            "{\"Character\":" + JsonSerializer.Serialize(withBadItem.ToJsonString()) + "}",
            "{\"Character\":null}",
            new string('[', 10000),
        };
        int refused = 0;
        foreach (string hello in hellos)
        {
            NetSession bad = await NewIntruder();
            bad.HelloJsonOverride = hello;
            bad.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
            await WaitFor(() => !bad.Active, 8f);
            refused += !bad.Active && session.Members.Count == 1 ? 1 : 0;
            await DropIntruder(bad);
        }

        Check("MALFORMED-HELLOS-REFUSED", refused == hellos.Length && session.IsHost,
            $"{refused}/{hellos.Length} refused (junk, infinite window, unknown stat id in gear, null character, 10 000-deep nesting)");
    }

    private async Task ChurnCheck(NetSession session, Profile profile)
    {
        // Connect and leave 20 times (half with the code, half without): nothing left behind.
        for (int i = 0; i < 10; i++)
        {
            NetSession friend = await NewIntruder();
            friend.Join("127.0.0.1", _port, profile.Active, JoinCode.TestCode);
            await WaitFor(() => friend.Joined, 6f);
            await DropIntruder(friend);
            RawPeer raw = Raw();
            await WaitFor(() => raw.Connected, 3f);
            raw.Close();
        }

        bool settled = await WaitFor(() => session.Members.Count == 1 && session.TrackedPeers == 0, 8f);
        Check("CONNECTION-CHURN-CLEAN", settled, $"after 20 connect/leave cycles: lobby={session.Members.Count}, per-peer state kept for {session.TrackedPeers} peers");
    }

    // --- a hostile host -------------------------------------------------------------------------

    private async Task HostileHostCheck(NetSession session, Profile profile)
    {
        if (FileAccess.FileExists(ClientLogPath))
        {
            DirAccess.RemoveAbsolute(ClientLogPath);
        }

        string[] args =
        {
            "--headless", "--path", ProjectSettings.GlobalizePath("res://"),
            "res://scenes/tests/SecuritySmokeTest.tscn", "--quit-after", "20000",
            "--", "--sec-role=client", $"--sec-port={_port}",
        };
        _pid = OS.CreateProcess(OS.GetExecutablePath(), args);
        bool joined = await WaitFor(() => session.Members.Count == 2, 40f);
        if (!joined)
        {
            Check("HOSTILE-HOST", false, "the client never joined");
            return;
        }

        long client = session.Members[1].PeerId;
        await Seconds(0.5f);

        // A poisoned lobby: 50 members, absurd names and windows, broken characters.
        var lobby = new List<object> { new { PeerId = 1, Name = "Host", ClassName = "x", Level = 1, Character = "{", ViewWidth = 1280, ViewHeight = 720 } };
        lobby.Add(new { PeerId = client, Name = session.Members[1].Name, ClassName = "x", Level = 1, Character = session.Members[1].Character, ViewWidth = 1280, ViewHeight = 720 });
        for (int i = 0; i < 48; i++)
        {
            lobby.Add(new { PeerId = 5000 + i, Name = new string('w', 3000), ClassName = "y", Level = -4, Character = "{\"Level\":999}", ViewWidth = 1e30, ViewHeight = -1 });
        }

        var watcher = new ReadyWatcher();
        session.Run = watcher;
        session.Rpc("RpcLobby", JsonSerializer.Serialize(lobby));
        await Seconds(0.5f);
        session.Rpc("RpcStartRun");
        bool ready = await WaitFor(() => watcher.Ready, 40f);
        if (!ready)
        {
            Check("HOSTILE-HOST", false, "the client never reported ready in the run");
            session.Run = null;
            return;
        }

        await Seconds(0.5f);
        var w = new NetWriter(1 << 20);

        // Strings: a menu scene, a test scene, the save file, an oversized string, a real monster.
        w.F32(0f);
        string[] strings = { "res://scenes/menus/MainMenu.tscn", "res://scenes/tests/EndgameSmokeTest.tscn", "user://profile.json", new string('x', 5000), "res://scenes/enemies/Slime.tscn", "res://scenes/enemies/../menus/MainMenu.tscn" };
        for (int i = 0; i < strings.Length; i++)
        {
            w.U8(RiftProtocol.StringDef);
            w.U16((ushort)(i + 1));
            w.Str(strings[i]);
        }

        // Monster spawns of non-monster scenes (refused) and one real one with NaN/infinite looks.
        for (int i = 1; i <= strings.Length; i++)
        {
            w.U8(RiftProtocol.EnemySpawn);
            w.U16((ushort)i);
            w.U16((ushort)i);
            w.Pos(new Vector2(100f, 100f));
            w.U8(i == 5 ? (byte)0 : (byte)200);
            w.F32(float.NaN);
            w.Str(new string('m', 4000));
            w.U8(0);
            w.F32(float.PositiveInfinity);
            w.U8(0);
        }

        w.U8(RiftProtocol.EnemySpawn); // the real monster, rarity in range this time
        w.U16(50);
        w.U16(5);
        w.Pos(new Vector2(120f, 100f));
        w.U8(0);
        w.F32(float.NaN);
        w.Str("Slime");
        w.U8(0);
        w.F32(float.PositiveInfinity);
        w.U8(0);

        // Remove the client's own player; credit NaN/infinite/negative gold and experience.
        w.U8(RiftProtocol.PlayerLeft);
        w.I32((int)client);
        foreach (float v in new[] { float.NaN, float.NegativeInfinity, -1e30f, float.PositiveInfinity, 1e30f })
        {
            w.U8(RiftProtocol.Collected);
            w.U8((byte)PickupKind.Gold);
            w.F32(v);
            w.U16(0);
            w.I32(-1);
            w.U8(RiftProtocol.CharacterXp);
            w.F32(v);
        }

        w.U8(RiftProtocol.Progress);
        w.U16(65535);
        w.F32(float.NaN);
        w.U8(0);

        // Gear: broken JSON and an unknown stat id.
        foreach (string item in new[] { "{", "{\"Base\":\"x\",\"BaseStats\":[[9999,0,1,0]],\"Affixes\":[]}" })
        {
            w.U8(RiftProtocol.ItemDrop);
            w.Pos(Vector2.Zero);
            w.Str(item);
        }

        w.U8(RiftProtocol.Banner);
        w.Str(new string('B', 60000));
        w.Str("sub");
        w.Colour(Colors.White);
        session.SendHostEvents(client, w.ToArray());

        // 20 000 effects that never end, stamped with a NaN clock.
        w.Clear();
        w.F32(float.NaN);
        for (int i = 0; i < 20000; i++)
        {
            w.U8(RiftProtocol.Burst);
            w.Pos(new Vector2(i % 500, i / 500));
            w.F32(float.NaN);
            w.F32(float.PositiveInfinity);
            w.Colour(Colors.Red);
            w.F32(float.PositiveInfinity);
            w.U8(0);
        }

        session.SendHostEvents(client, w.ToArray());
        await Seconds(1.5f);

        // Snapshots: a NaN clock (ignored), then a run clock of infinity and negative kills.
        // (then a finite but absurd 1e10 s, which must be clamped, not turned into 10^8 orbs).
        foreach ((float hostTime, float elapsed) in new[] { (float.NaN, 5f), (1f, float.PositiveInfinity), (2f, 1e10f) })
        {
            w.Clear();
            w.U32(1);
            w.F32(hostTime);
            w.U8(0);
            w.F32(elapsed);
            w.I32(-5);
            w.U16(0);
            w.U16(0);
            w.U8(0);
            w.U8(0);
            w.U16(0);
            w.U8(0);
            w.Bytes(new byte[6]);
            w.U16(0);
            session.SendSnapshot(client, w.ToArray());
            await Frames(3);
        }

        await Seconds(0.5f);
        w.Clear();
        w.F32(0f);
        w.U8(RiftProtocol.RunEnded);
        w.Bool(false);
        session.SendHostEvents(client, w.ToArray());

        bool reported = await WaitFor(() => ReadFile(ClientLogPath).Contains("HOSTILE-HOST-CLIENT"), 40f);
        string line = ReadFile(ClientLogPath).Split('\n').FirstOrDefault(l => l.Contains("HOSTILE-HOST-CLIENT")) ?? "(no report)";
        Check("HOSTILE-HOST-CLIENT-SURVIVES", reported && line.Contains(": PASS"), line);
        session.Run = null;
    }

    private static string ReadFile(string path)
    {
        using FileAccess? f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return f?.GetAsText() ?? "";
    }
}

/// <summary>The client of the hostile-host check: joins, lives through whatever
/// the host sends, then reports whether it and its saved profile are intact.</summary>
public partial class SecurityTestClient : CoopTestDriver
{
    public int Port { get; set; }

    protected override string LogPath => "user://test_results/SecuritySmokeTest_client.txt";

    protected override string Tag => "SEC-CLIENT";

    protected override async Task Main()
    {
        await Frames(2);
        NetSession? session = NetSession.Get(this);
        Profile? profile = SetUpProfile("Victim", "rogue");
        if (session is null || profile is null)
        {
            Log("HOSTILE-HOST-CLIENT: FAIL — no autoloads");
            return;
        }

        // A real save, in a scratch file: the host must not be able to corrupt it.
        profile.FilePath = SecurityTestHost.ClientProfilePath;
        profile.Persist = true;
        profile.Save();
        session.Join("127.0.0.1", Port, profile.Active, JoinCode.TestCode);
        bool inRun = await WaitFor(() => Run?.NetClient is not null, 60f);
        if (!inRun)
        {
            Log($"HOSTILE-HOST-CLIENT: FAIL — never reached the run ('{session.Status}')");
            return;
        }

        RunController run = Run!;
        bool ended = await WaitFor(() => run.State.Ended, 45f);
        await Seconds(1f);
        int children = run.GetChildCount();
        bool localOk = IsInstanceValid(run.Player) && run.Player.IsInsideTree() && !run.Player.IsQueuedForDeletion();
        string saved = "";
        using (FileAccess? f = FileAccess.Open(profile.FilePath, FileAccess.ModeFlags.Read))
        {
            saved = f?.GetAsText() ?? "";
        }

        bool saveOk = false;
        string saveInfo = "";
        try
        {
            JsonNode node = JsonNode.Parse(saved)!;
            JsonNode c = node["Characters"]![node["Active"]!.GetValue<int>()]!;
            float xp = c["Experience"]!.GetValue<float>();
            float best = c["BestSurvivalTime"]!.GetValue<float>();
            long gold = c["Gold"]!.GetValue<long>();
            string name = c["Name"]!.GetValue<string>();
            saveOk = name == "Victim" && float.IsFinite(xp) && xp >= 0f && float.IsFinite(best) && best <= 86_400f && gold >= 0;
            saveInfo = $"{name}: xp={xp} best={best} gold={gold}";
        }
        catch (Exception e)
        {
            saveInfo = $"unreadable: {e.Message}";
        }

        RiftClient? net = run.NetClient;
        bool ok = ended && localOk && children <= RiftClient.MaxSceneChildren + 200 && float.IsFinite(run.State.Elapsed) && saveOk
                  && session.Members.Count <= PartyRules.MaxPlayers && (net?.Refused ?? 0) > 0;
        Log($"HOSTILE-HOST-CLIENT: {(ok ? "PASS" : "FAIL")} — run ended={ended}, own player intact={localOk}, scene children={children} (cap {RiftClient.MaxSceneChildren}), "
            + $"elapsed={run.State.Elapsed}, snapshots={net?.Snapshots}, lobby={session.Members.Count}, refused events={net?.Refused}, monsters={net?.EnemyCount}, saved profile: {saveInfo}");
        GetTree().Quit();
    }
}
