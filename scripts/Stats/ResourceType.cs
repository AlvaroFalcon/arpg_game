namespace ArpgGame.Stats;

/// <summary>
/// A resource pool an actor can spend from. Part of the authored cost shape on a
/// skill definition (see docs/design/skills.md -> "Resource costs") — the resource
/// type is part of the authoring, not a hardcoded "mana" assumption. Only `Mana` is
/// spent by any shipped skill in the first slice; `Life` and `Energy` are supported
/// by the model without a shipped consumer yet.
/// </summary>
public enum ResourceType
{
    Mana,
    Life,
    Energy,
}
