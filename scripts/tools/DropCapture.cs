#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Combat;
using PathToExile.Items;

namespace PathToExile.Tools;

/// <summary>
/// Drives scripted drops and pickups in a real (windowed) run and saves devlog
/// screenshots to docs/devlog/screenshots/. Same rationale as
/// <see cref="SkillCapture"/>: a static frame of a gold pile says nothing about
/// what the pipeline does, so each phase is staged to make one behaviour legible
/// on its own.
///
/// Run with:
///   ./scripts/capture_drop_screenshots.sh
/// Must run windowed: --headless uses the dummy renderer and captures an empty image.
///
/// Produces three images:
///   drops-ground.png    — dummies killed, gold piles on the ground, labels styled
///                         entirely by FilterChain (never by reading Rarity.Color).
///   drops-hidden.png    — a user filter rule hiding small piles; one pile in the
///                         row has no label at all.
///   drops-collected.png — after the walk-through: the hidden pile was collected
///                         too. This is the pair that shows "filters control
///                         display, never behaviour" rather than asserting it.
/// </summary>
public partial class DropCapture : Node2D
{
    private static readonly Vector2 PlayerStart = new(140f, 470f);

    /// <summary>Fixed amounts (not the dummies' random rolls) so the arithmetic in
    /// the final caption is checkable by the reader.</summary>
    private static readonly long[] RowAmounts = { 40L, 18L, 65L, 120L };
    private const long HideBelow = 25L;

    [Export]
    public PackedScene DummyScene = null!;

    [Export]
    public PackedScene DropScene = null!;

    [Export]
    public CurrencyDefinition GoldCurrency = null!;

    private Player _player = null!;
    private Label _walletCaption = null!;

    private readonly List<Node> _phaseNodes = new();

    /// <summary>Top-of-screen captions only. Phase 3 deliberately does NOT call
    /// ResetPhase — it needs phase 2's piles left standing so the player can walk
    /// into them — so its new headline has to replace the old one explicitly, or
    /// the two draw on top of each other.</summary>
    private readonly List<Node> _headerNodes = new();
    private int _phase;
    private int _frame;
    private bool _busy;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");

        // Same two adjustments SkillCapture makes: the follow-camera would track
        // the character instead of framing the play area, and the life/mana
        // readout sits exactly where these captions go.
        _player.GetNode<Camera2D>("Camera2D").Enabled = false;
        _player.GetNode<CanvasLayer>("DebugReadout").Visible = false;
        _player.GlobalPosition = PlayerStart;

        _walletCaption = new Label { Position = new Vector2(40f, 620f) };
        _walletCaption.AddThemeFontSizeOverride("font_size", 20);
        _walletCaption.AddThemeColorOverride("font_color", GoldCurrency.Color);
        AddChild(_walletCaption);

