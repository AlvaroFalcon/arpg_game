#nullable enable
using Godot;
using ArpgGame.Combat;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Characters;

/// <summary>
/// The class mechanic — docs/design/classes.md -> "The base classes":
/// <list type="bullet">
/// <item><b>Combo</b> (Rogue): each hit adds a stack (max 10): +3% attack speed
/// and +4% critical chance per stack. 2.5 s without hitting and the combo drops.</item>
/// <item><b>Rage</b> (Warrior): hits dealt +1, hits taken +3 (max 30): +1%
/// damage and 0.3% less damage taken per point. Fades 2/s after 3 s out of combat.</item>
/// <item><b>Arcane Charges</b> (Arcanist): each spell cast grants a charge (max 5):
/// +5% cast speed and +6% spell damage each. One drains every 3 s without casting.</item>
/// </list>
/// Expressed as ordinary stat modifiers with their own source, refreshed when
/// the value changes.
/// </summary>
[GlobalClass]
public partial class ClassResource : Node
{
    public ClassResourceKind Kind { get; set; }

    /// <summary>Extra maximum (Barbarian).</summary>
    public int BonusMax { get; set; }

    /// <summary>Barbarian "Wrath": Rage also grants 1% attack speed per point.</summary>
    public bool RageGrantsAttackSpeed { get; set; }

    public int Value { get; private set; }

    public int Max => Kind switch
    {
        ClassResourceKind.Combo => 10,
        ClassResourceKind.Rage => 30,
        _ => 5,
    } + BonusMax;

    public string Label => Kind switch
    {
        ClassResourceKind.Combo => "Combo",
        ClassResourceKind.Rage => "Rage",
        _ => "Arcane Charges",
    };

    public Color Color => Kind switch
    {
        ClassResourceKind.Combo => new Color(0.5f, 1f, 0.55f),
        ClassResourceKind.Rage => new Color(1f, 0.35f, 0.2f),
        _ => new Color(0.6f, 0.55f, 1f),
    };

    private static readonly object EffectSource = new();
    private Player _player = null!;
    private float _idle;
    private float _decayTimer;
    private float _gainCooldown;
    private int _applied = -1;

    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        Health.AnyHitResolved += OnAnyHit;
        _player.SkillUser.CastFired += OnCastFired;
    }

    public override void _ExitTree()
    {
        Health.AnyHitResolved -= OnAnyHit;
        if (IsInstanceValid(_player))
        {
            _player.SkillUser.CastFired -= OnCastFired;
            _player.Stats.RemoveModifiersFromSource(EffectSource);
        }
    }

    private void OnAnyHit(Health target, DamagePayload payload, float taken)
    {
        if (!IsInstanceValid(_player))
        {
            return;
        }

        if (payload.SourceActor == _player && target.GetParent() != _player)
        {
            if (Kind == ClassResourceKind.Combo || Kind == ClassResourceKind.Rage)
            {
                if (_gainCooldown <= 0f)
                {
                    Gain(1);
                    _gainCooldown = Kind == ClassResourceKind.Combo ? 0.12f : 0.15f;
                }
            }

            _idle = 0f;
        }
        else if (target.GetParent() == _player && Kind == ClassResourceKind.Rage && taken > 0f)
        {
            Gain(3);
            _idle = 0f;
        }
    }

    private void OnCastFired(int slotIndex)
    {
        if (Kind != ClassResourceKind.ArcaneCharges)
        {
            return;
        }

        if (_player.SkillUser.GetSkill((SkillSlotId)slotIndex) is { } skill && (skill.Definition.Tags & SkillTags.Spell) != 0 && _gainCooldown <= 0f)
        {
            Gain(1);
            _gainCooldown = 0.6f;
            _idle = 0f;
        }
    }

    /// <summary>Forces a refresh of the stat effects (after a max or flag change).</summary>
    public void Invalidate()
    {
        _applied = -1;
        Value = Mathf.Min(Value, Max);
        Refresh();
    }

    public void Gain(int amount)
    {
        Value = Mathf.Clamp(Value + amount, 0, Max);
        Refresh();
    }

    public void Spend(int amount)
    {
        Value = Mathf.Clamp(Value - amount, 0, Max);
        Refresh();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _idle += dt;
        _gainCooldown -= dt;
        if (Value <= 0)
        {
            return;
        }

        switch (Kind)
        {
            case ClassResourceKind.Combo when _idle > 2.5f:
                Value = 0;
                Refresh();
                break;
            case ClassResourceKind.Rage when _idle > 3f:
                _decayTimer += dt;
                if (_decayTimer >= 0.5f)
                {
                    _decayTimer = 0f;
                    Spend(1);
                }

                break;
            case ClassResourceKind.ArcaneCharges when _idle > 3f:
                _decayTimer += dt;
                if (_decayTimer >= 3f)
                {
                    _decayTimer = 0f;
                    Spend(1);
                }

                break;
        }
    }

    private void Refresh()
    {
        if (Value == _applied)
        {
            return;
        }

        _applied = Value;
        ActorStats stats = _player.Stats;
        stats.RemoveModifiersFromSource(EffectSource);
        if (Value <= 0)
        {
            return;
        }

        switch (Kind)
        {
            case ClassResourceKind.Combo:
                stats.AttackSpeed.AddModifier(new StatModifier(ModifierType.Increased, 3f * Value, EffectSource));
                stats.CritChance.AddModifier(new StatModifier(ModifierType.Increased, 4f * Value, EffectSource));
                break;
            case ClassResourceKind.Rage:
                stats.Damage.AddModifier(new StatModifier(ModifierType.Increased, 1f * Value, EffectSource));
                stats.Get(StatId.DamageTaken).AddModifier(new StatModifier(ModifierType.More, -0.3f * Value, EffectSource));
                if (RageGrantsAttackSpeed)
                {
                    stats.AttackSpeed.AddModifier(new StatModifier(ModifierType.Increased, Value, EffectSource));
                }

                break;
            default:
                stats.CastSpeed.AddModifier(new StatModifier(ModifierType.Increased, 5f * Value, EffectSource));
                stats.Damage.AddModifier(new StatModifier(ModifierType.Increased, 6f * Value, EffectSource, SkillTags.Spell));
                break;
        }
    }
}
