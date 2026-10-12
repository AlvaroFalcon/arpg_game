#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using PathToExile.Characters;
using PathToExile.Core;
using PathToExile.UI;
using PathToExile.World;

namespace PathToExile;

/// <summary>
/// The hideout — docs/design/menus.md -> "Hideout" and endgame.md -> "The
/// Mirror". The safe room between realities: your persistent character with
/// its gear, the Mirror of Realities (E) to open shards, the Altar of
/// Ascension (E), the Survival Rift portal, unkillable training dummies, and
/// the inventory (I), skills (K) and passive tree (P). Leaving saves the
/// character through <see cref="Profile"/>.
/// </summary>
public partial class Hideout : Node2D
{
    private const float InteractRange = 80f;

    private Player _player = null!;
    private Node2D _portal = null!;
    private bool _leaving;
    private MirrorPanel _mirror = null!;
    private AscendancyPanel _altar = null!;
    private StashPanel _stash = null!;
    private Label _prompt = null!;
    private Label _status = null!;
    private ProgressBar _xp = null!;
    private readonly List<(string Name, Node2D Node, Action Use)> _interactables = new();

    public override void _Ready()
    {
        GetTree().Paused = false;
        RenderingServer.SetDefaultClearColor(new Color(0.3f, 0.3f, 0.3f));
        _player = GetNode<Player>("Player");
        _portal = GetNode<Node2D>("Portal");
        Profile.Get(this)?.ApplyTo(_player);
        _player.GetNodeOrNull<CanvasLayer>("DebugReadout")?.Hide();
        _player.GetNode<PlayerSkillInput>("PlayerSkillInput").Mode = PlayerSkillInput.CastMode.Bound;
        Sfx.Music(this, "music_menu");

        AddChild(new PauseMenu { LeaveText = "Character select", OnLeave = () => Leave(Scenes.MainMenu) });
        AddChild(new CharacterPanels { Player = _player, Name = "CharacterPanels" });
        _mirror = new MirrorPanel { Player = _player, Travel = Leave, Name = "MirrorPanel" };
        AddChild(_mirror);
        _altar = new AscendancyPanel { Player = _player, Name = "AscendancyPanel" };
        AddChild(_altar);

        var mirrorNode = GD.Load<PackedScene>("res://assets/sprites/props/mirror.tscn").Instantiate<Node2D>();
        mirrorNode.Name = "Mirror";
        mirrorNode.Scale = new Vector2(2.5f, 2.5f);
        AddChild(mirrorNode);
        mirrorNode.Position = new Vector2(300f, 150f);
        _interactables.Add(("Mirror of Realities", mirrorNode, _mirror.Open));

        var altar = new Altar { Name = "Altar", Position = new Vector2(210f, 470f) };
        AddChild(altar);
        _interactables.Add(("Altar of Ascension", altar, _altar.Open));

        _stash = new StashPanel { Player = _player, Name = "StashPanel" };
        AddChild(_stash);
        var chest = new StashChest { Name = "Stash", Position = new Vector2(480f, 500f) };
        AddChild(chest);
        _interactables.Add(("Stash", chest, _stash.Open));
        AddLabel("Stash", chest.Position + new Vector2(0f, -44f));

        _portal.Position = new Vector2(680f, 150f);
        AddLabel("Survival Rift", _portal.Position + new Vector2(0f, -70f));
        AddLabel("Mirror of Realities", mirrorNode.Position + new Vector2(0f, -80f));
        AddLabel("Altar of Ascension", altar.Position + new Vector2(0f, -56f));

        BuildHud();
    }

    private void AddLabel(string text, Vector2 at)
    {
        Label l = UiTheme.Label(text, 14, new Color(0.85f, 0.8f, 1f), HorizontalAlignment.Center);
        l.Size = new Vector2(200f, 20f);
        l.Position = at - new Vector2(100f, 10f);
        l.ZIndex = 5;
        AddChild(l);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer { Layer = 4 };
        AddChild(layer);
        Control root = UiTheme.FullRect(layer);

        _xp = UiTheme.Bar(UiTheme.Experience, 8f);
        root.AddChild(_xp);
        _xp.SetAnchorsPreset(Control.LayoutPreset.TopWide, true);
        _xp.OffsetBottom = 8f;
        _status = UiTheme.Label("", 18, UiTheme.Gold);
        root.AddChild(_status);
        _status.Position = new Vector2(16f, 16f);

        var hintBack = new ColorRect { Color = new Color(0.03f, 0.02f, 0.05f, 0.75f), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(hintBack);
        hintBack.SetAnchorsPreset(Control.LayoutPreset.BottomWide, true);
        hintBack.OffsetTop = -72f;
        Label hint = UiTheme.Label(
            "HIDEOUT — E at the Mirror to open a reality, at the chest for your stash · portal: Survival Rift\nI inventory · C character · K skills · P passive tree · Shift sprint · Space dodge · Esc menu",
            17, UiTheme.Text, HorizontalAlignment.Center);
        root.AddChild(hint);
        hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        hint.OffsetLeft = -520f;
        hint.OffsetRight = 520f;
        hint.OffsetTop = -64f;

        _prompt = UiTheme.Label("", 22, UiTheme.Gold, HorizontalAlignment.Center);
        root.AddChild(_prompt);
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom, true);
        _prompt.OffsetLeft = -300f;
        _prompt.OffsetRight = 300f;
        _prompt.OffsetTop = -150f;
    }

