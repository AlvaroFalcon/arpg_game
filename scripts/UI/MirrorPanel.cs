#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.World;

namespace ArpgGame.UI;

/// <summary>
/// The Mirror of Realities — docs/design/endgame.md -> "The Mirror". Pick a
/// shard tier (tier 1 is always free), optionally empower it for gold (rolls
/// one to three shard modifiers: harder, more loot), and step through. The
/// Survival Rift (the original survivor mode) is offered here too.
/// </summary>
public partial class MirrorPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    /// <summary>Called with the scene to travel to (the hideout saves first).</summary>
    public Action<string>? Travel { get; set; }

    protected override string Title => "Mirror of Realities";

    private GridContainer _tiers = null!;
    private Label _tierTitle = null!;
    private Label _tierInfo = null!;
    private Label _modsLabel = null!;
    private Button _empower = null!;
    private Button _open = null!;
    private Label _gold = null!;
    private int _selected = 1;
    private List<string> _mods = new();
    private readonly RandomNumberGenerator _rng = new();

    private CharacterData? Character => Profile.Get(this)?.Active;

    private long Gold => Player.Wallet.GetBalance(Profile.GoldId);

    protected override void Build(VBoxContainer body)
    {
        _rng.Randomize();
        body.AddChild(UiTheme.Label("Each Mirror Shard opens one Altered Reality of its tier. Bosses drop shards of the same tier or higher.", 16, UiTheme.TextDim));
        HBoxContainer columns = UiTheme.HBox(body, 24);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        VBoxContainer left = UiTheme.VBox(columns, 6);
        left.AddChild(UiTheme.Label("Shards", 20, UiTheme.Text));
        _tiers = new GridContainer { Columns = 4 };
        _tiers.AddThemeConstantOverride("h_separation", 6);
        _tiers.AddThemeConstantOverride("v_separation", 6);
        left.AddChild(_tiers);

        VBoxContainer right = UiTheme.VBox(columns, 10);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _tierTitle = UiTheme.Label("", 26, UiTheme.Gold);
        right.AddChild(_tierTitle);
        _tierInfo = UiTheme.Label("", 17, UiTheme.Text);
        _tierInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_tierInfo);
        _modsLabel = UiTheme.Label("", 16, UiTheme.Ember);
        _modsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_modsLabel);
        _gold = UiTheme.Label("", 16, UiTheme.Gold);
        right.AddChild(_gold);
        _empower = UiTheme.Button("Empower", Empower, 18, 320f);
        right.AddChild(_empower);
        _open = UiTheme.Button("Step through the Mirror", OpenReality, 22, 320f);
        right.AddChild(_open);
        right.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        right.AddChild(UiTheme.Label("Survival Rift: the endless arena — gear, gold and character experience.", 15, UiTheme.TextDim));
        right.AddChild(UiTheme.Button("Enter the Survival Rift", () => Leave(Scenes.Run), 18, 320f));
    }

    public override void Refresh()
    {
        CharacterData? c = Character;
        ClearChildren(_tiers);
        for (int tier = 1; tier <= RealityTiers.MaxTier; tier++)
        {
            int t = tier;
            int owned = c?.ShardCount(tier) ?? 0;
            string count = tier == 1 ? "∞" : owned.ToString();
            Button b = UiTheme.Button($"T{tier}\n{count}", () => Select(t), 15, 74f);
            b.CustomMinimumSize = new Vector2(74f, 58f);
            b.Disabled = tier > 1 && owned <= 0;
            b.AddThemeColorOverride("font_color", RealityTiers.ShardColor(tier));
            if (tier == _selected)
            {
                b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.22f, 0.17f, 0.3f), RealityTiers.ShardColor(tier), 2));
            }

            _tiers.AddChild(b);
        }

        int t2 = _selected;
        int portals = RealityTiers.Portals(t2);
        _tierTitle.Text = $"Tier {t2} Mirror Shard";
        _tierTitle.AddThemeColorOverride("font_color", RealityTiers.ShardColor(t2));
        _tierInfo.Text =
            $"Monster level {RealityTiers.MonsterLevel(t2)}  ·  monster life ×{RealityTiers.LifeMultiplier(t2):0.0}  ·  damage ×{RealityTiers.DamageMultiplier(t2):0.0}\n"
            + $"{portals} portal{(portals == 1 ? "" : "s")} (deaths allowed)  ·  layout: random of {LayoutCatalog.Layouts.Count}\n"
            + $"Magic packs {RealityTiers.MagicPackChance(t2) * 100f:0}%  ·  rare packs {RealityTiers.RarePackChance(t2) * 100f:0}%  ·  unique-rare chance {RealityTiers.UniqueChance(t2) * 100f:0}%\n"
            + (c is null ? "" : $"Highest tier cleared: {c.HighestTierCleared}  ·  owned: {(t2 == 1 ? "always free" : c.ShardCount(t2).ToString())}");
        (float q, float r) = RealityLaunch.Bonuses(_mods);
        _modsLabel.Text = _mods.Count == 0
            ? "Not empowered."
            : "Empowered:\n  " + string.Join("\n  ", _mods.Select(m => RealityModifier.Find(m)?.Text ?? m)) + $"\n  → +{q * 100f:0}% item quantity, +{r * 100f:0}% item rarity";
        long cost = RealityLaunch.EmpowerCost(t2);
        _gold.Text = $"Gold: {Gold}";
        _empower.Text = _mods.Count == 0 ? $"Empower ({cost} gold)" : $"Re-roll modifiers ({cost} gold)";
        _empower.Disabled = Gold < cost;
        _open.Disabled = c is not null && t2 > 1 && c.ShardCount(t2) <= 0;
    }

    public void Select(int tier)
    {
        if (tier != _selected)
        {
            _mods = new List<string>();
        }

        _selected = tier;
        Refresh();
    }

    public void Empower()
    {
        if (!Player.Wallet.TrySpend(Profile.GoldId, RealityLaunch.EmpowerCost(_selected)))
        {
            return;
        }

        _mods = RealityLaunch.RollModifiers(_selected, _rng);
        Sfx.Play(this, "levelup", -6f);
        Refresh();
    }

    private void OpenReality()
    {
        if (Character is { } c && !c.TrySpendShard(_selected))
        {
            return;
        }

        RealityLaunch.Tier = _selected;
        RealityLaunch.LayoutId = null;
        RealityLaunch.Modifiers = new List<string>(_mods);
        _mods = new List<string>();
        Leave(Scenes.Reality);
    }

    private void Leave(string scene)
    {
        Close();
        Travel?.Invoke(scene);
    }
}
