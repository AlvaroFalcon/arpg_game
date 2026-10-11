#nullable enable
using System.Collections.Generic;

namespace PathToExile.UI;

/// <summary>Something <see cref="PanelStack"/> can close on the player's
/// behalf. Implemented by whatever owns a panel's actual visibility — e.g.
/// <see cref="InventoryUI"/> — never by the panel's own visual
/// <see cref="Godot.Control"/> directly, since closing usually means more than
/// flipping <c>Visible</c> (bookkeeping, signals, etc. a future panel might
/// need).</summary>
public interface IClosablePanel
{
    void Close();
}

/// <summary>
/// docs/design/items.md -&gt; "Closing panels": "Escape closes the topmost open
/// panel, and i still toggles the inventory specifically. Any future panel
/// joins the same stack rather than binding its own close key."
///
/// A static registry rather than a scene node. Every panel-owning script
/// (currently just <see cref="InventoryUI"/>) pushes itself on open and removes
/// itself on close — from wherever it already toggles its own visibility, so
/// there is exactly one thing a future panel has to do to participate: call
/// <see cref="Push"/>/<see cref="Remove"/> alongside its own show/hide, and
/// never touch <c>Escape</c>/<c>ui_cancel</c> itself. Whoever polls <c>Escape</c>
/// (today, <see cref="InventoryUI._Process"/> — see that method's doc comment
/// for why the polling site doesn't need to move as more panels are added) just
/// calls <see cref="CloseTopmost"/> and never needs to know how many panels
/// exist or which one is open.
///
/// Static rather than a <see cref="Godot.Node"/> deliberately: nothing here
/// needs scene-tree lifetime or a <c>_Process</c> of its own (the one thing that
/// polls input already has a natural home), and every panel that would need to
/// find this node already lives in the same process/AppDomain — this codebase
/// already keeps comparable shared, no-lifecycle state as plain static members
/// (e.g. <see cref="Items.FilterChain.Default"/>), so this follows the same
/// precedent rather than introducing the project's first scene-tree singleton.
/// </summary>
public static class PanelStack
{
    private static readonly List<IClosablePanel> Open = new();

    /// <summary>Marks <paramref name="panel"/> as the most-recently-opened.
    /// Idempotent: re-pushing an already-open panel (e.g. toggled shut then
    /// back open) just moves it back to the top rather than duplicating it.</summary>
    public static void Push(IClosablePanel panel)
    {
        Open.Remove(panel);
        Open.Add(panel);
    }

    /// <summary>Removes <paramref name="panel"/> from the stack, wherever in it
    /// the panel happens to be — a panel can close itself (e.g. its own toggle
    /// key) without necessarily being on top.</summary>
    public static void Remove(IClosablePanel panel) => Open.Remove(panel);

    /// <summary>
    /// Closes the single most-recently-opened panel, if any. Calls
    /// <see cref="IClosablePanel.Close"/> and trusts that call to remove the
    /// panel from this stack itself (via <see cref="Remove"/>) — the same
    /// single source of truth <see cref="InventoryUI"/> already uses for its own
    /// toggle key, so a panel is never "open" here without also being visibly
    /// open, or vice versa. Returns whether a panel was actually closed, for a
    /// future caller that only wants to consume <c>Escape</c> when it did
    /// something.
    /// </summary>
    public static bool CloseTopmost()
    {
        if (Open.Count == 0)
        {
            return false;
        }

        Open[^1].Close();
        _lastClosedFrame = Godot.Engine.GetProcessFrames();
        return true;
    }

    private static ulong _lastClosedFrame = ulong.MaxValue;

    /// <summary>Forgets every panel — called on scene change, when the panels
    /// themselves are being freed.</summary>
    public static void Clear() => Open.Clear();

    /// <summary>Whether any panel is currently open.</summary>
    public static bool HasOpen => Open.Count > 0;

    /// <summary>
    /// True during the frame in which <see cref="CloseTopmost"/> closed
    /// something. Lets a second Escape consumer (the pause menu,
    /// docs/design/menus.md -> "Pause") stand down without depending on which
    /// of the two happens to process first.
    /// </summary>
    public static bool ClosedThisFrame => _lastClosedFrame == Godot.Engine.GetProcessFrames();
}
