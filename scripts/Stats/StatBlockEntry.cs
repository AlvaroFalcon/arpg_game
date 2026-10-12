using Godot;

namespace ArpgGame.Stats;

/// <summary>
/// One <c>(StatId, BaseValue)</c> entry inside a <see cref="StatBlock"/>. A
/// Godot <see cref="Resource"/> (not a plain struct) so it can be authored as a
/// sub-resource inside a StatBlock <c>.tres</c> from the editor — the same
/// pattern <see cref="Skills.CostEntry"/> uses inside a SkillDefinition's Costs
/// array.
/// </summary>
[GlobalClass]
public partial class StatBlockEntry : Resource
{
    [Export]
    public StatId Id;

    [Export]
    public float BaseValue;
}
