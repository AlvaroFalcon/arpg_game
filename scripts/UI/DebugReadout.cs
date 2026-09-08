#nullable enable
using Godot;
using PathToExile.Stats;

namespace PathToExile.UI;

/// <summary>
/// Plain-text life/mana readout. Per docs/design/skills.md -> priority item 7,
/// this is deliberately not polish: "without it you cannot tell whether 0.5s use
/// time and 20 damage feel right, and every number authored from here on is
/// guessed." No bar art, no hotbar — just numbers on screen, cheap enough to not
/// be worth deferring behind real UI.
///
/// Milestone 2b extends it with the gear-affected values the equip system
/// brief asks for (Maximum Life, Armour, Strength/Dexterity/Intelligence) — the
/// only way to actually see picking something up have an effect while playing,
/// before an equipment panel exists to show it properly.
/// </summary>
[GlobalClass]
public partial class DebugReadout : CanvasLayer
{
    [Export]
    public NodePath StatsPath = new();

    private ActorStats? _stats;
    private Label _label = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("Label");
        _stats = GetNodeOrNull<ActorStats>(StatsPath);
    }

    public override void _Process(double delta)
    {
        if (_stats is null)
        {
            return;
        }

        string life = _stats.Life is { } l ? $"{l.Current:0}/{l.Max:0}" : "-";
        string mana = _stats.Mana is { } m ? $"{m.Current:0}/{m.Max:0}" : "-";
        float armour = _stats.Armour.Value;
        float strength = _stats.Strength.Value;
        float dexterity = _stats.Dexterity.Value;
        float intelligence = _stats.Intelligence.Value;

        _label.Text = $"Life {life}\nMana {mana}\nArmour {armour:0}\n" +
            $"Str {strength:0}  Dex {dexterity:0}  Int {intelligence:0}";
    }
}
