#nullable enable
using System;
using Godot;

namespace ArpgGame.UI;

/// <summary>
/// Base for the full-screen character panels (Mirror, skills, passive tree,
/// ascendancy) — docs/design/menus.md -> "Panels". Opening pauses the game and
/// joins <see cref="PanelStack"/>; Escape or the close button closes it. The
/// panel itself polls Escape because the usual poller (the inventory) lives on
/// the player and is paused while a modal is up.
/// </summary>
public abstract partial class ModalPanel : CanvasLayer, IClosablePanel
{
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    protected Control Root { get; private set; } = null!;
    protected VBoxContainer Body { get; private set; } = null!;
    protected Label TitleLabel { get; private set; } = null!;

    protected abstract string Title { get; }

    /// <summary>Size of the panel frame.</summary>
    protected virtual Vector2 FrameSize => new(980f, 620f);

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        Root = UiTheme.FullRect(this);
        UiTheme.Dim(Root, 0.75f);
        CenterContainer center = UiTheme.Center(Root);
        var frame = new PanelContainer { CustomMinimumSize = FrameSize };
        center.AddChild(frame);
        Body = UiTheme.VBox(frame, 10);
        HBoxContainer header = UiTheme.HBox(Body, 10);
        TitleLabel = UiTheme.Label(Title, 30, UiTheme.Gold);
        TitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(TitleLabel);
        header.AddChild(UiTheme.Button("Close (Esc)", Close, 16, 140f));
        Build(Body);
        Root.Visible = false;
    }

    protected abstract void Build(VBoxContainer body);

    /// <summary>Re-reads the character and redraws (called on open and after every change).</summary>
    public virtual void Refresh()
    {
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        Root.Visible = true;
        GetTree().Paused = true;
        PanelStack.Push(this);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Root.Visible = false;
        PanelStack.Remove(this);
        GetTree().Paused = false;
        OnClosed();
        Closed?.Invoke();
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else if (!GetTree().Paused)
        {
            Open();
        }
    }

    protected virtual void OnClosed()
    {
    }

    public override void _Process(double delta)
    {
        if (IsOpen && Input.IsActionJustPressed("ui_cancel") && !PanelStack.ClosedThisFrame)
        {
            PanelStack.CloseTopmost();
        }
    }

    /// <summary>Removes and frees every child of <paramref name="node"/>.</summary>
    protected static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
