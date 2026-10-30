#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Core;
using ArpgGame.Progression;

namespace ArpgGame.Tests;

public partial class EndgameSmokeTest
{
    private static void SetGold(Player player, long gold)
    {
        player.Wallet.TrySpend(Profile.GoldId, player.Wallet.GetBalance(Profile.GoldId));
        player.Wallet.Add(Profile.GoldId, gold);
    }

    /// <summary>docs/design/hideout.md -> "Odran's Skill gems tab" (GEMSHOP-*) and the altar's confirm (ALTAR-CONFIRM).</summary>
    private async Task GemShopChecks(Hideout hideout)
    {
        // The catalogue is covered exactly once, at the plan's prices.
        var skillIds = UpgradeCatalog.Skills.Select(s => s.Id).ToList();
        var supportIds = UpgradeCatalog.Supports.Select(s => s.Id).ToList();
        bool covered = skillIds.All(id => GemShop.Offers.Count(o => !o.Support && o.Id == id) == 1)
            && supportIds.All(id => GemShop.Offers.Count(o => o.Support && o.Id == id) == 1)
            && GemShop.Offers.All(o => o.Support ? supportIds.Contains(o.Id) : skillIds.Contains(o.Id))
            && GemShop.Offers.Count(o => !o.Support) == 16 && GemShop.Offers.Count(o => o.Support) == 20;
        bool prices = GemShop.Find("spark", false)!.Price == 200 && GemShop.Find("meteor", false)!.Price == 800 && GemShop.Find("fireball", false)!.Price == 50
            && GemShop.Find("pierce", true)!.Price == 100 && GemShop.Find("brutality", true)!.Price == 580 && GemShop.Find("spell_echo", true)!.Price == 60;
        Check("GEMSHOP-COVERS-CATALOG", covered && prices, $"{GemShop.Offers.Count(o => !o.Support)} skills and {GemShop.Offers.Count(o => o.Support)} supports, each on sale once; price table checked");

        Player player = hideout.Player;
        Profile? profile = Profile.Get(this);
        int activeBefore = profile?.ActiveIndex ?? 0;
        CharacterData c = profile is not null ? profile.CreateCharacter("GemBuyer", "arcanist") : CharacterData.Create("GemBuyer", "arcanist");
        c.Level = 3;
        c.GemShopSeenLevel = 3;
        var merchant = hideout.GetNode<UI.MerchantPanel>("MerchantPanel");
        merchant.Character = c;
        var bought = new List<(string Key, int Value)>();
        void OnEvent(string id, int value, string key)
        {
            if (id == GameEvents.GemBought)
            {
                bought.Add((key, value));
            }
        }

        GameEvents.Raised += OnEvent;
        SetGold(player, 500);
        merchant.Open();
        merchant.ShowTab(UI.MerchantTab.SkillGems);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        UI.GemShopView view = merchant.Gems;

        // Locked: a level-3 character sees Meteor greyed out and can't buy it.
        view.Select(GemShop.Find("meteor", false)!);
        bool locked = view.BuyButton.Disabled && view.ReasonText == "Unlocks at character level 16" && view.RowLocked("meteor");
        Check("GEMSHOP-LOCKED", locked, $"Meteor row locked={view.RowLocked("meteor")}, Buy disabled={view.BuyButton.Disabled}, reason '{view.ReasonText}'");

        // Buying: wallet down, a level-1 gem, the row flips to Owned, the event fires.
        c.Level = 4;
        view.Rebuild();
        view.Select(GemShop.Find("spark", false)!);
        int gemsBefore = c.Gems.Count;
        bool did = view.Buy();
        GemData? spark = c.Gems.FirstOrDefault(g => g.Id == "spark" && !g.IsSupport);
        bool ok = did && player.Wallet.GetBalance(Profile.GoldId) == 300 && spark is { Level: 1 } && c.Gems.Count == gemsBefore + 1
            && view.RowStateText("spark").Contains("Owned") && bought.SequenceEqual(new[] { ("spark", 0) }) && view.OpenSkillsButton is { Visible: true }
            && view.ReasonText.StartsWith("Bought Spark");
        Check("GEMSHOP-BUY-SKILL", ok, $"bought={did}, gold {player.Wallet.GetBalance(Profile.GoldId)} (want 300), gem level {spark?.Level}, row '{view.RowStateText("spark")}', events {string.Join(",", bought.Select(b => b.Key))}");

        // A second copy of a skill is refused, the wallet untouched.
        bool again = view.Buy();
        Check("GEMSHOP-NO-DUPLICATE-SKILL", !again && player.Wallet.GetBalance(Profile.GoldId) == 300 && c.Gems.Count(g => g.Id == "spark") == 1
            && GemShop.CannotBuy(c, player.Wallet, GemShop.Find("spark", false)!)!.StartsWith("You have this skill"),
            $"second purchase refused={!again}, gold {player.Wallet.GetBalance(Profile.GoldId)}");

        // Supports stack up to the loadout slots (four); the next is refused.
        int cap = GemLoot.MaxSupportCopies;
        c.Level = 14;
        SetGold(player, 5000);
        view.SetKind(true);
        view.Select(GemShop.Find("pierce", true)!);
        int bought6 = 0;
        for (int i = 0; i < cap; i++)
        {
            if (view.Buy())
            {
                bought6++;
            }
        }

        long goldAt6 = player.Wallet.GetBalance(Profile.GoldId);
        bool seventh = view.Buy();
        Check("GEMSHOP-SUPPORT-CAP", bought6 == cap && !seventh && player.Wallet.GetBalance(Profile.GoldId) == goldAt6 && view.ReasonText == $"You have {cap} of {cap}, one per loadout slot"
            && view.BuyButton.Disabled && c.Gems.Count(g => g.Id == "pierce" && g.IsSupport) == cap,
            $"bought {bought6} of {cap}, next refused={!seventh}, reason '{view.ReasonText}'");

        // Not enough gold says how much more.
        c.Level = 6;
        SetGold(player, 160);
        view.SetKind(false);
        view.Select(GemShop.Find("frost_shards", false)!);
        Check("GEMSHOP-NOT-ENOUGH-GOLD", view.BuyButton.Disabled && view.ReasonText == "Not enough gold: 140 more", $"reason '{view.ReasonText}'");

        // "Fits my skills" for an Arcanist that owns Fireball and Spark: Pierce and Spell Echo yes, Multistrike no.
        var fireballOnly = CharacterData.Create("Solo", "arcanist");
        fireballOnly.Level = 14;
        merchant.Character = fireballOnly;
        view.SetKind(true);
        view.SetFilter("fits");
        var fits = view.Offered.Select(o => o.Id).ToList();
        Check("GEMSHOP-FITS-FILTER", fits.Contains("pierce") && fits.Contains("spell_echo") && !fits.Contains("multistrike") && !fits.Contains("extended_duration"),
            $"fits a Fireball-only character: {string.Join(", ", fits)}");
        merchant.Character = c;

        // The NEW dot: levelling past an unlock shows it, opening the tab clears it.
        merchant.Close();
        c.Level = 14;
        c.GemShopSeenLevel = 14;
        merchant.Open();
        bool quiet = !merchant.GemsTabDot;
        merchant.Close();
        c.Level = 16;
        merchant.Open();
        bool dot = merchant.GemsTabDot;
        merchant.ShowTab(UI.MerchantTab.SkillGems);
        bool cleared = !merchant.GemsTabDot && c.GemShopSeenLevel == 16;
        view.SetKind(false);
        bool newTag = view.RowStateText("meteor").Contains("NEW");
        Check("GEMSHOP-NEW-DOT", quiet && dot && cleared && newTag, $"no dot when seen={quiet}, dot after levelling to 16={dot}, cleared by opening={cleared}, NEW tag on Meteor={newTag}");

        // Saved: closing the panel captures the profile, which keeps the gem.
        SetGold(player, 1000);
        view.SetKind(false);
        view.Select(GemShop.Find("meteor", false)!);
        view.Buy();
        merchant.Close();
        bool saved = false;
        if (profile is not null)
        {
            var reread = new Profile { Persist = false };
            reread.LoadJson(profile.ToJson());
            CharacterData? back = reread.Characters.FirstOrDefault(k => k.Name == "GemBuyer");
            saved = back is not null && back.Gems.Any(g => g.Id == "meteor" && !g.IsSupport && g.Level == 1) && back.Gems.Count(g => g.Id == "pierce" && g.IsSupport) == cap
                && back.GemShopSeenLevel == 16 && profile.Active.Gold == player.Wallet.GetBalance(Profile.GoldId);
            reread.Free();
        }

        Check("GEMSHOP-SAVED", saved, $"profile JSON keeps the bought Meteor, {cap} Pierce and the seen level (profile present={profile is not null})");
        GameEvents.Raised -= OnEvent;
        merchant.Character = null;

        // The altar needs two clicks (ALTAR-CONFIRM): the first arms, a second within 3 s chooses.
        CharacterData ac = CharacterBuild.Of(player)?.Data ?? c;
        string? ascBefore = ac.Ascendancy;
        int pointsBefore = ac.AscendancyPoints;
        ac.Ascendancy = null;
        ac.AscendancyPoints = 4;
        var altar = hideout.GetNode<UI.AscendancyPanel>("AscendancyPanel");
        var chosenEvents = new List<string>();
        void OnAscend(string id, int value, string key)
        {
            if (id == GameEvents.AscendancyChosen)
            {
                chosenEvents.Add(key);
            }
        }

        GameEvents.Raised += OnAscend;
        altar.Open();
        bool firstClick = altar.Choose("witch");
        bool armed = !firstClick && ac.Ascendancy is null && altar.PendingChoice == "witch";
        bool other = !altar.Choose("elementalist") && ac.Ascendancy is null && altar.PendingChoice == "elementalist";
        bool timedOut = false;
        string expiryNote = "";
        if (!(ac.Ascendancy is not null))
        {
            ulong t0 = Time.GetTicksMsec();
            double wait = UI.AscendancyPanel.ConfirmSeconds + 0.4;
            while (Time.GetTicksMsec() - t0 < wait * 1000.0)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            timedOut = altar.PendingChoice is null && ac.Ascendancy is null;
            expiryNote = $"waited {Time.GetTicksMsec() - t0} ms, pending={altar.PendingChoice}, open={altar.IsOpen}, ascendancy={ac.Ascendancy}, tree paused={GetTree().Paused}";
        }

        altar.Choose("witch");
        bool chosen = altar.Choose("witch") && ac.Ascendancy == "witch" && chosenEvents.SequenceEqual(new[] { "witch" });
        altar.Close();
        GameEvents.Raised -= OnAscend;
        Check("ALTAR-CONFIRM", armed && other && timedOut && chosen, $"first click arms without choosing={armed}, another card re-arms={other}, expires after {UI.AscendancyPanel.ConfirmSeconds} s={timedOut} ({expiryNote}), second click chooses={chosen}");

        // Put the account back as it was.
        ac.Ascendancy = ascBefore;
        ac.AscendancyPoints = pointsBefore;
        ac.AscendancyNodes.Clear();
        if (profile is not null)
        {
            profile.Characters.Remove(c);
            profile.Select(activeBefore);
        }
    }
}
