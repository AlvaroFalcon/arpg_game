#nullable enable
using System.Linq;
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.UI;

/// <summary>
/// The playground's DPS meter (docs/design/playground.md -> "DPS meter"): everything
/// <see cref="Owner"/> deals — hits, and damage over time (bleed, ignite, corruption,
/// detonations) — counted by <see cref="DpsTracker"/> and broken down by source. Damage to
/// anything but a player counts, overkill included; damage the owner takes does not.
/// </summary>
public partial class DpsMeter : PanelContainer
{
    public Player Owner = null!;

    public DpsTracker Tracker { get; } = new();

    private Label _big = null!;
    private Label _stats = null!;
    private VBoxContainer _rows = null!;
    private float _redraw;

    /// <summary>Game time, not wall-clock: on a slow frame the game slows down too, and DPS
    /// must be damage per second of game.</summary>
    private double _clock;

    private float Now => (float)_clock;

    public override void _PhysicsProcess(double delta) => _clock += delta;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.09f, 0.92f), UiTheme.Gold, 2, 6, 12));
        VBoxContainer box = UiTheme.VBox(this, 4);
        HBoxContainer head = UiTheme.HBox(box, 8);
        Label title = UiTheme.Label("DPS meter · F3", 15, UiTheme.Gold);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        Button reset = UiTheme.Button("Reset", Tracker.Reset, 12, 64f);
        reset.FocusMode = FocusModeEnum.None;
        head.AddChild(reset);
        _big = UiTheme.Label("0", 30, UiTheme.Text);
        box.AddChild(_big);
        _stats = UiTheme.Label("", 12, UiTheme.TextDim);
        // Wrap inside the panel's width instead of pushing it off the right edge.
        _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _stats.CustomMinimumSize = new Vector2(236f, 0f);
        box.AddChild(_stats);
        _rows = UiTheme.VBox(box, 2);
        Health.AnyHitResolved += OnHit;
        Health.AnyResolvedDamage += OnResolved;
        Show();
        Redraw();
    }

    public override void _ExitTree()
    {
        Health.AnyHitResolved -= OnHit;
        Health.AnyResolvedDamage -= OnResolved;
    }

    /// <summary>Whether this damage is the owner's, dealt to something that is not a player.</summary>
    private bool Counts(Health target, Node2D? source) =>
        IsInstanceValid(Owner) && source == Owner && target.GetParent() is not Player;

    private void OnHit(Health target, DamagePayload payload, float taken)
    {
        if (Counts(target, payload.SourceActor))
        {
            Tracker.Add(Now, taken, payload.Skill?.DisplayName ?? "Other", hit: true, crit: payload.IsCrit);
        }
    }

    private void OnResolved(Health target, float amount, Node2D? source, string label)
    {
        if (Counts(target, source))
        {
            Tracker.Add(Now, amount, label);
        }
    }

    public override void _Process(double delta)
    {
        _redraw -= (float)delta;
        if (_redraw <= 0f && Visible)
        {
            _redraw = 0.25f;
            Redraw();
        }
    }

    private static string Short(float v) => v >= 1_000_000f ? $"{v / 1_000_000f:0.00}M" : v >= 10_000f ? $"{v / 1000f:0.0}k" : $"{v:N0}";

    private void Redraw()
    {
        DpsTracker t = Tracker;
        float now = Now;
        bool idle = t.Started && now - t.Last > DpsTracker.IdleReset;
        _big.Text = t.Started ? $"{Short(idle ? t.FightDps : t.Recent(now))} DPS" : "— DPS";
        _big.AddThemeColorOverride("font_color", idle ? UiTheme.TextDim : UiTheme.Text);
        _stats.Text = !t.Started
            ? "Hit something to start. A pause of 5 s starts a new fight."
            : $"{(idle ? "Last fight" : "Last 5 s")}  ·  fight {Short(t.FightDps)} DPS over {t.FightSeconds:0.0} s\n"
              + $"Total {Short(t.Total)}  ·  peak {Short(t.PeakSecond)} in 1 s\n"
              + $"{t.Hits} hits" + (t.Hits > 0 ? $"  ·  {100f * t.Crits / t.Hits:0}% crits" : "");
        foreach (Node n in _rows.GetChildren())
        {
            n.QueueFree();
        }

        foreach ((string label, float total) in t.Breakdown.Take(7))
        {
            float share = t.Total > 0f ? total / t.Total : 0f;
            HBoxContainer row = UiTheme.HBox(_rows, 6);
            Label name = UiTheme.Label(label, 12, UiTheme.Text);
            name.CustomMinimumSize = new Vector2(104f, 0f);
            name.ClipText = true;
            row.AddChild(name);
            var bar = new ColorRect { Color = UiTheme.Gold with { A = 0.75f }, CustomMinimumSize = new Vector2(Mathf.Max(2f, 70f * share), 10f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
            row.AddChild(bar);
            Label value = UiTheme.Label($"{share * 100f:0}% · {Short(total / t.FightSeconds)}/s", 11, UiTheme.TextDim);
            row.AddChild(value);
        }
    }
}
