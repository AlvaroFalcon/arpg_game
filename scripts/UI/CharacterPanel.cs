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

    protected override Vector2 FrameSize => new(1000f, 640f);

    private HBoxContainer _columns = null!;
    private Label _header = null!;

    // The character plate: it lived on the hideout HUD until 2026-10-29 (owner: show it
    // with the character panel instead) — docs/design/menus.md -> "Hideout HUD".
    private Control _portraitHost = null!;
    private string _portraitSprite = "";
    private Label _name = null!;
    private ProgressBar _xp = null!;
    private Label _xpText = null!;
    private Label _gold = null!;
    private Label _points = null!;

    protected override void Build(VBoxContainer body)
    {
        var plate = new PanelContainer { Name = "CharacterPlate" };
        plate.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.09f, 0.9f), UiTheme.Gold with { A = 0.6f }, 1, 6, 10));
        body.AddChild(plate);
        HBoxContainer row = UiTheme.HBox(plate, 12);
        _portraitHost = new Control { CustomMinimumSize = new Vector2(56f, 56f) };
        row.AddChild(_portraitHost);
        VBoxContainer text = UiTheme.VBox(row, 1);
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _name = UiTheme.Label("", 20, UiTheme.Gold);
        text.AddChild(_name);
        _header = UiTheme.Label("", 14, UiTheme.TextDim);
        text.AddChild(_header);
        HBoxContainer xpRow = UiTheme.HBox(text, 6);
        _xp = UiTheme.Bar(UiTheme.Experience, 6f);
        _xp.CustomMinimumSize = new Vector2(220f, 6f);
        _xp.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        xpRow.AddChild(_xp);
        _xpText = UiTheme.Label("", 12, UiTheme.TextDim);
        xpRow.AddChild(_xpText);
        VBoxContainer right = UiTheme.VBox(row, 4);
        right.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        HBoxContainer goldRow = UiTheme.HBox(right, 4);
        goldRow.Alignment = BoxContainer.AlignmentMode.End;
        goldRow.AddChild(UiTheme.PixelIcon(ItemArt.ForCurrency("gold"), 18f));
        _gold = UiTheme.Label("", 16, UiTheme.Gold);
        goldRow.AddChild(_gold);
        _points = UiTheme.Label("", 13, new Color(0.12f, 0.08f, 0.02f), HorizontalAlignment.Center);
        var pill = new StyleBoxFlat { BgColor = UiTheme.Gold };
        pill.SetCornerRadiusAll(9);
        pill.SetContentMarginAll(3f);
        pill.ContentMarginLeft = 8f;
        pill.ContentMarginRight = 8f;
        _points.AddThemeStyleboxOverride("normal", pill);
        _points.AddThemeConstantOverride("outline_size", 0);
        _points.AddThemeColorOverride("font_shadow_color", Colors.Transparent);
        right.AddChild(_points);
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
            _name.Text = c.Name;
            _header.Text = $"Level {c.Level} {c.AscendancyDef?.Name ?? c.Class.Name}   ·   best tier {c.HighestTierCleared}   ·   {c.PassivePointsFree} passive / {c.AscendancyPointsFree} ascendancy points free";
            float frac = build?.ExperienceFraction ?? 0f;
            _xp.Value = frac;
            _xpText.Text = $"{Mathf.RoundToInt(frac * 100f)}%  ·  {xp}";
            _gold.Text = Player.Wallet.GetBalance(Profile.GoldId).ToString("N0");
            int passive = c.PassivePointsFree;
            bool ascend = c.AscendancyPointsFree > 0 || c is { AscendancyPoints: > 0, Ascendancy: null };
            _points.Visible = passive > 0 || ascend;
            _points.Text = passive > 0 ? $"{passive} passive point{(passive == 1 ? "" : "s")} to spend (P)" : "Ascendancy ready at the altar";
            string sprite = c.AscendancyDef?.Sprite is { Length: > 0 } sp ? sp : c.Class.Sprite;
            if (sprite != _portraitSprite)
            {
                _portraitSprite = sprite;
                ClearChildren(_portraitHost);
                _portraitHost.AddChild(UiTheme.Portrait(sprite, 56f));
            }
        }

        ClearChildren(_columns);

        VBoxContainer attributes = Column("Attributes");
        float str = V(StatId.Strength), dex = V(StatId.Dexterity), intel = V(StatId.Intelligence);
        Row(attributes, "Strength", N(str), $"+{N(str * 2f)} life, +{N(str)} armour");
        Row(attributes, "Dexterity", N(dex), $"+{N(dex * 0.5f)}% critical chance");
        Row(attributes, "Intelligence", N(intel), $"+{N(intel * 2f)} mana, +{intel * 0.05f:0.##} mana/s");
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
