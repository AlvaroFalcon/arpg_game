#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using ArpgGame.Items;

namespace ArpgGame.UI;

/// <summary>One line of the loot feed: what was picked up. Lines with the same
/// <see cref="Key"/> that arrive while one is still showing merge into it (gold piles add up).</summary>
public readonly record struct LootEntry(string Key, Texture2D? Icon, string Name, Color Color, long Count);

/// <summary>
/// The loot feed's bus — docs/design/menus.md -> "Loot feed". Whatever this machine's player
/// picks up is reported here and every <see cref="LootFeedView"/> in the scene shows it.
/// Anything can be reported (an icon, a name, a colour, a count); gold and crafting
/// currency are wired today. Static like <see cref="Core.GameEvents"/>: with no view
/// listening (tests run scenes directly) a report costs a null check.
/// </summary>
public static class LootFeed
{
    public static event Action<LootEntry>? Reported;

    public static void Report(LootEntry entry) => Reported?.Invoke(entry);

    public static void Gold(Node context, long amount)
    {
        if (amount > 0)
        {
            Report(new LootEntry("gold", ItemArt.ForCurrency("gold"), "Gold", UiTheme.Gold, amount));
        }
    }

    public static void Currency(Node context, string id, int count)
    {
        if (Crafting.Find(id) is { } c && count > 0)
        {
            Report(new LootEntry(c.Id, ItemArt.ForCurrency(c.Id), c.Name, c.Color, count));
        }
    }

    /// <summary>A piece of gear (not wired yet: the bag shows it; kept for when the feed should).</summary>
    public static void Item(Node context, EquipmentItem item) =>
        Report(new LootEntry($"item:{item.GetInstanceId()}", ItemArt.ForItem(item), item.Descriptor.DisplayName, ResultsScreen.RarityColor(item.Rarity), 1));
}

/// <summary>
/// The loot feed on the left of the screen (the owner's sketch, 2026-10-29): one line per
/// pickup, an icon in a small frame and the name, sliding in from the left edge, staying a
/// few seconds and sliding back out to the left. Lines under a leaving one move up to close
/// the gap. A repeat of a showing line (more gold) adds to its count and starts its timer again.
/// </summary>
public partial class LootFeedView : Control
{
    public const float RowWidth = 270f;
    public const float RowHeight = 34f;
    public const float RowGap = 6f;
    public const int MaxRows = 6;

    /// <summary>How long a line stays before it slides out.</summary>
    public const float Hold = 4f;

    private const float SlideIn = 0.28f;
    private const float SlideOut = 0.32f;

    private readonly List<Row> _rows = new();
    private bool _preview;

    /// <summary>The names on the lines showing now, top to bottom (tests).</summary>
    public IEnumerable<string> Lines
    {
        get
        {
            foreach (Row r in _rows)
            {
                yield return r.Text;
            }
        }
    }

    public override void _EnterTree() => LootFeed.Reported += OnReported;

    public override void _ExitTree() => LootFeed.Reported -= OnReported;

    public override void _Ready()
    {
        Name = "LootFeed";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(RowWidth, MaxRows * (RowHeight + RowGap));
        Size = CustomMinimumSize;
    }

    /// <summary>The HUD editor shows two sample lines while it is open.</summary>
    public void SetPreview(bool on)
    {
        _preview = on;
        if (on && _rows.Count == 0)
        {
            Add(new LootEntry("preview_gold", ItemArt.ForCurrency("gold"), "Gold", UiTheme.Gold, 120));
            Add(new LootEntry("preview_orb", ItemArt.ForCurrency("ash_orb"), "Picked-up loot shows here", UiTheme.TextDim, 1));
        }
    }

    private void OnReported(LootEntry entry)
    {
        if (!IsInsideTree())
        {
            return;
        }

        foreach (Row row in _rows)
        {
            if (row.Entry.Key == entry.Key && !row.Leaving)
            {
                row.Merge(entry.Count);
                return;
            }
        }

        Add(entry);
    }

    private void Add(LootEntry entry)
    {
        // Too many: the oldest still sitting there leaves early.
        int staying = 0;
        foreach (Row r in _rows)
        {
            staying += r.Leaving ? 0 : 1;
        }

        if (staying >= MaxRows)
        {
            foreach (Row r in _rows)
            {
                if (!r.Leaving)
                {
                    r.Leave();
                    break;
                }
            }
        }

        var row = new Row(entry) { Size = new Vector2(RowWidth, RowHeight) };
        AddChild(row);
        row.Position = new Vector2(-RowWidth - 20f, SlotY(_rows.Count));
        _rows.Add(row);
    }

