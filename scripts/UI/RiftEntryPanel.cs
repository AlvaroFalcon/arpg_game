#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Run;

namespace ArpgGame.UI;

/// <summary>
/// The Survival Rift's entry panel — docs/design/survival-roadmap.md -> M4: the tier (1 up to one
/// above the highest cleared, account-wide) and up to three Fear cards, each harder in one way
/// and +20 % to every reward. Opened by the hideout portal and the Mirror's "Enter the Survival
/// Rift". "Enter" hands a <see cref="Net.RiftSpec"/> to <see cref="Enter"/> (solo: straight in;
/// a party: the proposal).
/// </summary>
public partial class RiftEntryPanel : ModalPanel
{
    public Action<Net.RiftSpec>? Enter { get; set; }

    protected override string Title => "The Survival Rift";

    protected override Vector2 FrameSize => new(1180f, 640f);

    private GridContainer _tiers = null!;
    private VBoxContainer _fears = null!;
    private Label _summary = null!;
    private Label _tierInfo = null!;
    private VBoxContainer _challenges = null!;
    private Label _challengeCount = null!;

    /// <summary>The tier chosen (tests, captures).</summary>
    public int Tier { get; private set; } = 1;

    /// <summary>The Fear cards chosen (tests, captures).</summary>
    public List<string> Fears { get; } = new();

    private int Unlocked => Profile.Get(this)?.RiftTierUnlocked ?? 1;

