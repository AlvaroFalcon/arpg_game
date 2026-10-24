#nullable enable
using Godot;
using ArpgGame.Combat;
using ArpgGame.Net;

namespace ArpgGame.UI;

/// <summary>
/// Co-op revives on this machine — docs/design/multiplayer.md -> "Deaths and revives".
/// Added by the Survival Rift and co-op realities (never offline). It reads E and shows
/// one line above the skill bar:
/// <list type="bullet">
/// <item>next to a downed ally: "Hold E to revive Ana" (or "Bob is reviving Ana": one
/// reviver at a time); while channelling: "Reviving Ana… 3.2 s — stay still";</item>
/// <item>while down: "Getting up in 7 s… or wait for an ally" (or "Bob is reviving you…").</item>
/// </list>
/// Holding E asks the host to start (and, while refused, asks again every
/// <see cref="RetrySeconds"/>); releasing it asks to stop. The host decides everything
/// (<see cref="CoopHost"/>.Revives): this only sends intents and draws what snapshots say.
/// </summary>
public partial class ReviveControl : CanvasLayer
{
    /// <summary>While E is held and the host hasn't started the channel, the start is asked again this often.</summary>
    public const float RetrySeconds = 0.4f;

    /// <summary>This machine's player.</summary>
    public Player Local { get; set; } = null!;

    /// <summary>The downed ally the prompt is about (in reach, or the one being revived), or null.</summary>
    public Player? Target { get; private set; }

    /// <summary>True when E belongs to the revive (a reality's exit portal leaves it alone then).</summary>
    public bool WantsInteract => Target is not null;

    /// <summary>The line on screen ("" when nothing is shown; tests read it).</summary>
    public string PromptText => _label.Visible ? _label.Text : "";

    /// <summary>Intents sent from this machine (tests).</summary>
    public int StartsSent { get; private set; }

    public int StopsSent { get; private set; }

    private Label _label = null!;
    private bool _sentStart;
    private float _retry;

    public override void _Ready()
    {
        Layer = 6;
        _label = UiTheme.Label("", 18, new Color(0.75f, 1f, 0.8f), HorizontalAlignment.Center);
        _label.AnchorLeft = 0.5f;
        _label.AnchorRight = 0.5f;
        _label.AnchorTop = 1f;
        _label.AnchorBottom = 1f;
        _label.OffsetLeft = -320f;
        _label.OffsetRight = 320f;
        _label.OffsetTop = -196f;
        _label.OffsetBottom = -168f;
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 5);
        _label.Visible = false;
        AddChild(_label);
    }

    private static string NameOf(Player p) => p.NetName.Length > 0 ? p.NetName : "your ally";

    /// <summary>The ally (not <paramref name="except"/>) channelling on <paramref name="downed"/>, if any.</summary>
    private Player? ReviverOf(Player downed)
    {
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.PlayerGroup))
        {
            if (node is Player p && IsInstanceValid(p) && p.RevivingTarget == downed)
            {
                return p;
            }
        }

        return null;
    }

    private Player? NearestDowned()
    {
        Player? best = null;
        float bestDistance = PartyRules.ReviveRadius;
        foreach (Node node in GetTree().GetNodesInGroup(CombatQuery.PlayerGroup))
        {
            if (node is not Player p || p == Local || !IsInstanceValid(p) || !p.ShowsDowned || p.IsQueuedForDeletion())
            {
                continue;
            }

            float d = p.GlobalPosition.DistanceTo(Local.GlobalPosition);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }

        return best;
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(Local))
        {
            return;
        }

        string text = "";
        if (Local.Health is { IsDead: true })
        {
            Target = null;
            Release();
            if (ReviverOf(Local) is { } reviver)
            {
                text = $"{NameOf(reviver)} is reviving you… {(1f - Local.ReviveProgress) * PartyRules.ReviveChannelSeconds:0.0} s";
            }
            else
            {
                int secs = Mathf.Max(1, Mathf.CeilToInt((1f - Local.GetUpProgress) * PartyRules.RespawnSeconds));
                text = $"Getting up in {secs} s… or wait for an ally";
            }
        }
        else
        {
            Player? channelling = Local.RevivingTarget is { } t && IsInstanceValid(t) && t.ShowsDowned ? t : null;
            Target = channelling ?? NearestDowned();
            bool held = Input.IsActionPressed("interact") && !PanelStack.HasOpen && Local.ControlsEnabled && !GetTree().Paused;
            if (channelling is not null)
            {
                text = $"Reviving {NameOf(channelling)}… {(1f - channelling.ReviveProgress) * PartyRules.ReviveChannelSeconds:0.0} s — stay still";
            }
            else if (Target is not null)
            {
                text = ReviverOf(Target) is { } other && other != Local
                    ? $"{NameOf(other)} is reviving {NameOf(Target)}"
                    : $"Hold E to revive {NameOf(Target)}";
            }

            if (held && Target is not null && channelling is null)
            {
                _retry -= (float)delta;
                if (_retry <= 0f)
                {
                    _retry = RetrySeconds;
                    Send(true, Target.PeerId);
                    _sentStart = true;
                }
            }
            else if (!held)
            {
                Release();
            }
        }

        _label.Text = text;
        _label.Visible = text.Length > 0;
    }

    /// <summary>E released (or this player went down): stop the channel once.</summary>
    private void Release()
    {
        _retry = 0f;
        if (_sentStart)
        {
            _sentStart = false;
            Send(false, 0);
        }
    }

    private void Send(bool start, long peer)
    {
        if (start)
        {
            StartsSent++;
        }
        else
        {
            StopsSent++;
        }

        switch (NetSession.Get(this)?.Run)
        {
            case CoopHost host:
                host.LocalReviveIntent(start, peer);
                break;
            case CoopClient client:
                client.SendRevive(start, peer);
                break;
        }
    }
}
