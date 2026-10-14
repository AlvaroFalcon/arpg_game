#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Progression;
using ArpgGame.Run;

namespace ArpgGame.UI;

/// <summary>
/// End of run — docs/design/menus.md -> "Results" and docs/design/run.md ->
/// "Rewards". Shows how the run went and offers a choice of three pieces of
/// gear rolled through the normal item pipeline; the one picked goes to the
/// persistent inventory. Better runs (more time, bosses killed, victory) roll
/// higher item levels and better rarities — the run feeds the character.
/// </summary>
public partial class ResultsScreen : CanvasLayer
{
    public Action? OnHideout { get; set; }
    public Action? OnMenu { get; set; }

    private Control _root = null!;
    private VBoxContainer _box = null!;
    private HBoxContainer _rewards = null!;
    private Label _rewardHint = null!;
    private int _claimed;
    private int _keep = 1;

    public override void _Ready()
    {
        Layer = 25;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        _root.Visible = false;
    }

    public void ShowResults(RunState state, RunBuild build, RunRewards? rewards = null)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        rewards ??= RunRewards.For(state.Elapsed, state.BossesKilled, state.Kills, state.Victory, rng);
        _keep = rewards.Keep;
        GetTree().Paused = true;
        _root.Visible = true;
        UiTheme.Dim(_root, 0.82f);
        CenterContainer center = UiTheme.Center(_root);
        var panel = new PanelContainer();
        center.AddChild(panel);
        _box = UiTheme.VBox(panel, 10);

        _box.AddChild(UiTheme.Label(state.Victory ? "VICTORY" : "YOU HAVE FALLEN", 48, state.Victory ? UiTheme.Gold : UiTheme.Life, HorizontalAlignment.Center));
        _box.AddChild(UiTheme.Label(
            $"Survived {UiTheme.FormatTime(state.Elapsed)}   ·   Level {state.Level}   ·   {state.Kills} kills   ·   {state.ElitesKilled} elites   ·   {state.BossesKilled} bosses   ·   +{state.GoldCollected} gold",
            18, UiTheme.Text, HorizontalAlignment.Center));
        Label summary = UiTheme.Label(LevelUpScreen.BuildSummary(build), 15, UiTheme.TextDim, HorizontalAlignment.Center);
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.CustomMinimumSize = new Vector2(900f, 0f);
        _box.AddChild(summary);

        _box.AddChild(CurrencyRow(rewards));
        if (state.Victory && state.Elapsed > 0f)
        {
            // A hint at why staying in the endless phase pays.
            _box.AddChild(UiTheme.Label("Every minute survived adds gold, orbs, item cards and items to keep.", 13, UiTheme.TextDim, HorizontalAlignment.Center));
        }

        _rewardHint = UiTheme.Label(KeepText(rewards.Keep), 24, UiTheme.Ember, HorizontalAlignment.Center);
        _box.AddChild(_rewardHint);
        _rewards = UiTheme.HBox(_box, 14);
        _rewards.Alignment = BoxContainer.AlignmentMode.Center;
        // Up to five cards: narrow them so they always fit the screen.
        float cardWidth = Mathf.Min(280f, (1180f - 14f * (rewards.Choices - 1)) / rewards.Choices);
        foreach (EquipmentItem item in RollRewards(rewards))
        {
            _rewards.AddChild(RewardCard(item, cardWidth));
        }

        HBoxContainer buttons = UiTheme.HBox(_box, 16);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        buttons.AddChild(UiTheme.Button("Return to Hideout", () => Leave(OnHideout)));
        buttons.AddChild(UiTheme.Button("Main Menu", () => Leave(OnMenu)));
        Core.Sfx.Play(this, state.Victory ? "victory" : "defeat");
    }

    private void Leave(Action? next)
    {
        GetTree().Paused = false;
        next?.Invoke();
    }

    private static string KeepText(int keep) => keep == 1 ? "Choose one item to keep" : $"Choose {keep} items to keep";

    /// <summary>"Rift reward: +gold" and an icon per orb earned (already in the wallet).</summary>
    private static Control CurrencyRow(RunRewards rewards)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddChild(UiTheme.Label($"Rift reward:  +{rewards.Gold} gold", 18, UiTheme.Gold));
        foreach (IGrouping<string, string> group in rewards.Orbs.GroupBy(o => o))
        {
            row.AddChild(UiTheme.PixelIcon(ItemArt.ForCurrency(group.Key), 26f));
            CraftingCurrency? orb = Crafting.Find(group.Key);
            row.AddChild(UiTheme.Label($"{group.Count()}× {orb?.Name ?? group.Key}", 15, orb?.Color ?? UiTheme.Text));
        }

        return row;
    }

    public static List<EquipmentItem> RollRewards(RunRewards rewards)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        var items = new List<EquipmentItem>();
        var usedBases = new HashSet<string>();
        for (int i = 0; i < rewards.Choices; i++)
        {
            EquipmentItem item;
            int guard = 0;
            do
            {
                item = ItemFactory.RollRandom(rewards.ItemLevel, DropSource.RiftReward, rewards.RarityMultiplier, rng);
            }
            while (!usedBases.Add(item.Base!.Id) && ++guard < 10);

            items.Add(item);
        }

        return items;
    }

    public static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Magic => new Color(0.53f, 0.53f, 1f),
        Rarity.Rare => new Color(1f, 1f, 0.47f),
        _ => new Color(0.85f, 0.85f, 0.85f),
    };

    private Control RewardCard(EquipmentItem item, float width)
    {
        Color color = RarityColor(item.Rarity);
        var card = new Button { CustomMinimumSize = new Vector2(width, 260f), FocusMode = Control.FocusModeEnum.All, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Panel, color with { A = 0.5f }, 2, 8, 12));
        card.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f), color, 3, 8, 12));
        card.AddThemeStyleboxOverride("focus", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f, 0f), color, 3, 8, 12));
        // The tooltip text carries BBCode colour tags (rarity), same as the
        // in-game tooltip, so it needs a RichTextLabel.
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = ItemTooltipText.Build(item),
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        text.AddThemeFontSizeOverride("normal_font_size", 15);
        text.AddThemeColorOverride("default_color", color);
        card.AddChild(text);
        text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        text.OffsetLeft = 12f;
        text.OffsetRight = -12f;
        text.OffsetTop = 10f;
        text.OffsetBottom = -10f;
        card.Pressed += () => Claim(item, card);
        return card;
    }

    private void Claim(EquipmentItem item, Button card)
    {
        if (_claimed >= _keep || card.Disabled)
        {
            return;
        }

        Profile? profile = Profile.Get(this);
        if (profile is null || !profile.AddToStash(item))
        {
            _rewardHint.Text = "Your inventory is full — make room in the hideout first.";
            return;
        }

        _claimed++;
        profile.Save();
        Core.Sfx.Play(this, "pickup");
        card.Disabled = true;
        card.SetMeta("claimed", true);
        int left = _keep - _claimed;
        _rewardHint.Text = left > 0
            ? $"{item.Descriptor.DisplayName} added to your inventory — {left} more to choose."
            : $"{item.Descriptor.DisplayName} added to your inventory.";
        if (left > 0)
        {
            return;
        }

        foreach (Node child in _rewards.GetChildren())
        {
            if (child is Button b)
            {
                bool kept = b.HasMeta("claimed");
                b.Disabled = true;
                b.Modulate = kept ? Colors.White : new Color(1f, 1f, 1f, 0.35f);
            }
        }
    }
}
