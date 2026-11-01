#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Enemies;
using ArpgGame.Progression;
using ArpgGame.World;

namespace ArpgGame.Tests;

/// <summary>
/// The hidden playground (docs/design/playground.md), end to end: from the hideout,
/// Ctrl+Shift+P's entry builds a sandbox copy of the character with every skill at
/// level 20 and loads the playground (no packs, no boss); the spawner makes hordes and
/// bosses at the chosen tier; Escaldón and the Artificer are there; nothing drops; and
/// leaving restores the real character untouched. The test survives the scene changes by
/// moving itself under the root. Run:
///   ./scripts/run_headless.sh res://scenes/tests/PlaygroundTest.tscn --quit-after 6000
/// </summary>
public partial class PlaygroundTest : Node
{
    private bool _failed;
    private readonly List<string> _lines = new();

    public override void _Ready() => CallDeferred(nameof(Start));

    private void Start()
    {
        Window root = GetTree().Root;
        GetParent().RemoveChild(this);
        root.AddChild(this);
        _ = Run();
    }

    private void Check(string name, bool ok, string detail)
    {
        _failed |= !ok;
        string line = $"[PLAYGROUND] {name}: {(ok ? "PASS" : "FAIL")} — {detail}";
        _lines.Add(line);
        GD.Print(line);
    }

    private async Task Seconds(float s) => await ToSignal(GetTree().CreateTimer(s, processAlways: true), SceneTreeTimer.SignalName.Timeout);

    private async Task<T?> WaitScene<T>(float timeout) where T : Node
    {
        for (float t = 0f; t < timeout; t += 0.1f)
        {
            if (GetTree().CurrentScene is T found && found.IsNodeReady())
            {
                return found;
            }

            await Seconds(0.1f);
        }

        return null;
    }

    private async Task Run()
    {
        Profile? profile = Profile.Get(this);
        if (profile is null)
        {
            Check("PROFILE", false, "no Profile autoload");
            Finish();
            return;
        }

        profile.Persist = false; // never touch the real save from a test
        GetTree().ChangeSceneToFile(Scenes.Hideout);
        Hideout? hideout = await WaitScene<Hideout>(10f);
        await Seconds(0.5f);
        if (hideout is null)
        {
            Check("HIDEOUT", false, "the hideout did not load");
            Finish();
            return;
        }

        CharacterData real = profile.Active;
        string name = real.Name;
        int gems = real.Gems.Count;
        long gold = hideout.Player.Wallet.GetBalance(Profile.GoldId);
        int characters = profile.Characters.Count;

        hideout.EnterPlayground();
        RealityController? ground = await WaitScene<RealityController>(20f);
        await Seconds(1f);
        if (ground is null)
        {
            Check("ENTERS", false, "the playground did not load");
            Finish();
            return;
        }

        Playground? pg = ground.GetNodeOrNull<Playground>("Playground");
        CharacterData sandbox = profile.Active;
        bool allMax = UpgradeCatalog.Skills.All(s => sandbox.Gems.Any(g => g.Id == s.Id && !g.IsSupport && g.Level == GemData.MaxLevel));
        int monsters = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).Count;
        Check("ENTERS", ground.IsPlayground && pg is not null && sandbox.Name.EndsWith("(playground)") && allMax && monsters == 0 && !profile.Persist
                && ground.Player.Wallet.GetBalance(Profile.GoldId) >= Playground.SandboxGold,
            $"playground={ground.IsPlayground}; sandbox \"{sandbox.Name}\" with every skill at {GemData.MaxLevel}={allMax}; {monsters} monsters at the start; writes off={!profile.Persist}; gold {ground.Player.Wallet.GetBalance(Profile.GoldId):N0}");
        if (pg is null)
        {
            Finish();
            return;
        }

        bool npcs = ground.GetNodeOrNull("Escaldón") is not null && ground.GetNodeOrNull("theArtificer") is not null
            && ground.GetNodeOrNull<UI.SkillTreePanel>("SkillTreePanel") is not null && ground.GetNodeOrNull<UI.ArtificerPanel>("ArtificerPanel") is not null;
        Check("NPCS", npcs && pg.ConsoleOpen, $"Escaldón and the Artificer stand in the camp with their panels={npcs}; spawner open={pg.ConsoleOpen}");

        ground.SetPlaygroundTier(10);
        int horde = ground.SpawnPlaygroundHorde(12, null, null, ground.Player.GlobalPosition + new Vector2(260f, 0f)).Count;
        await Seconds(0.3f);
        var spawned = GetTree().GetNodesInGroup(Combat.CombatQuery.EnemyGroup).OfType<Enemy>().ToList();
        bool levelled = spawned.Count == 12 && spawned.All(e => e.MonsterLevel == RealityTiers.MonsterLevel(10) && !e.Dormant);
        Check("HORDE-AT-TIER", horde == 12 && levelled, $"{spawned.Count} monsters awake at monster level {spawned.FirstOrDefault()?.MonsterLevel} (tier 10 is {RealityTiers.MonsterLevel(10)})");

        pg.ClearMonsters();
        await Seconds(0.2f);
        ground.SetPlaygroundTier(3);
        BossEnemy low = ground.SpawnPlaygroundBoss(RealityController.AllBosses[0], ground.Player.GlobalPosition + new Vector2(300f, 0f));
        float lowLife = low.Stats.Life!.Max;
        low.QueueFree();
        ground.SetPlaygroundTier(18);
        BossEnemy high = ground.SpawnPlaygroundBoss(RealityController.AllBosses[0], ground.Player.GlobalPosition + new Vector2(300f, 0f));
        float highLife = high.Stats.Life!.Max;
        await Seconds(0.2f);
        high.Health.ApplyResolvedDamage(1e12f, ground.Player);
        await Seconds(1.5f);
        int pickups = GetTree().GetNodesInGroup(ArpgGame.Run.RunState.PickupGroup).Count;
        Check("BOSS-ANY-TIER", highLife > lowLife * 2f && high.Health.IsDead && pickups == 0 && !ground.Completed,
            $"{high.DisplayName}: {lowLife:N0} life at tier 3, {highLife:N0} at tier 18; killed with no loot ({pickups} pickups) and no exit portal");

        // Leaving throws the sandbox away.
        ground.Pause.OnLeave?.Invoke();
        Hideout? back = await WaitScene<Hideout>(20f);
        await Seconds(0.5f);
        CharacterData after = profile.Active;
        Check("LEAVES-CLEAN", back is not null && after.Name == name && after.Gems.Count == gems && profile.Characters.Count == characters
                && (back?.Player.Wallet.GetBalance(Profile.GoldId) ?? -1) == gold,
            $"back home as \"{after.Name}\" with {after.Gems.Count} gems (was {gems}), {profile.Characters.Count} characters (was {characters}), gold {back?.Player.Wallet.GetBalance(Profile.GoldId):N0} (was {gold:N0})");
        Finish();
    }

    private void Finish()
    {
        string verdict = _failed ? "[PLAYGROUND] === RESULT: FAIL ===" : "[PLAYGROUND] === RESULT: PASS ===";
        _lines.Add(verdict);
        GD.Print(verdict);
        DirAccess.MakeDirRecursiveAbsolute("user://test_results");
        using (var f = FileAccess.Open("user://test_results/PlaygroundTest.txt", FileAccess.ModeFlags.Write))
        {
            f?.StoreString(string.Join("\n", _lines) + "\n");
        }

        GetTree().Quit();
    }
}
