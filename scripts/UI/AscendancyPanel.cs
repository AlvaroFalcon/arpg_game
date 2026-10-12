#nullable enable
using System.Linq;
using Godot;
using PathToExile.Characters;
using PathToExile.Core;

namespace PathToExile.UI;

/// <summary>
/// The ascendancy altar — docs/design/classes.md -> "Ascendancies". Locked
/// until the tier-5 reality boss falls (4 points; tier 12 adds 2). The first
/// visit picks one of the class's ascendancies for good; after that, points go
/// into its eight nodes. A node can be refunded for gold.
/// </summary>
public partial class AscendancyPanel : ModalPanel
{
    public Player Player { get; set; } = null!;

    protected override string Title => "Altar of Ascension";

    private VBoxContainer _content = null!;

    private CharacterData? Character => CharacterBuild.Of(Player)?.Data ?? Profile.Get(this)?.Active;

    public static long RefundCost(CharacterData c) => 50 + c.Level * 5;

    protected override void Build(VBoxContainer body)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(940f, 540f), SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        _content = UiTheme.VBox(scroll, 10);
        _content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    }

    public override void Refresh()
    {
        ClearChildren(_content);
        if (Character is not { } c)
        {
            return;
        }

        if (c.AscendancyPoints <= 0)
        {
            _content.AddChild(UiTheme.Label("The altar is dormant.", 24, UiTheme.TextDim));
            _content.AddChild(Wrap(UiTheme.Label($"Defeat the boss of a tier {AscendancyCatalog.Tier1Unlock} (or higher) Altered Reality to earn your first {AscendancyCatalog.FirstPoints} ascendancy points. A tier {AscendancyCatalog.Tier2Unlock} boss grants {AscendancyCatalog.SecondPoints} more.", 17, UiTheme.Text)));
            AddChoices(c, false);
            return;
        }

        if (c.AscendancyDef is null)
        {
            _content.AddChild(UiTheme.Label($"Choose your path, {c.Class.Name}. This choice is permanent.", 22, UiTheme.Gold));
            AddChoices(c, true);
            return;
        }

        AscendancyDefinition def = c.AscendancyDef;
        _content.AddChild(UiTheme.Label($"{def.Name} — {c.AscendancyPointsFree} of {c.AscendancyPoints} points free", 22, UiTheme.Gold));
        _content.AddChild(Wrap(UiTheme.Label($"{def.Tagline}  Refunding a node costs {RefundCost(c)} gold.", 15, UiTheme.TextDim)));
        foreach (AscendancyNode node in AscendancyCatalog.For(def.Id))
        {
            bool taken = c.AscendancyNodes.Contains(node.Id);
            HBoxContainer row = UiTheme.HBox(_content, 10);
            Label text = UiTheme.Label($"{(node.Notable ? "◆ " : "• ")}{node.Name}: {node.Description}", 16, taken ? UiTheme.Gold : node.Notable ? new Color(0.8f, 0.6f, 1f) : UiTheme.Text);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            text.CustomMinimumSize = new Vector2(720f, 0f);
            row.AddChild(text);
            string id = node.Id;
            if (taken)
            {
                Button refund = UiTheme.Button("Refund", () => Refund(id), 15, 140f);
                refund.Disabled = Player.Wallet.GetBalance(Profile.GoldId) < RefundCost(c);
                row.AddChild(refund);
            }
            else
            {
                Button take = UiTheme.Button("Allocate", () => Allocate(id), 15, 140f);
                take.Disabled = c.AscendancyPointsFree <= 0;
                row.AddChild(take);
            }
        }
    }

    private static Label Wrap(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(900f, 0f);
        return label;
    }

    private void AddChoices(CharacterData c, bool selectable)
    {
        foreach (string id in c.Class.Ascendancies)
        {
            AscendancyDefinition? def = ClassCatalog.GetAscendancy(id);
            if (def is null)
            {
                continue;
            }

            var card = new PanelContainer();
            _content.AddChild(card);
            VBoxContainer v = UiTheme.VBox(card, 4);
            HBoxContainer head = UiTheme.HBox(v, 12);
            head.AddChild(UiTheme.Portrait(def.Sprite, 80f));

            VBoxContainer titles = UiTheme.VBox(head, 2);
            titles.AddChild(UiTheme.Label(def.Name, 22, UiTheme.Gold));
            titles.AddChild(UiTheme.Label(def.Tagline, 15, UiTheme.Text));
            string notables = string.Join("  ·  ", AscendancyCatalog.For(id).Where(n => n.Notable).Select(n => n.Name));
            v.AddChild(Wrap(UiTheme.Label($"Notables: {notables}", 14, UiTheme.TextDim)));
            if (selectable)
            {
                v.AddChild(UiTheme.Button($"Become a {def.Name}", () => Choose(id), 17, 300f));
            }
        }
    }

    private void Choose(string id)
    {
        if (Character is not { } c || c.Ascendancy is not null)
        {
            return;
        }

        c.Ascendancy = id;
        Sfx.Play(this, "victory", -6f);
        Changed();
    }

    private void Allocate(string id)
    {
        if (Character is { } c && c.AscendancyPointsFree > 0 && c.AscendancyNodes.Add(id))
        {
            Sfx.Play(this, "levelup", -8f);
            Changed();
        }
    }

    private void Refund(string id)
    {
        if (Character is { } c && c.AscendancyNodes.Contains(id) && Player.Wallet.TrySpend(Profile.GoldId, RefundCost(c)))
        {
            c.AscendancyNodes.Remove(id);
            Changed();
        }
    }

    private void Changed()
    {
        CharacterBuild.Of(Player)?.Rebuild();
        Refresh();
    }
}
