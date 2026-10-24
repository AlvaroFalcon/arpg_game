#nullable enable
using Godot;
using ArpgGame.Combat;

namespace ArpgGame.UI;

/// <summary>Who a damage number belongs to — it decides the style (docs/design/menus.md
/// -> "Damage numbers").</summary>
public enum DamageNumberKind : byte
{
    /// <summary>The local player's hit (or an unattributed one): full emphasis.</summary>
    Own,

    /// <summary>A co-op ally's hit: smaller, dimmer, no tags, no icon.</summary>
    Ally,

    /// <summary>Damage over time (ignite, poison) or an explosion: a small row that sums its ticks.</summary>
    Tick,

    /// <summary>Damage the local player takes: red, beside the character, never in a column.</summary>
    Incoming,
}

/// <summary>The qualities of one hit a number can show on its tag line. Only what the
/// game already resolves per hit: the crit roll and the ailments the hit applied.</summary>
[System.Flags]
public enum HitTag : byte
{
    None = 0,
    Critical = 1,
    Ignite = 2,
    Chill = 4,
    Shock = 8,
    Freeze = 16,
}

/// <summary>Everything a damage number shows about one hit.</summary>
public struct DamageHit
{
    public float Amount;
    public DamageType Type;
    public HitTag Tags;
    public Texture2D? Icon;
    public DamageNumberKind Kind;

    /// <summary>A large share of an elite's or a boss's life: shown big like a crit.</summary>
    public bool Heavy;

    /// <summary>The target is an elite or a boss: its hits keep full emphasis in a crowd.</summary>
    public bool Important;

    /// <summary>Colour override (ticks: the ailment's colour).</summary>
    public Color? Tint;

    /// <summary>Text instead of a number ("AVOIDED").</summary>
    public string? Word;
}

/// <summary>
/// One line of a damage-number column (docs/design/menus.md -> "Damage numbers"): an
/// optional skill icon, an optional tag line in caps ("CRITICAL IGNITE"), and the
/// number, with a golden streak behind it when the hit is big. Drawn in screen
/// pixels by <see cref="_Draw"/>; <see cref="DamageNumberLayer"/> owns every row,
/// moves them, scales them against the camera zoom and pools them (a finished row
/// hides and waits to be reused: docs/design/performance.md -> "Mass deaths").
/// No Label, no theme lookups: one canvas item per row, redrawn only while its
/// flash animates.
/// </summary>
[GlobalClass]
public partial class DamageNumber : Node2D
{
    // --- what the row shows (set by Configure) ---------------------------------------
    public DamageNumberKind Kind { get; private set; }
    public HitTag Tags { get; private set; }
    public DamageType Type { get; private set; }
    public float Amount { get; private set; }
    public string Text { get; private set; } = "";
    public string TagText { get; private set; } = "";

    /// <summary>Big style: huge number, punch, golden streak.</summary>
    public bool Big { get; private set; }

    /// <summary>A crit shown without the streak (Compact, or too many big hits at once).</summary>
    public bool Medium { get; private set; }

    public Texture2D? Icon { get; private set; }

    /// <summary>How many hits this row has summed (merged ticks or rapid small hits).</summary>
    public int Merged { get; private set; }

    // --- animation state (driven by DamageNumberLayer) -----------------------------
    internal DamageNumberLayer.Column? Column;
    internal float Age;

    /// <summary>Seconds since the row started (merges reset <see cref="Age"/>, not this).</summary>
    internal float Since;
    internal float Life;
    internal float Y;
    internal float TargetY;
    internal bool Leaving;
    internal float LeaveAge;
    internal float Alpha = 1f;
    internal Vector2 WorldAnchor;

    /// <summary>Height of the row in screen pixels (for stacking).</summary>
    internal float Height;

    private Color _color;
    private float _size;
    private float _textWidth;
    private bool _showTags;
    private bool _rightAlign;
    private float _flash;

    private const float TagSize = 14f;
    private const float StreakTime = 0.7f;