    public override void _Process(double delta)
    {
        CharacterBuild? build = CharacterBuild.Of(_player);
        if (build is not null)
        {
            CharacterData c = build.Data;
            _xp.Value = build.ExperienceFraction;
            string points = c.PassivePointsFree > 0 ? $"  ·  {c.PassivePointsFree} passive point{(c.PassivePointsFree == 1 ? "" : "s")} (P)" : "";
            string asc = c.AscendancyPointsFree > 0 ? $"  ·  {c.AscendancyPointsFree} ascendancy point{(c.AscendancyPointsFree == 1 ? "" : "s")}" : "";
            _status.Text = $"{c.Name} — level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}  ·  gold {_player.Wallet.GetBalance(Profile.GoldId)}  ·  best tier {c.HighestTierCleared}{points}{asc}";
        }

        (string Name, Node2D Node, Action Use)? near = Nearest();
        _prompt.Text = near is { } n && !GetTree().Paused ? $"[E] {n.Name}" : "";
        if (near is { } target && !GetTree().Paused && Input.IsActionJustPressed("interact"))
        {
            target.Use();
        }
    }

    private (string Name, Node2D Node, Action Use)? Nearest()
    {
        foreach ((string Name, Node2D Node, Action Use) i in _interactables)
        {
            if (_player.GlobalPosition.DistanceTo(i.Node.GlobalPosition) < InteractRange)
            {
                return i;
            }
        }

        return null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_leaving && _player.GlobalPosition.DistanceTo(_portal.GlobalPosition) < 42f)
        {
            Leave(Scenes.Run);
        }
    }

    private void Leave(string scene)
    {
        if (_leaving)
        {
            return;
        }

        _leaving = true;
        Profile.Get(this)?.CaptureFrom(_player);
        Scenes.Go(this, scene);
    }

    /// <summary>The stash: an iron-bound chest.</summary>
    private sealed partial class StashChest : Node2D
    {
        public override void _Draw()
        {
            DrawRect(new Rect2(-30f, -10f, 60f, 30f), new Color(0.36f, 0.22f, 0.12f));
            DrawRect(new Rect2(-30f, -26f, 60f, 18f), new Color(0.44f, 0.28f, 0.15f));
            DrawRect(new Rect2(-30f, -26f, 60f, 44f), new Color(0.1f, 0.07f, 0.05f), false, 2f);
            DrawRect(new Rect2(-30f, -10f, 60f, 4f), new Color(0.55f, 0.55f, 0.6f));
            DrawRect(new Rect2(-22f, -26f, 5f, 46f), new Color(0.5f, 0.5f, 0.56f));
            DrawRect(new Rect2(17f, -26f, 5f, 46f), new Color(0.5f, 0.5f, 0.56f));
            DrawRect(new Rect2(-5f, -12f, 10f, 10f), new Color(0.9f, 0.75f, 0.3f));
        }
    }

    /// <summary>The Altar of Ascension: a rune circle that glows once points are waiting.</summary>
    private sealed partial class Altar : Node2D
    {
        private float _t;

        public override void _Process(double delta)
        {
            _t += (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            bool ready = Profile.Get(this)?.Active is { AscendancyPointsFree: > 0 } || Profile.Get(this)?.Active is { AscendancyPoints: > 0, Ascendancy: null };
            Color glow = ready ? new Color(1f, 0.8f, 0.35f) : new Color(0.55f, 0.45f, 0.8f);
            float pulse = 0.6f + 0.4f * Mathf.Sin(_t * (ready ? 4f : 1.5f));
            DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, 0.55f));
            DrawCircle(Vector2.Zero, 40f, new Color(0.12f, 0.1f, 0.16f));
            DrawArc(Vector2.Zero, 38f, 0f, Mathf.Tau, 40, glow with { A = 0.5f + 0.5f * pulse }, 3f);
            DrawArc(Vector2.Zero, 28f, _t * 0.5f, _t * 0.5f + Mathf.Tau * 0.8f, 32, glow with { A = 0.7f }, 2f);
            for (int i = 0; i < 6; i++)
            {
                float a = _t * 0.3f + i * Mathf.Tau / 6f;
                DrawCircle(Vector2.FromAngle(a) * 33f, 3f, glow);
            }

            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawRect(new Rect2(-8f, -34f, 16f, 30f), new Color(0.3f, 0.26f, 0.36f));
            DrawRect(new Rect2(-12f, -38f, 24f, 6f), new Color(0.42f, 0.36f, 0.5f));
            DrawCircle(new Vector2(0f, -44f), 6f + pulse * 2f, glow with { A = 0.9f });
        }
    }
}
