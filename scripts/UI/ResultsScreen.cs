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

    /// <summary>The last reward claimed and where it went (tests, captures).</summary>
    public Profile.RewardClaim? LastClaim { get; private set; }

    /// <summary>The hint above the item cards (tests, captures).</summary>
    public string RewardHint => _rewardHint?.Text ?? "";

    /// <summary>Claims item card <paramref name="index"/> as if clicked (tests, captures).</summary>
    public void ClaimIndex(int index)
    {
        if (index >= 0 && index < _rewards.GetChildCount() && _rewards.GetChild(index) is Button b)
        {
            b.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }

    /// <summary>Item cards on offer (tests).</summary>
    public int RewardCount => _rewards?.GetChildCount() ?? 0;

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
        if (state.Victory && state.Fell)
        {
            // Falling in the endless phase is still a win: the Colossus was already down.
            Label fell = UiTheme.Label($"You fell at {UiTheme.FormatTime(state.Elapsed)} in the endless phase. The reward is yours.", 18, UiTheme.Ember, HorizontalAlignment.Center);
            fell.Name = "FellSubtitle";
            _box.AddChild(fell);
        }
        _box.AddChild(UiTheme.Label(
            $"Survived {UiTheme.FormatTime(state.Elapsed)}   ·   Level {state.Level}   ·   {state.Kills} kills   ·   {state.ElitesKilled} elites   ·   {state.BossesKilled} bosses   ·   +{state.GoldCollected} gold",
            18, UiTheme.Text, HorizontalAlignment.Center));
        Label summary = UiTheme.Label(LevelUpScreen.BuildSummary(build), 15, UiTheme.TextDim, HorizontalAlignment.Center);
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.CustomMinimumSize = new Vector2(900f, 0f);
        _box.AddChild(summary);

        _box.AddChild(CurrencyRow(rewards));
        if (state.Victory && state.Elapsed > 0f && rewards.Choices < 5)
        {
            // A hint at why staying in the endless phase pays (dropped at five cards: no room).
            _box.AddChild(UiTheme.Label("Every minute survived adds gold, orbs, item cards and items to keep.", 13, UiTheme.TextDim, HorizontalAlignment.Center));
        }

        _rewardHint = UiTheme.Label(rewards.ItemsEarned ? KeepText(rewards.Keep) : rewards.NoItemReason, rewards.ItemsEarned ? 24 : 18, rewards.ItemsEarned ? UiTheme.Ember : UiTheme.TextDim, HorizontalAlignment.Center);
        _rewardHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _rewardHint.CustomMinimumSize = new Vector2(900f, 0f);
        _box.AddChild(_rewardHint);
        _rewards = UiTheme.HBox(_box, 14);
        _rewards.Alignment = BoxContainer.AlignmentMode.Center;
        // Up to five cards: narrow them so they always fit the screen.
        float cardWidth = Mathf.Min(280f, (1180f - 14f * (rewards.Choices - 1)) / Mathf.Max(1, rewards.Choices));
        foreach (EquipmentItem item in RollRewards(rewards))
        {
            _rewards.AddChild(RewardCard(item, cardWidth, rewards.Choices >= 5));
        }

        HBoxContainer buttons = UiTheme.HBox(_box, 16);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        Button hideout = UiTheme.Button("Return to hideout", () => { });
        Button menu = UiTheme.Button("Main menu", () => { });
        hideout.Pressed += () => LeaveOrConfirm(hideout, OnHideout);
        menu.Pressed += () => LeaveOrConfirm(menu, OnMenu);
        buttons.AddChild(hideout);
        buttons.AddChild(menu);
        Core.Sfx.Play(this, state.Victory ? "victory" : "defeat");
    }

    /// <summary>Leaving before choosing forfeits the reward: the first press asks
    /// (docs/design/menus.md -> "Results"), the second leaves.</summary>
    private void LeaveOrConfirm(Button button, Action? next)
    {
        bool unclaimed = _claimed < _keep && _rewards.GetChildCount() > 0;
        if (unclaimed && !button.HasMeta("confirm"))
        {
            button.SetMeta("confirm", true);
            button.Text = "Leave without an item?";
            button.AddThemeColorOverride("font_color", UiTheme.Ember);
            _rewardHint.Text = _claimed == 0
                ? "You haven't chosen an item yet — press again to leave without one."
                : $"{_keep - _claimed} more to choose — press again to leave without them.";
            return;
        }

        Leave(next);
    }

    /// <summary>Public for tests and scripts.</summary>
    public void Leave(Action? next)
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

            // Tier V and up guarantee some cards Rare or better (docs/design/run.md -> "Reward tiers"):
            // the first cards are re-rolled until they are, else made Rare outright.
            if (i < rewards.GuaranteedRare && item.Rarity < Rarity.Rare)
            {
                EquipmentItem? better = null;
                for (int tries = 0; tries < 40 && better is null; tries++)
                {
                    EquipmentItem candidate = ItemFactory.RollRandom(rewards.ItemLevel, DropSource.RiftReward, rewards.RarityMultiplier, rng);
                    if (candidate.Rarity >= Rarity.Rare)
                    {
                        better = candidate;
                    }
                    else
                    {
                        candidate.Free();
                    }
                }

                better ??= ItemFactory.Roll(item.Base!, Rarity.Rare, rewards.ItemLevel, rng);
                item.Free();
                item = better;
            }

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

    private Control RewardCard(EquipmentItem item, float width, bool compact)
    {
        Color color = RarityColor(item.Rarity);
        // Five cards: the "Hold Alt" hint goes (it does nothing on this screen anyway) and the
        // text is a size smaller, so a rare with four modifiers is no longer clipped.
        var card = new Button { CustomMinimumSize = new Vector2(width, compact ? 272f : 260f), FocusMode = Control.FocusModeEnum.All, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.Panel, color with { A = 0.5f }, 2, 8, 12));
        card.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f), color, 3, 8, 12));
        card.AddThemeStyleboxOverride("focus", UiTheme.Box(new Color(0.16f, 0.13f, 0.22f, 0f), color, 3, 8, 12));
        // The tooltip text carries BBCode colour tags (rarity), same as the
        // in-game tooltip, so it needs a RichTextLabel.
        var text = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = ItemTooltipText.Build(item, false, hint: false),
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        text.AddThemeFontSizeOverride("normal_font_size", compact ? 14 : 15);
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
        if (profile is null)
        {
            _rewardHint.Text = "No profile to keep the item in.";
            return;
        }

        // A full inventory is no reason to lose a reward: it goes to Pending items
        // (docs/design/items.md -> "Pending items"), opened from the hideout stash.
        Profile.RewardClaim claim = profile.ClaimReward(item);
        if (!claim.Stored)
        {
            _rewardHint.Text = "That item could not be stored.";
            return;
        }

        _claimed++;
        Core.Sfx.Play(this, "pickup");
        card.Disabled = true;
        card.SetMeta("claimed", true);
        int left = _keep - _claimed;
        string where = claim.ToOverflow
            ? $"{item.Descriptor.DisplayName} sent to Pending items (your inventory is full) — take it from the stash in the hideout."
            : $"{item.Descriptor.DisplayName} added to your inventory.";
        if (claim.SoldOldest is { } sold)
        {
            where += $"\nPending items was full: {sold.Descriptor.DisplayName}, the oldest, was sold for {claim.SoldFor} gold to make room.";
            if (!sold.IsInsideTree())
            {
                sold.QueueFree();
            }
        }

        _rewardHint.Text = left > 0 ? $"{where}  {left} more to choose." : where;
        LastClaim = claim;
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
