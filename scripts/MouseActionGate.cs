#nullable enable
using Godot;

namespace PathToExile;

/// <summary>
/// Tracks whether a mouse-bound input action is "held," in a way that actually
/// respects what the GUI has consumed — docs/design/items.md -&gt; "UI must
/// consume its own input": "Control nodes consume what they handle, and the
/// movement controller ignores clicks the GUI has taken."
///
/// <b>Why polling <see cref="Input.IsActionPressed"/> every frame is the wrong
/// fix, not just an inconvenient one:</b> <see cref="Viewport.SetInputAsHandled"/>'s
/// own doc is explicit that consuming an event "does not affect the methods in
/// Input, only the way events are propagated." A <see cref="Control"/> with
/// <see cref="Control.MouseFilterEnum.Stop"/> (docs confirm Stop "automatically
/// marks events as handled") swallows a click before it ever reaches
/// <see cref="Node._UnhandledInput"/> — but the <em>global</em> Input singleton
/// keeps reporting the button pressed regardless, because that state is a raw
/// hardware snapshot taken before any node sees the event at all. A caster that
/// polls <c>Input.IsActionPressed</c> therefore cannot tell "the player is
/// holding LMB over the game world" from "the player is dragging an item over
/// the inventory panel" — which is exactly milestone 2c/2d's bug: a drag over
/// the grid also issued a move order.
///
/// <b>The fix:</b> the only channel that genuinely reflects GUI consumption is
/// the engine's own unhandled-input dispatch — per <c>Node._unhandled_input</c>'s
/// doc, it is "called when an InputEvent hasn't been consumed by <c>_input()</c>
/// or any GUI Control item." So this type only <i>raises</i> its latch from an
/// event that survives to <see cref="HandleUnhandledInput"/> — a click the GUI
/// ate never reaches here, no matter how long it's held. It only <i>lowers</i>
/// the latch by polling <see cref="Input.IsActionPressed"/> going false, which is
/// always safe to trust unconditionally: a real physical release should always
/// stop whatever the held button was driving, even if the matching release
/// event happened to be delivered to a Control instead (e.g. the player drags
/// from the world into the panel before releasing) — see the class using this
/// type for why silently getting "stuck held" would be worse than the reverse.
///
/// One instance per mouse-bound action; construct once and reuse. Keyboard-bound
/// actions (WASD, <c>force_move</c>, slot hotkeys 1-4) don't need this — Controls
/// only intercept mouse/keyboard-focus input, and nothing in this project grabs
/// keyboard focus, so polling those directly is fine.
/// </summary>
public sealed class MouseActionGate
{
    private readonly string _action;
    private bool _held;

    public MouseActionGate(string action) => _action = action;

    /// <summary>Feed every event from <c>Node._UnhandledInput</c>. Only a press
    /// that the GUI didn't consume ever reaches this method — see class doc.</summary>
    public void HandleUnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(_action))
        {
            _held = true;
        }
    }

    /// <summary>
    /// Call once per frame (physics or idle, matching whatever loop drives the
    /// caster) to read the current, GUI-respecting held state. Must be called
    /// every frame the caller cares about the falling edge — see class doc on
    /// why release is trusted directly from the Input singleton rather than
    /// waited for via <see cref="HandleUnhandledInput"/>.
    /// </summary>
    public bool Poll()
    {
        if (!Input.IsActionPressed(_action))
        {
            _held = false;
        }

        return _held;
    }
}
