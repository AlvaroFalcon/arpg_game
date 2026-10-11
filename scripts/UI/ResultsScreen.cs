#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Core;
using PathToExile.Items;
using PathToExile.Progression;
using PathToExile.Run;

namespace PathToExile.UI;

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
    private bool _claimed;

    public override void _Ready()
    {
        Layer = 25;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        _root.Visible = false;
    }

    public void ShowResults(RunState state, RunBuild build)
    {
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

        _rewardHint = UiTheme.Label("Choose one item to keep", 24, UiTheme.Ember, HorizontalAlignment.Center);
        _box.AddChild(_rewardHint);
        _rewards = UiTheme.HBox(_box, 14);
        _rewards.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (EquipmentItem item in RollRewards(state))
        {
            _rewards.AddChild(RewardCard(item));
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

    public static List<EquipmentItem> RollRewards(RunState state)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        int itemLevel = Mathf.Clamp(3 + Mathf.RoundToInt(state.Elapsed / 60f * 5f) + state.BossesKilled * 8, 1, 100);
        float quality = Mathf.Clamp(state.Elapsed / 450f * 0.5f + state.BossesKilled * 0.25f + (state.Victory ? 0.3f : 0f), 0f, 1.2f);
        var items = new List<EquipmentItem>();
        var usedBases = new HashSet<string>();
        for (int i = 0; i < 3; i++)
        {
            EquipmentItem item;
            int guard = 0;
            do
            {
                item = ItemFactory.RollRandom(itemLevel, quality, rng);
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

    private Control RewardCard(EquipmentItem item)
    {
        Color color = RarityColor(item.Rarity);
        var card = new Button { CustomMinimumSize = new Vector2(280f, 260f), FocusMode = Control.FocusModeEnum.All, ClipContents = true };
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
        if (_claimed)
        {
            return;
        }

        Profile? profile = Profile.Get(this);
        if (profile is null || !profile.AddToStash(item))
        {
            _rewardHint.Text = "Your inventory is full — make room in the hideout first.";
            return;
        }

        _claimed = true;
        profile.Save();
        Core.Sfx.Play(this, "pickup");
        _rewardHint.Text = $"{item.Descriptor.DisplayName} added to your inventory.";
        foreach (Node child in _rewards.GetChildren())
        {
            if (child is Button b)
            {
                b.Disabled = b != card;
                b.Modulate = b == card ? Colors.White : new Color(1f, 1f, 1f, 0.35f);
            }
        }
    }
}
