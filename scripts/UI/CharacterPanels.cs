#nullable enable
using Godot;

namespace PathToExile.UI;

/// <summary>
/// The character panels available anywhere the persistent character is in
/// play (hideout and realities): K skills, P passive tree, C character sheet.
/// </summary>
public partial class CharacterPanels : Node
{
    public Player Player { get; set; } = null!;

    public SkillsPanel Skills { get; private set; } = null!;
    public PassiveTreePanel Tree { get; private set; } = null!;
    public CharacterPanel Sheet { get; private set; } = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Skills = new SkillsPanel { Player = Player, Name = "SkillsPanel" };
        AddChild(Skills);
        Tree = new PassiveTreePanel { Player = Player, Name = "PassiveTreePanel" };
        AddChild(Tree);
        Sheet = new CharacterPanel { Player = Player, Name = "CharacterPanel" };
        AddChild(Sheet);
    }

    public override void _Process(double delta)
    {
        // One panel at a time; a key toggles its own panel.
        Toggle("toggle_skills", Skills);
        Toggle("toggle_passives", Tree);
        Toggle("toggle_character", Sheet);
    }

    private void Toggle(string action, ModalPanel panel)
    {
        if (!Input.IsActionJustPressed(action))
        {
            return;
        }

        if (!panel.IsOpen && (Skills.IsOpen || Tree.IsOpen || Sheet.IsOpen))
        {
            return;
        }

        panel.Toggle();
    }
}