        StartGround();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_busy)
        {
            return;
        }

        _frame++;
        _walletCaption.Text = $"Wallet:  {_player.Wallet.GetBalance("gold")} Gold";

        switch (_phase)
        {
            // Long enough for every dummy's QueueFree to settle and every label to
            // have been through a full _Process projection pass.
            case 1 when _frame > 40:
                Capture("res://docs/devlog/screenshots/drops-ground.png", StartHidden);
                break;

            case 2 when _frame > 30:
                Capture("res://docs/devlog/screenshots/drops-hidden.png", StartWalk);
                break;

            case 3:
                WalkRight();
                if (_frame > 130)
                {
                    Capture("res://docs/devlog/screenshots/drops-collected.png",
                        () => GetTree().Quit());
                }

                break;
        }
    }

    /// <summary>
    /// Phase 1: kill three dummies of different sizes and let their DropSpawners
    /// produce real, randomly-rolled piles — this is the actual gameplay path, not
    /// hand-placed props.
    /// </summary>
    private void StartGround()
    {
        _phase = 1;
        ResetPhase();

        AddCaption(new Vector2(40f, 40f), "Kill a dummy, it drops gold", 24f);
        AddCaption(new Vector2(40f, 76f),
            "DropSpawner listens to Health.Died and spawns into the scene, not under the\n" +
            "dying entity — TargetDummy calls QueueFree on itself and would take the drop with it.\n" +
            "Amount is rolled per-dummy from an authored range; bigger dummies drop more.");

        SpawnAndKillDummy(new Vector2(380f, 300f), 5, 15, "30 life\n5-15 gold");
        SpawnAndKillDummy(new Vector2(640f, 300f), 50, 90, "210 life\n50-90 gold");
        SpawnAndKillDummy(new Vector2(900f, 300f), 120, 200, "420 life\n120-200 gold");

        _busy = false;
    }

    /// <summary>
    /// Phase 2: prepend a user-style filter rule ahead of the built-in defaults —
    /// exactly the path a player-authored filter will take — and lay out a row
    /// where one pile falls under its threshold.
    /// </summary>
    private void StartHidden()
    {
        _phase = 2;
        ResetPhase();

        FilterChain.Default.PrependRules(new[]
        {
            new FilterRule(
                d => d.ItemClass == ItemClass.Currency && d.Amount < HideBelow,
                new DisplayStyle { Hidden = true }),
        });

        AddHeader(new Vector2(40f, 40f), $"A filter rule: hide gold piles under {HideBelow}", 24f);
        AddHeader(new Vector2(40f, 76f),
            "One rule prepended ahead of the built-in defaults. Nothing about the drops changed —\n" +
            "the label pool just never gets a visible label for the pile that matches.");

        for (int i = 0; i < RowAmounts.Length; i++)
        {
            var position = new Vector2(340f + (i * 210f), 470f);
            SpawnPile(position, RowAmounts[i]);

            if (RowAmounts[i] < HideBelow)
            {
                Label marker = AddCaption(position + new Vector2(-58f, -96f),
                    $"{RowAmounts[i]} gold here —\nno label, hidden by\nthe filter rule");
                marker.AddThemeColorOverride("font_color", new Color(0.95f, 0.45f, 0.4f));
            }
        }

        AddHeader(new Vector2(40f, 560f), "Player walks left to right through all four ->");
        _busy = false;
    }

    private void StartWalk()
    {
        _phase = 3;
        _frame = 0;
        _busy = false;
    }

    /// <summary>
    /// Phase 3: drive the player across the row. Movement is a direct position
    /// write rather than the navigation path, because the point being captured is
    /// the Area2D walk-over pickup, not pathfinding — and BodyEntered fires for a
    /// teleported body on the next physics step just the same.
    /// </summary>
    private void WalkRight()
    {
        _player.GlobalPosition = new Vector2(
            Mathf.Min(PlayerStart.X + (_frame * 8f), 1120f), PlayerStart.Y);

        if (_frame == 125)
        {
            long total = 0L;
            foreach (long amount in RowAmounts)
            {
                total += amount;
            }

            ClearHeaders();
            AddHeader(new Vector2(40f, 40f), "Hidden does not mean uncollectable", 24f);
            AddHeader(new Vector2(40f, 76f),
                $"All four piles collected, including the {RowAmounts[1]} the filter hid:\n" +
                $"{string.Join(" + ", RowAmounts)} = {total}. The filter decides what the label\n" +
                "shows. It has no way to reach the pickup path at all.");
        }
    }

    private void SpawnAndKillDummy(Vector2 position, int min, int max, string caption)
    {
        var dummy = DummyScene.Instantiate<Node2D>();
        dummy.Position = position;
        AddChild(dummy);
        _phaseNodes.Add(dummy);

        var spawner = dummy.GetNode<DropSpawner>("DropSpawner");
        spawner.MinAmount = min;
        spawner.MaxAmount = max;

        Label label = AddCaption(position + new Vector2(-34f, 44f), caption);
        label.AddThemeColorOverride("font_color", new Color(0.62f, 0.62f, 0.68f));

        dummy.GetNode<Health>("Health").TakeDamage(new DamagePayload(
            9999f, DamageType.Physical, null, Faction.Player, SkillTags.Spell, DamageDeliveryKind.Hit));
    }

    private void SpawnPile(Vector2 position, long amount)
    {
        var drop = DropScene.Instantiate<CurrencyDrop>();
        AddChild(drop);
        drop.GlobalPosition = position;
        drop.Initialize(GoldCurrency, amount);
        _phaseNodes.Add(drop);
    }

    private Label AddHeader(Vector2 position, string text, float size = 16f)
    {
        Label label = AddCaption(position, text, size);
        _headerNodes.Add(label);
        return label;
    }

    private void ClearHeaders()
    {
        foreach (Node node in _headerNodes)
        {
            if (IsInstanceValid(node))
            {
                node.QueueFree();
            }

            _phaseNodes.Remove(node);
        }

        _headerNodes.Clear();
    }

    private Label AddCaption(Vector2 position, string text, float size = 16f)
    {
        var label = new Label { Position = position, Text = text };
        label.AddThemeFontSizeOverride("font_size", (int)size);
        label.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
        AddChild(label);
        _phaseNodes.Add(label);
        return label;
    }

    private void ResetPhase()
    {
        // IsInstanceValid is load-bearing here, unlike in SkillCapture's copy of
        // this method: that tool's dummies survive its captures (BaseDamage = 1),
        // while this one kills them on purpose, so _phaseNodes holds references
        // TargetDummy.OnDied has already QueueFree'd. Calling QueueFree on a freed
        // node throws ObjectDisposedException out of an async void continuation,
        // where it is swallowed and simply stalls the phase machine.
        foreach (Node node in _phaseNodes)
        {
            if (IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }

        _phaseNodes.Clear();
        _headerNodes.Clear();
        _frame = 0;

        foreach (Node child in GetChildren())
        {
            if (child is CurrencyDrop drop && IsInstanceValid(drop))
            {
                drop.QueueFree();
            }
        }
    }

    private async void Capture(string resPath, System.Action onDone)
    {
        _busy = true;

        // Let a frame finish drawing, otherwise the viewport texture is empty.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image image = GetViewport().GetTexture().GetImage();
        string absolutePath = ProjectSettings.GlobalizePath(resPath);
        Error err = image.SavePng(absolutePath);
        GD.Print(err == Error.Ok
            ? $"[CAPTURE] Saved {absolutePath}"
            : $"[CAPTURE] FAILED to save {absolutePath}: {err}");

        onDone();
    }
}