    protected override void Build(VBoxContainer body)
    {
        body.AddChild(UiTheme.Label("Survive the waves, slay the Ember Colossus, then stay as long as you dare. A harder Rift pays more.", 16, UiTheme.TextDim));
        HBoxContainer columns = UiTheme.HBox(body, 24);
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        VBoxContainer left = UiTheme.VBox(columns, 8);
        left.AddChild(UiTheme.Label("Tier", 20, UiTheme.Text));
        _tiers = new GridContainer { Columns = 5 };
        _tiers.AddThemeConstantOverride("h_separation", 6);
        _tiers.AddThemeConstantOverride("v_separation", 6);
        left.AddChild(_tiers);
        _tierInfo = UiTheme.Label("", 15, UiTheme.TextDim);
        _tierInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tierInfo.CustomMinimumSize = new Vector2(330f, 0f);
        left.AddChild(_tierInfo);

        VBoxContainer right = UiTheme.VBox(columns, 6);
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.AddChild(UiTheme.Label($"Fear cards (up to {RiftDifficulty.MaxFears}, +{Mathf.RoundToInt(RiftDifficulty.FearReward * 100)}% rewards each)", 20, UiTheme.Text));
        _fears = UiTheme.VBox(right, 4);
        _summary = UiTheme.Label("", 16, UiTheme.Gold);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(_summary);

        // Rift challenges (docs/design/survival-roadmap.md -> M7).
        VBoxContainer third = UiTheme.VBox(columns, 4);
        third.CustomMinimumSize = new Vector2(330f, 0f);
        _challengeCount = UiTheme.Label("Challenges", 20, UiTheme.Text);
        third.AddChild(_challengeCount);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(330f, 420f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        third.AddChild(scroll);
        _challenges = UiTheme.VBox(scroll, 6);
        _challenges.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        body.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        HBoxContainer buttons = UiTheme.HBox(body, 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button enter = UiTheme.Button("Enter the Rift", Go, 22, 300f);
        enter.Name = "EnterRift";
        buttons.AddChild(enter);
    }

    public override void Refresh()
    {
        Tier = Mathf.Clamp(Tier, 1, Unlocked);
        foreach (Node c in _tiers.GetChildren())
        {
            c.QueueFree();
        }

        for (int t = 1; t <= RiftDifficulty.MaxTier; t++)
        {
            int tier = t;
            Button b = UiTheme.Button(t.ToString(), () => { Tier = tier; Refresh(); }, 18, 56f);
            b.Name = $"Tier{t}";
            b.Disabled = t > Unlocked;
            if (t == Tier)
            {
                b.AddThemeColorOverride("font_color", UiTheme.Gold);
                b.AddThemeColorOverride("font_disabled_color", UiTheme.Gold);
            }

            UiTheme.InstantTooltip(b, () => tier > Unlocked ? $"Slay the Ember Colossus on tier {tier - 1} to open it" : $"Tier {tier}");
            _tiers.AddChild(b);
        }

        foreach (Node c in _fears.GetChildren())
        {
            c.QueueFree();
        }

        foreach (FearDefinition fear in RiftDifficulty.AllFears)
        {
            FearDefinition f = fear;
            bool on = Fears.Contains(f.Id);
            Button b = UiTheme.Button($"{(on ? "[x]" : "[  ]")}  {Loc.T(f.Name)} — {Loc.T(f.Description)}", () => ToggleFear(f.Id), 15, 460f);
            b.Name = $"Fear_{f.Id}";
            b.Alignment = HorizontalAlignment.Left;
            b.Disabled = !on && Fears.Count >= RiftDifficulty.MaxFears;
            if (on)
            {
                b.AddThemeColorOverride("font_color", UiTheme.Ember);
            }

            _fears.AddChild(b);
        }

        RefreshChallenges();
        RiftDifficulty d = Preview();
        _tierInfo.Text = Loc.F("Tier {0}: monsters ×{1:0.0} life and ×{2:0.0} damage", Tier, d.Life, d.Damage)
            + (d.MagicChance > 0f ? Loc.F(", {0}% come as magic monsters", Mathf.RoundToInt(d.MagicChance * 100)) : "")
            + Loc.F(".\nItems +{0} item levels.\nHighest cleared: {1}.", d.ItemLevelBonus, Profile.Get(this)?.RiftTierCleared ?? 0);
        _summary.Text = $"Rewards: gold and orbs ×{d.Gold:0.00}, item rarity +{Mathf.RoundToInt(d.RarityBonus * 100)}%";
    }

    private void RefreshChallenges()
    {
        foreach (Node c in _challenges.GetChildren())
        {
            c.QueueFree();
        }

        _challenges.GetParent<Control>().GetParent<Control>().Visible = Features.RiftChallenges;
        Dictionary<string, int> progress = Profile.Get(this)?.RiftChallenges ?? new Dictionary<string, int>();
        int done = 0;
        foreach (ChallengeDef c in RiftChallenges.All)
        {
            bool complete = RiftChallenges.IsDone(progress, c);
            done += complete ? 1 : 0;
            string count = c.Count > 1 ? $"  ({RiftChallenges.ProgressOf(progress, c)}/{c.Count})" : "";
            Label title = UiTheme.Label((complete ? "✓ " : "") + Loc.T(c.Title) + count, 15, complete ? UiTheme.Experience : UiTheme.Text);
            title.Name = $"Challenge_{c.Id}";
            _challenges.AddChild(title);
            Label line = UiTheme.Label(Loc.F("{0}\nReward: {1}", Loc.T(c.Description), Loc.T(c.RewardText)), 12, UiTheme.TextDim);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(310f, 0f);
            _challenges.AddChild(line);
        }

        _challengeCount.Text = Loc.F("Challenges  {0}/{1}", done, RiftChallenges.All.Count);
    }

    private void ToggleFear(string id)
    {
        if (!Fears.Remove(id) && Fears.Count < RiftDifficulty.MaxFears)
        {
            Fears.Add(id);
        }

        Refresh();
    }

    /// <summary>Picks a tier and Fear cards (tests, captures).</summary>
    public void Choose(int tier, IEnumerable<string> fears)
    {
        Tier = tier;
        Fears.Clear();
        Fears.AddRange(fears.Where(id => RiftDifficulty.FindFear(id) is not null).Distinct().Take(RiftDifficulty.MaxFears));
        Refresh();
    }

    private RiftDifficulty Preview()
    {
        var d = new RiftDifficulty { Tier = Tier };
        d.Fears.AddRange(Fears.Select(RiftDifficulty.FindFear).Where(f => f is not null)!);
        return d;
    }

    /// <summary>The spec "Enter" sends.</summary>
    public Net.RiftSpec Spec => Net.RiftSpec.SurvivalRift(Tier, Fears);

    private void Go()
    {
        Net.RiftSpec spec = Spec;
        Close();
        Enter?.Invoke(spec);
    }
}