    private static float SlotY(int index) => index * (RowHeight + RowGap);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        int slot = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            Row row = _rows[i];
            row.Tick(dt, _preview);
            if (row.Gone)
            {
                row.QueueFree();
                _rows.RemoveAt(i);
                i--;
                continue;
            }

            // In and out along x; lines close up along y once a leaving one is gone.
            float targetY = SlotY(slot++);
            float y = Mathf.MoveToward(row.Position.Y, targetY, dt * 220f);
            row.Position = new Vector2(row.SlideX, y);
        }
    }

    private sealed partial class Row : Control
    {
        public LootEntry Entry { get; private set; }
        private float _age;
        private float _leaveAge = -1f;
        private float _pulse;

        public Row(LootEntry entry)
        {
            Entry = entry;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public bool Leaving => _leaveAge >= 0f;

        public bool Gone => Leaving && _leaveAge >= SlideOut;

        public string Text => Entry.Count > 1 || Entry.Key == "gold" ? $"{Entry.Name} {CountText}" : Entry.Name;

        private string CountText => Entry.Key == "gold" ? $"+{Entry.Count:N0}" : $"×{Entry.Count:N0}";

        /// <summary>Where the line sits along x: sliding in from the left, then back out.</summary>
        public float SlideX
        {
            get
            {
                float hidden = -RowWidth - 20f;
                if (Leaving)
                {
                    float t = Mathf.Clamp(_leaveAge / SlideOut, 0f, 1f);
                    return Mathf.Lerp(0f, hidden, t * t);
                }

                float k = Mathf.Clamp(_age / SlideIn, 0f, 1f);
                return Mathf.Lerp(hidden, 0f, 1f - Mathf.Pow(1f - k, 3f));
            }
        }

        public void Merge(long count)
        {
            Entry = Entry with { Count = Entry.Count + count };
            _age = Mathf.Min(_age, SlideIn);
            _pulse = 1f;
            QueueRedraw();
        }

        public void Leave()
        {
            if (!Leaving)
            {
                _leaveAge = 0f;
            }
        }

        public void Tick(float dt, bool hold)
        {
            _age += dt;
            _pulse = Mathf.MoveToward(_pulse, 0f, dt * 3f);
            if (Leaving)
            {
                _leaveAge += dt;
            }
            else if (_age > SlideIn + Hold && !hold)
            {
                Leave();
            }

            if (_pulse > 0f)
            {
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            Font font = ThemeDB.FallbackFont;
            // A dark band fading out to the right, so the line reads on any floor.
            var band = new[] { Vector2.Zero, new Vector2(Size.X, 0f), new Vector2(Size.X, Size.Y), new Vector2(0f, Size.Y) };
            var bg = new Color(0.03f, 0.02f, 0.05f, 0.78f);
            DrawPolygon(band, new[] { bg, bg with { A = 0f }, bg with { A = 0f }, bg });
            DrawRect(new Rect2(0f, 0f, 2f, Size.Y), Entry.Color with { A = 0.9f });

            // The icon in a small frame, like the sketch.
            var frame = new Rect2(8f, 3f, Size.Y - 6f, Size.Y - 6f);
            DrawRect(frame, new Color(0.08f, 0.06f, 0.12f, 0.95f));
            if (Entry.Icon is { } icon)
            {
                DrawTextureRect(icon, frame.Grow(-3f), false);
            }

            DrawRect(frame, (_pulse > 0f ? Entry.Color.Lerp(Colors.White, _pulse) : Entry.Color) with { A = 0.9f }, false, 1.5f);

            float x = frame.End.X + 10f;
            float baseline = Size.Y / 2f + 6f;
            DrawStringOutline(font, new Vector2(x, baseline), Entry.Name, HorizontalAlignment.Left, -1, 16, 4, new Color(0f, 0f, 0f, 0.8f));
            DrawString(font, new Vector2(x, baseline), Entry.Name, HorizontalAlignment.Left, -1, 16, Entry.Color.Lightened(0.15f));
            if (Entry.Count > 1 || Entry.Key == "gold")
            {
                float w = font.GetStringSize(Entry.Name, HorizontalAlignment.Left, -1, 16).X;
                Color c = _pulse > 0f ? UiTheme.Text.Lerp(Colors.White, _pulse) : UiTheme.Text;
                DrawStringOutline(font, new Vector2(x + w + 8f, baseline), CountText, HorizontalAlignment.Left, -1, 15, 4, new Color(0f, 0f, 0f, 0.8f));
                DrawString(font, new Vector2(x + w + 8f, baseline), CountText, HorizontalAlignment.Left, -1, 15, c);
            }
        }
    }
}
