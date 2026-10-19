#nullable enable
using System;
using Godot;
using ArpgGame.Net;

namespace ArpgGame.UI;

/// <summary>
/// "Invite friends" / "Party & invite" from the hideout's pause menu —
/// docs/design/menus.md -> "Party overlay". The co-op screen in its Invite
/// purpose over the running hideout: offline it opens this hideout to friends
/// (a lobby that is already home), hosting it shows the invite and the party
/// with Stop hosting, a guest sees the party and Leave the party. Closing it
/// (Close, Esc) never ends anything. It joins <see cref="PanelStack"/> so Esc
/// closes it and not the pause menu behind.
/// </summary>
public partial class PartyOverlay : CanvasLayer, IClosablePanel
{
    /// <summary>Runs before hosting (the hideout saves its player).</summary>
    public Action? BeforeHost { get; set; }

    /// <summary>A guest's "Leave the party".</summary>
    public Action? LeaveParty { get; set; }

    /// <summary>Set before adding: skip UPnP (tests).</summary>
    public bool NoUpnp { get; set; }

    /// <summary>Set before adding: the join code to host with (tests).</summary>
    public string? Code { get; set; }

    public bool IsOpen { get; private set; }

    public CoopPanel Panel { get; private set; } = null!;

    public event Action? Closed;

    private Control _root = null!;

    public override void _Ready()
    {
        Layer = 35;
        ProcessMode = ProcessModeEnum.Always;
        _root = UiTheme.FullRect(this);
        UiTheme.Dim(_root, 0.7f);
        CenterContainer center = UiTheme.Center(_root);
        Panel = new CoopPanel { Purpose = CoopPanel.PanelPurpose.Invite, BeforeHost = BeforeHost, NoUpnp = NoUpnp, AutoCode = Code };
        Panel.Back += Close;
        Panel.LeaveParty += () =>
        {
            Close();
            LeaveParty?.Invoke();
        };
        center.AddChild(Panel);
        _root.Visible = false;
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        _root.Visible = true;
        PanelStack.Push(this);
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _root.Visible = false;
        PanelStack.Remove(this);
        Closed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (IsOpen && Input.IsActionJustPressed("ui_cancel") && !PanelStack.ClosedThisFrame)
        {
            PanelStack.CloseTopmost();
        }
    }

    public override void _ExitTree() => PanelStack.Remove(this);
}
