#nullable enable
using System.Collections.Generic;
using Godot;

namespace PathToExile.Combat;

/// <summary>
/// Conditional damage multipliers that depend on both sides of a hit — "more
/// damage against low-life enemies", "chance to block", "more damage against
/// rares" (docs/design/classes.md -> ascendancies). Stat modifiers cannot
/// express these: they need the target, the attacker and the moment. An actor
/// registers one hook; <see cref="Health.TakeDamage"/> asks the attacker's hook
/// (outgoing) and the defender's hook (incoming) and multiplies. A result of 0
/// means the hit was avoided entirely (block, evasion).
/// </summary>
public interface IDamageHook
{
    float Outgoing(Health target, DamagePayload payload);

    float Incoming(Health self, DamagePayload payload);
}

public static class DamageHooks
{
    private static readonly Dictionary<ulong, IDamageHook> Hooks = new();

    public static void Register(Node actor, IDamageHook hook) => Hooks[actor.GetInstanceId()] = hook;

    public static void Unregister(Node actor) => Hooks.Remove(actor.GetInstanceId());

    public static float Multiplier(Health target, DamagePayload payload)
    {
        float m = 1f;
        if (payload.SourceActor is { } source && GodotObject.IsInstanceValid(source) && Hooks.TryGetValue(source.GetInstanceId(), out IDamageHook? outgoing))
        {
            m *= outgoing.Outgoing(target, payload);
        }

        if (Hooks.TryGetValue(target.GetParent().GetInstanceId(), out IDamageHook? incoming))
        {
            m *= incoming.Incoming(target, payload);
        }

        return m;
    }
}
