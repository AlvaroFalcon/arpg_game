#nullable enable
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Items;
using ArpgGame.Stats;

namespace ArpgGame.UI;

/// <summary>
/// The character sheet (C) — docs/design/menus.md -> "Character panels". Live
/// values from the player's stats, grouped like an ARPG sheet: who you are,
/// attributes (with what each one grants), defences, offence and utility.
/// Numbers of magnitude 1+ are shown whole; multipliers as percentages.
/// </summary>
public partial class CharacterPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    protected override string Title => "Character";

    protected override Vector2 FrameSize => new(1000f, 600f);

    private HBoxContainer _columns = null!;
    private Label _header = null!;

    protected override void Build(VBoxContainer body)
    {
        _header = UiTheme.Label("", 18, UiTheme.Gold);
        body.AddChild(_header);
        _columns = UiTheme.HBox(body, 28);
    }

    private ActorStats S => Player.Stats;

    private float V(StatId id) => S.Get(id).Value;

    private static string N(float v) => ItemTooltipText.Number(v);

    private static string Pct(float multiplier) => $"{(multiplier - 1f) * 100f:+0;-0;0}%";

    public override void Refresh()
    {
        CharacterData? c = CharacterBuild.Of(Player)?.Data ?? Profile.Get(this)?.Active;
        CharacterBuild? build = CharacterBuild.Of(Player);
        if (c is not null)
        {
            string xp = c.Level >= CharacterData.MaxLevel ? "max level" : $"{N(c.Experience)} / {N(CharacterData.ExperienceToNext(c.Level))} XP";
            _header.Text = $"{c.Name} — level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}   ·   {xp}   ·   {c.PassivePointsFree} passive / {c.AscendancyPointsFree} ascendancy points free   ·   best tier {c.HighestTierCleared}";
        }

        ClearChildren(_columns);

        VBoxContainer attributes = Column("Attributes");
        float str = V(StatId.Strength), dex = V(StatId.Dexterity), intel = V(StatId.Intelligence);
        Row(attributes, "Strength", N(str), $"+{N(str * 2f)} life, +{N(str)} armour");
        Row(attributes, "Dexterity", N(dex), $"+{N(dex * 0.5f)}% critical chance");
        Row(attributes, "Intelligence", N(intel), $"+{N(intel * 2f)} mana, +{intel * 0.1f:0.#} mana/s");
        if (build?.Resource is { } res)
        {
            Section(attributes, "Class");
            Row(attributes, res.Label, $"{res.Value} / {res.Max}", "");
        }

        VBoxContainer defence = Column("Defence");
        Row(defence, "Life", $"{N(S.Life?.Current ?? 0f)} / {N(S.Life?.Max ?? 0f)}", $"{V(StatId.LifeRegen):0.#}/s regen");
        Row(defence, "Mana", $"{N(S.Mana?.Current ?? 0f)} / {N(S.Mana?.Max ?? 0f)}", $"{V(StatId.ManaRegen):0.#}/s regen");
        Row(defence, "Stamina", $"{N(Player.Stamina)} / {N(Player.MaxStamina)}", $"{N(V(StatId.StaminaRegen))}/s regen");
        Row(defence, "Armour", N(V(StatId.Armour)), "");
        Row(defence, "Fire resistance", $"{N(V(StatId.FireResistance))}%", "");
        Row(defence, "Cold resistance", $"{N(V(StatId.ColdResistance))}%", "");
        Row(defence, "Lightning resistance", $"{N(V(StatId.LightningResistance))}%", "");
        Row(defence, "Damage taken", Pct(V(StatId.DamageTaken)), "");
        Row(defence, "Elemental damage taken", Pct(V(StatId.ElementalDamageTaken)), "");
        Row(defence, "Physical damage taken", Pct(V(StatId.PhysicalDamageTaken)), "");
        Row(defence, "Dodge charges", N(V(StatId.DodgeCharges)), $"recovery {Pct(V(StatId.DodgeRecovery))}");

        VBoxContainer offence = Column("Offence");
        Row(offence, "Attack speed", Pct(V(StatId.AttackSpeed)), "");
        Row(offence, "Cast speed", Pct(V(StatId.CastSpeed)), "");
        Row(offence, "Cooldown recovery", Pct(V(StatId.CooldownRecovery)), "");
        Row(offence, "Critical chance", $"{V(StatId.CritChance) * 100f:0.#}%", "");
        Row(offence, "Critical multiplier", $"{V(StatId.CritMultiplier) * 100f:0}%", "");
        Row(offence, "Area of effect", Pct(V(StatId.AreaOfEffect)), "");
        Row(offence, "Projectile speed", Pct(V(StatId.ProjectileSpeed)), "");
        Row(offence, "Extra projectiles", N(V(StatId.ExtraProjectiles)), $"+{N(V(StatId.ExtraChains))} chains, +{N(V(StatId.ExtraPierce))} pierce");
        Row(offence, "Ignite / shock chance", $"{V(StatId.IgniteChance) * 100f:0}% / {V(StatId.ShockChance) * 100f:0}%", "");
        Row(offence, "Freeze chance", $"{V(StatId.FreezeChance) * 100f:0}%", "");
        Row(offence, "Life leech", $"{V(StatId.LifeLeech):0.#}%", "");
        Row(offence, "Skill duration", Pct(V(StatId.SkillDuration)), "");

        VBoxContainer utility = Column("Utility");
        Row(utility, "Movement speed", N(V(StatId.MovementSpeed)), $"sprint ×{Player.SprintMultiplier:0.##}");
        Row(utility, "Pickup radius", N(V(StatId.PickupRadius)), "");
        Row(utility, "Experience gain", Pct(V(StatId.ExperienceGain)), "");
        Row(utility, "Life on kill", N(V(StatId.LifeOnKill)), "");
        Row(utility, "Mana on kill", N(V(StatId.ManaOnKill)), "");
        Row(utility, "Mana cost of skills", Pct(V(StatId.ManaCostMultiplier)), "");
        Row(utility, "Thorns", N(V(StatId.ThornsDamage)), "");
        if (c is not null)
        {
            Section(utility, "Records");
            Row(utility, "Realities cleared", c.RealitiesCleared.ToString(), "");
            Row(utility, "Monsters slain", c.TotalKills.ToString(), "");
            Row(utility, "Deaths", c.Deaths.ToString(), "");
        }
    }

    private VBoxContainer Column(string title)
    {
        VBoxContainer col = UiTheme.VBox(_columns, 4);
        col.CustomMinimumSize = new Vector2(220f, 0f);
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Section(col, title);
        return col;
    }

    private static void Section(VBoxContainer col, string title)
    {
        col.AddChild(UiTheme.Label(title, 19, UiTheme.Ember));
    }

    private static void Row(VBoxContainer col, string name, string value, string note)
    {
        HBoxContainer row = UiTheme.HBox(col, 6);
        Label n = UiTheme.Label(name, 15, UiTheme.TextDim);
        n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(n);
        row.AddChild(UiTheme.Label(value, 15, UiTheme.Text, HorizontalAlignment.Right));
        if (note.Length > 0)
        {
            Label sub = UiTheme.Label("   " + note, 12, new Color(0.55f, 0.6f, 0.75f));
            col.AddChild(sub);
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (IsOpen && Engine.GetProcessFrames() % 30 == 0)
        {
            Refresh();
        }
    }
}