    internal void Configure(in DamageHit hit, bool big, bool medium, bool full, bool crowd = false)
    {
        Kind = hit.Kind;
        Tags = hit.Tags;
        Type = hit.Type;
        Amount = hit.Amount;
        Big = big;
        Medium = medium && !big;
        Merged = 1;
        Icon = full && hit.Kind == DamageNumberKind.Own ? hit.Icon : null;
        _showTags = full && hit.Kind == DamageNumberKind.Own && hit.Tags != HitTag.None;
        _rightAlign = hit.Kind == DamageNumberKind.Incoming;
        bool crit = (hit.Tags & HitTag.Critical) != 0;

        _color = hit.Kind switch
        {
            DamageNumberKind.Incoming => hit.Word is null ? UiTheme.DamageTaken : UiTheme.TextDim,
            DamageNumberKind.Tick => hit.Tint ?? UiTheme.DamagePhysical.Darkened(0.15f),
            DamageNumberKind.Ally => (crit ? UiTheme.DamageCrit : UiTheme.DamageColor(hit.Type)).Lerp(new Color(0.75f, 0.73f, 0.7f), 0.45f),
            _ => crit ? UiTheme.DamageCrit : big ? UiTheme.DamageColor(hit.Type).Lightened(0.15f) : UiTheme.DamageColor(hit.Type),
        };

        _size = hit.Kind switch
        {
            DamageNumberKind.Incoming => hit.Heavy ? 28f : hit.Word is null ? 22f : 17f,
            DamageNumberKind.Tick => 16f,
            DamageNumberKind.Ally => crit ? 20f : 17f,
            _ => big ? 40f : Medium ? (crowd ? 24f : 28f) : crowd ? 19f : 22f,
        };

        Alpha = hit.Kind == DamageNumberKind.Ally ? 0.62f : 1f;
        Life = big ? 1.35f : hit.Kind == DamageNumberKind.Tick ? 0.9f : hit.Kind == DamageNumberKind.Incoming ? 0.95f : 1.0f;
        TagText = _showTags ? TagLine(hit.Tags) : "";
        SetText(hit.Word ?? Format(hit.Amount));
        Height = _size + 4f + (_showTags ? TagSize + 2f : 0f);
        Age = 0f;
        Since = 0f;
        LeaveAge = 0f;
        Leaving = false;
        _flash = big ? StreakTime : 0f;
        QueueRedraw();
    }

    /// <summary>Adds a merged hit or tick: the row shows the running total and pops again.</summary>
    internal void Add(float amount, float holdFrom)
    {
        Amount += amount;
        Merged++;
        SetText(Format(Amount));
        Age = Mathf.Min(Age, holdFrom);
        QueueRedraw();
    }

    private void SetText(string text)
    {
        Text = text;
        _textWidth = FontFor().GetStringSize(text, HorizontalAlignment.Left, -1, (int)_size).X;
    }

    private Font FontFor() => Big ? UiTheme.BigNumberFont : UiTheme.NumberFont;

    /// <summary>"CRITICAL IGNITE CHILL" — in the order a player reads importance.</summary>
    public static string TagLine(HitTag tags)
    {
        if (tags == HitTag.None)
        {
            return "";
        }

        var parts = new System.Collections.Generic.List<string>(4);
        if ((tags & HitTag.Critical) != 0)
        {
            parts.Add("CRITICAL");
        }

        if ((tags & HitTag.Freeze) != 0)
        {
            parts.Add("FREEZE");
        }
        else if ((tags & HitTag.Chill) != 0)
        {
            parts.Add("CHILL");
        }

        if ((tags & HitTag.Ignite) != 0)
        {
            parts.Add("IGNITE");
        }

        if ((tags & HitTag.Shock) != 0)
        {
            parts.Add("SHOCK");
        }

        return string.Join(" ", parts);
    }

