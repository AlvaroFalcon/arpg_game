#nullable enable
using Godot;

namespace PathToExile.UI;

/// <summary>
/// The character panels available anywhere the persistent character is in
/// play (hideout and realities): K opens skills, P the passive tree.
/// </summary>
public partial class CharacterPanels : Node
{
    public Player Player { get; set; } = null!;

    public SkillsPanel Skills { get; private set; } = null!;
    public PassiveTreePanel Tree { get; private set; } = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Skills = new SkillsPanel { Player = Player, Name = "SkillsPanel" };
        AddChild(Skills);
        Tree = new PassiveTreePanel { Player = Player, Name = "PassiveTreePanel" };
        AddChild(Tree);
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("toggle_skills") && !Tree.IsOpen)
        {
            Skills.Toggle();
        }
        else if (Input.IsActionJustPressed("toggle_passives") && !Skills.IsOpen)
        {
            Tree.Toggle();
        }
    }
}
