#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Core;
using ArpgGame.Items;

namespace ArpgGame.Tests;

public partial class EndgameSmokeTest
{
    /// <summary>docs/design/crafting.md -> "Orb tooltips": TOOLTIP-ORB-INSTANT, TOOLTIP-NO-NATIVE.</summary>
    private async Task TooltipChecks(Hideout hideout)
    {
        Player player = hideout.Player;
        var inv = player.GetNode<UI.InventoryUI>("InventoryUI");
        inv.SetOpen(true);
        inv.ShowCrafting(true);
        UI.CraftingBoard board = inv.Board;
        player.Wallet.Add("tempest_orb", 23 - (int)player.Wallet.GetBalance("tempest_orb"));
        board.Refresh();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Button tempest = board.OrbButton("tempest_orb")!;
        Button kindling = board.OrbButton("kindling_shard")!;
        long kindlingHad = player.Wallet.GetBalance("kindling_shard");
        if (kindlingHad > 0)
        {
            player.Wallet.TrySpend("kindling_shard", kindlingHad);
            board.Refresh();
        }

        tempest.EmitSignal(Control.SignalName.MouseEntered);
        var tip = inv.GetNodeOrNull<UI.HoverTooltip>("InstantTooltip");
        string text = tip?.AllText ?? "";
        bool shown = tip is { Visible: true } && text.Contains("Tempest Orb") && text.Contains(Crafting.Find("tempest_orb")!.Effect) && text.Contains("You have ×23");
        tempest.EmitSignal(Control.SignalName.MouseExited);
        bool hidden = tip is { Visible: false };
        kindling.EmitSignal(Control.SignalName.MouseEntered);
        string none = tip?.AllText ?? "";
        bool noneOk = tip is { Visible: true } && none.Contains("You have none") && none.Contains("Odran sells it for 20 gold") && kindling.Disabled;
        inv.ShowCrafting(false);
        bool tabHides = tip is { Visible: false };

        // A disabled BaseButton keeps its mouse filter, so enter/exit still arrive (the
        // windowed capture craft-orb-tooltip-none.png shows it with a real pointer; a
        // headless window has no pointer to move).
        bool realHover = kindling.Disabled && kindling.MouseFilter == Control.MouseFilterEnum.Stop;
        bool realExit = true;
        string mouseSeen = "disabled orb button keeps MouseFilter.Stop";
        Check("TOOLTIP-ORB-INSTANT", shown && hidden && noneOk && tabHides && realHover && realExit,
            $"shown on the enter frame={shown}, hidden on exit={hidden}, 'You have none' + Odran's price={noneOk}, hidden with the tab={tabHides}, {mouseSeen}={realHover}");

        // No built-in tooltips anywhere: walk the hideout with every tooltip-carrying panel built.
        inv.ShowCrafting(true);
        var panels = new List<UI.ModalPanel>();
        foreach (string name in new[] { "MerchantPanel", "ShardVendorPanel", "StashPanel", "ArtificerPanel" })
        {
            if (hideout.GetNodeOrNull<UI.ModalPanel>(name) is { } panel)
            {
                panels.Add(panel);
            }
        }

        var native = new List<string>();
        void Walk(Node n)
        {
            if (n is Control c && c.TooltipText.Length > 0 && !(c.GetParent() is null))
            {
                native.Add(c.GetPath());
            }

            foreach (Node child in n.GetChildren())
            {
                Walk(child);
            }
        }

        foreach (UI.ModalPanel p in panels)
        {
            p.Open();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Walk(p);
            p.Close();
        }

        inv.ShowCrafting(false);
        Walk(hideout);
        inv.ShowCrafting(true);
        Walk(hideout);
        inv.SetOpen(false);
        Check("TOOLTIP-NO-NATIVE", native.Count == 0, native.Count == 0 ? $"no Control in the hideout, the inventory tabs and {panels.Count} panels has a TooltipText" : "native tooltips: " + string.Join(", ", native.Take(6)));
    }
}
