#nullable enable
using System.Collections.Generic;
using System.Linq;
using Godot;
using PathToExile.Characters;
using PathToExile.Core;
using PathToExile.Progression;
using PathToExile.Skills;
using LoadoutSlot = PathToExile.Characters.LoadoutSlot;

namespace PathToExile.UI;

/// <summary>
/// The skills panel (K) — docs/design/classes.md -> "Gems as items". Four
/// loadout slots, each a skill gem plus support sockets (2, +1 at levels 12
/// and 28). Click a slot or socket to target it, then click a gem from the
/// collection below to socket it there; a gem already socketed elsewhere moves.
/// Right-click a slot or socket to empty it. Supports only list when they fit
/// the targeted skill.
/// </summary>
public partial class SkillsPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    protected override string Title => "Skills & Supports";

    private VBoxContainer _slots = null!;
    private Label _hint = null!;
    private HFlowContainer _gems = null!;
    private Label _detail = null!;
    private int _targetSlot;
    private int _targetSocket = -1;

    private CharacterData? Character => CharacterBuild.Of(Player)?.Data ?? Profile.Get(this)?.Active;

    protected override void Build(VBoxContainer body)
    {
        _slots = UiTheme.VBox(body, 6);
        _hint = UiTheme.Label("", 15, UiTheme.TextDim);
        body.AddChild(_hint);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(940f, 200f), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        _gems = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _gems.AddThemeConstantOverride("h_separation", 6);
        _gems.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_gems);
        _detail = UiTheme.Label("", 15, UiTheme.Text);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.CustomMinimumSize = new Vector2(940f, 44f);
        body.AddChild(_detail);
    }

    public static string GemName(GemData g) => GemLoot.NameOf(g.Id, g.IsSupport);

    private static SkillDefinition? SkillOf(GemData? g) => g is { IsSupport: false } ? UpgradeCatalog.FindSkill(g.Id)?.Definition : null;

    private static SupportDefinition? SupportOf(GemData? g) => g is { IsSupport: true } ? UpgradeCatalog.Supports.FirstOrDefault(s => s.Id == g.Id) : null;

    public override void Refresh()
    {
        if (Character is not { } c)
        {
            return;
        }

        ClearChildren(_slots);
        for (int i = 0; i < CharacterData.LoadoutSlots; i++)
        {
            int slotIndex = i;
            LoadoutSlot slot = c.Loadout[i];
            GemData? skill = c.FindGem(slot.SkillUid);
            HBoxContainer row = UiTheme.HBox(_slots, 6);
            row.AddChild(UiTheme.Label($"{i + 1}", 22, UiTheme.Gold));
            Button skillButton = GemButton(skill is null ? "— empty skill —" : $"{GemName(skill)}  Lv{skill.Level}", skill, _targetSlot == i && _targetSocket < 0, 260f);
            skillButton.Pressed += () => Target(slotIndex, -1);
            skillButton.GuiInput += e => RightClickClear(e, slotIndex, -1);
            row.AddChild(skillButton);

            for (int s = 0; s < c.SupportSockets; s++)
            {
                int socket = s;
                GemData? sup = s < slot.SupportUids.Count ? c.FindGem(slot.SupportUids[s]) : null;
                Button b = GemButton(sup is null ? "+ support" : GemName(sup), sup, _targetSlot == i && _targetSocket == s, 160f);
                b.Disabled = skill is null;
                b.Pressed += () => Target(slotIndex, socket);
                b.GuiInput += e => RightClickClear(e, slotIndex, socket);
                row.AddChild(b);
            }
        }

        LoadoutSlot target = c.Loadout[_targetSlot];
        SkillDefinition? targetSkill = SkillOf(c.FindGem(target.SkillUid));
        bool supports = _targetSocket >= 0 && targetSkill is not null;
        _hint.Text = supports
            ? $"Socketing a support into slot {_targetSlot + 1} ({targetSkill!.DisplayName}) — only supports that fit it are listed. Right-click a socket to empty it."
            : $"Choosing the skill for slot {_targetSlot + 1}. Skill gems level with the kills they make; supports never level. Right-click to empty.";

        ClearChildren(_gems);
        IEnumerable<GemData> list = c.Gems.Where(g => g.IsSupport == supports).OrderBy(g => GemName(g)).ThenByDescending(g => g.Level);
        foreach (GemData g in list)
        {
            if (supports && SupportOf(g) is { } def && !def.Accepts(targetSkill!))
            {
                continue;
            }

            string where = WhereSocketed(c, g.Uid);
            string text = g.IsSupport ? GemName(g) : $"{GemName(g)} Lv{g.Level}";
            Button b = GemButton(where.Length > 0 ? $"{text}  [{where}]" : text, g, false, 0f);
            b.Pressed += () => Socket(g);
            _gems.AddChild(b);
        }

        if (_gems.GetChildCount() == 0)
        {
            _gems.AddChild(UiTheme.Label(supports ? "No support gems that fit this skill yet — rare monsters and bosses drop them." : "No skill gems.", 16, UiTheme.TextDim));
        }
    }

    private Button GemButton(string text, GemData? gem, bool selected, float width)
    {
        Button b = UiTheme.Button(text, () => { }, 15, width);
        b.CustomMinimumSize = new Vector2(width, 40f);
        Color color = gem is null ? UiTheme.TextDim : gem.IsSupport ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.65f, 0.4f);
        b.AddThemeColorOverride("font_color", color);
        if (selected)
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.24f, 0.18f, 0.32f), UiTheme.Gold, 2));
        }

        b.MouseEntered += () => _detail.Text = Describe(gem);
        return b;
    }

    private static string Describe(GemData? gem)
    {
        if (gem is null)
        {
            return "";
        }

        if (SupportOf(gem) is { } sup)
        {
            return $"{sup.Name} (support): {sup.Description.Replace('\n', ' ')}";
        }

        if (SkillOf(gem) is { } skill)
        {
            string xp = gem.Level >= GemData.MaxLevel ? "max level" : $"{gem.Experience:0}/{GemData.ExperienceToNext(gem.Level):0} xp";
            return $"{skill.DisplayName} Lv{gem.Level} ({xp}) — {skill.Tags}: {skill.Description.Replace('\n', ' ')}";
        }

        return gem.Id;
    }

    private static string WhereSocketed(CharacterData c, string uid)
    {
        for (int i = 0; i < c.Loadout.Length; i++)
        {
            if (c.Loadout[i].SkillUid == uid || c.Loadout[i].SupportUids.Contains(uid))
            {
                return $"slot {i + 1}";
            }
        }

        return "";
    }

    private void Target(int slot, int socket)
    {
        _targetSlot = slot;
        _targetSocket = socket;
        Refresh();
    }

    private void RightClickClear(InputEvent e, int slot, int socket)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } || Character is not { } c)
        {
            return;
        }

        LoadoutSlot s = c.Loadout[slot];
        if (socket < 0)
        {
            s.SkillUid = null;
            s.SupportUids.Clear();
        }
        else if (socket < s.SupportUids.Count)
        {
            s.SupportUids.RemoveAt(socket);
        }

        Changed();
    }

    private void Socket(GemData gem)
    {
        if (Character is not { } c)
        {
            return;
        }

        // A gem lives in one place: take it out of wherever it is first.
        foreach (LoadoutSlot s in c.Loadout)
        {
            if (s.SkillUid == gem.Uid)
            {
                s.SkillUid = null;
            }

            s.SupportUids.Remove(gem.Uid);
        }

        LoadoutSlot target = c.Loadout[_targetSlot];
        if (!gem.IsSupport)
        {
            target.SkillUid = gem.Uid;
            // Supports that no longer fit fall out.
            SkillDefinition? skill = SkillOf(gem);
            target.SupportUids.RemoveAll(uid => skill is null || SupportOf(c.FindGem(uid)) is not { } d || !d.Accepts(skill));
        }
        else if (target.SkillUid is not null)
        {
            // One copy of each support per skill.
            target.SupportUids.RemoveAll(uid => c.FindGem(uid)?.Id == gem.Id);
            if (_targetSocket < target.SupportUids.Count)
            {
                target.SupportUids.RemoveAt(_targetSocket);
                target.SupportUids.Insert(_targetSocket, gem.Uid);
            }
            else
            {
                target.SupportUids.Add(gem.Uid);
            }

            while (target.SupportUids.Count > c.SupportSockets)
            {
                target.SupportUids.RemoveAt(target.SupportUids.Count - 1);
            }

            _targetSocket = Mathf.Min(_targetSocket + 1, c.SupportSockets - 1);
        }

        Sfx.Play(this, "ui");
        Changed();
    }

    private void Changed()
    {
        CharacterBuild.Of(Player)?.EquipLoadout();
        Refresh();
    }
}