    public static string Format(float amount)
    {
        float a = Mathf.Max(0f, amount);
        return a >= 1e6f ? $"{a / 1e6f:0.0}M" : a >= 100000f ? $"{a / 1000f:0}k" : Mathf.RoundToInt(a).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Per-frame animation; returns false when the row is finished.</summary>
    internal bool Step(float dt, float inv, Vector2 anchor)
    {
        Age += dt;
        Since += dt;
        if (Leaving)
        {
            LeaveAge += dt;
        }

        if (_flash > 0f)
        {
            _flash = Mathf.Max(0f, _flash - dt);
            QueueRedraw();
        }

        float fadeStart = Life - 0.3f;
        float a = Age < fadeStart ? 1f : 1f - (Age - fadeStart) / 0.3f;
        if (Leaving)
        {
            a = Mathf.Min(a, 0.7f * (1f - LeaveAge / 0.16f));
        }

        if (a <= 0f)
        {
            return false;
        }

        float punch = 1f;
        if (Big && Age < 0.16f)
        {
            float t = Age / 0.16f;
            punch = 1f + 0.8f * (1f - t) * (1f - t);
        }
        else if (Age < 0.08f && Kind != DamageNumberKind.Tick)
        {
            punch = 1f + 0.25f * (1f - Age / 0.08f);
        }

        float k = 1f - Mathf.Exp(-18f * dt);
        Y = Mathf.Lerp(Y, TargetY, k);
        float drift = Leaving ? -10f * LeaveAge / 0.16f : Column is null ? -40f * Age / Life : 0f;
        Position = anchor + new Vector2(0f, (Y + drift) * inv);
        Scale = new Vector2(inv * punch, inv * punch);
        Modulate = new Color(1f, 1f, 1f, Alpha * Mathf.Clamp(a, 0f, 1f));
        return true;
    }

    public override void _Draw()
    {
        Font font = FontFor();
        float x = 0f;
        if (Icon is not null)
        {
            float side = Big ? 30f : Medium ? 22f : 18f;
            DrawRect(new Rect2(-1f, -side / 2f - 1f, side + 2f, side + 2f), UiTheme.NumberOutline);
            DrawTextureRect(Icon, new Rect2(0f, -side / 2f, side, side), false);
            x = side + 6f;
        }

        if (_rightAlign)
        {
            x = -_textWidth;
        }

        float baseline = _size * 0.36f;
        if (Big)
        {
            DrawStreak(x);
        }

        int size = (int)_size;
        DrawStringOutline(font, new Vector2(x, baseline), Text, HorizontalAlignment.Left, -1, size, Big ? 9 : 6, UiTheme.NumberOutline);
        DrawString(font, new Vector2(x, baseline), Text, HorizontalAlignment.Left, -1, size, _color);

        if (_showTags)
        {
            DrawTags(new Vector2(x + 2f, -_size * 0.5f - 3f));
        }
    }

    /// <summary>The horizontal golden flash behind a big number: bright at first, then
    /// settling to a faint band before it goes.</summary>
    private void DrawStreak(float x)
    {
        float t = 1f - _flash / StreakTime; // 0 → 1
        float a = t < 0.12f ? 1f : Mathf.Lerp(0.85f, 0f, (t - 0.12f) / 0.88f);
        if (a <= 0.01f)
        {
            return;
        }

        float h = _size * (0.95f - 0.3f * t);
        float left = x - 26f;
        float mid = x + _textWidth * 0.35f;
        float right = x + _textWidth + 90f + 60f * (1f - t);
        Color c = UiTheme.DamageStreak;
        var clear = new Color(c, 0f);
        var solid = new Color(c, 0.7f * a);
        float top = -h / 2f;
        float bot = h / 2f;
        DrawPolygon(new[] { new Vector2(left, top), new Vector2(mid, top), new Vector2(mid, bot), new Vector2(left, bot) }, new[] { clear, solid, solid, clear });
        DrawPolygon(new[] { new Vector2(mid, top), new Vector2(right, top), new Vector2(right, bot), new Vector2(mid, bot) }, new[] { solid, clear, clear, solid });
        var core = new Color(1f, 0.95f, 0.75f, 0.9f * a);
        var coreClear = new Color(core, 0f);
        float ch = 1.5f + 2f * (1f - t);
        DrawPolygon(new[] { new Vector2(left, -ch), new Vector2(right, -ch), new Vector2(right, ch), new Vector2(left, ch) }, new[] { core, coreClear, coreClear, core });
    }

    private void DrawTags(Vector2 at)
    {
        Font font = UiTheme.NumberFont;
        float x = at.X;
        foreach (string word in TagText.Split(' '))
        {
            Color c = word switch
            {
                "CRITICAL" => UiTheme.DamageCrit.Lightened(0.15f),
                "IGNITE" => UiTheme.DamageFire,
                "CHILL" or "FREEZE" => UiTheme.DamageCold,
                "SHOCK" => UiTheme.DamageLightning,
                _ => UiTheme.Text,
            };
            DrawStringOutline(font, new Vector2(x, at.Y), word, HorizontalAlignment.Left, -1, (int)TagSize, 4, UiTheme.NumberOutline);
            DrawString(font, new Vector2(x, at.Y), word, HorizontalAlignment.Left, -1, (int)TagSize, c);
            x += font.GetStringSize(word + " ", HorizontalAlignment.Left, -1, (int)TagSize).X;
        }
    }
}
