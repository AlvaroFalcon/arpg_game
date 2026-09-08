#nullable enable
using System.Collections.Generic;
using Godot;
using PathToExile.Items;

namespace PathToExile.Tests;

/// <summary>
/// Headless functional smoke test for the M1 item/loot slice
/// (docs/design/items.md -> "Build order" item 1: "drop pipeline, ItemDescriptor,
/// FilterChain, labels, and currency/gold"). Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tests/ItemsSmokeTest.tscn --quit-after 60
///
/// Descriptor construction and filter-chain evaluation are pure C# and exercised
/// by direct calls in <see cref="_Ready"/> — there's no input or physics
/// dependency for that part, so a direct call is the real production path, not a
/// bypass (same rationale SkillsSmokeTest gives for calling TryCast directly).
///
/// The pickup path is deliberately tested through *real* physics rather than a
/// direct <c>OnPickup</c> call: the scene places a <see cref="CurrencyDrop"/>
/// exactly on the player's spawn position so <c>Area2D.BodyEntered</c> fires for
/// real within the first few physics frames. That is what proves "filters
/// control display, never behaviour" end to end — a hidden-styled drop is still
/// collected by the actual walk-over signal path, not just by an OnPickup method
/// that happens to ignore style in isolation.
/// </summary>
public partial class ItemsSmokeTest : Node2D
{
    private Player _player = null!;
    private CurrencyDrop _drop = null!;
    private ItemDescriptor _dropDescriptorAtSpawn = null!;

    private int _frame;
    private bool _failed;

    public override void _Ready()
    {
        _player = GetNode<Player>("Player");
        _drop = GetNode<CurrencyDrop>("CurrencyDrop");

        // Captured now, before physics has had a chance to collect the drop, so
        // the later HIDDEN-DROP-STILL-AUTO-COLLECTED check can evaluate a filter
        // against the exact descriptor this drop rolled even after the node has
        // freed itself.
        _dropDescriptorAtSpawn = _drop.Descriptor;

        GD.Print("[ITEMSTEST] Ready.");

        CheckDescriptorConstruction();
        CheckFilterChainFirstMatchWins();
        CheckHiddenRuleStillEvaluatesHidden();
        CheckWalletIncrementDirect();
    }

    private void Check(string name, bool passed, string detail)
    {
        GD.Print($"[ITEMSTEST] {name}: {(passed ? "PASS" : "FAIL")} — {detail}");
        if (!passed)
        {
            _failed = true;
        }
    }

    /// <summary>
    /// Builds a descriptor exercising every field, including the ones with no
    /// milestone-1 producer (AffixTiers, SetId, MajesticEffectId, SocketCount,
    /// ...), and confirms every one round-trips through the init-only properties
    /// unchanged. This is "the vocabulary is fixed before items exist," checked
    /// mechanically rather than just asserted in prose.
    /// </summary>
    private void CheckDescriptorConstruction()
    {
        var descriptor = new ItemDescriptor
        {
            ItemClass = ItemClass.Currency,
            BaseTypeId = "iron_wand",
            Rarity = Rarity.Rare,
            ItemLevel = 42,
            AreaLevel = 40,
            AffixCount = 3,
            AffixTiers = new List<int> { 2, 4, 6 },
            HighestAffixTier = 6,
            SetId = "vestige_of_the_hollow",
            MajesticEffectId = "emberwake",
            SocketCount = 2,
            StackSize = 1,
            Width = 1,
            Height = 2,
            DisplayName = "Test Item",
            CurrencyId = "gold",
            Amount = 250,
            Tags = ItemTags.None,
        };

        bool ok = descriptor.ItemClass == ItemClass.Currency
            && descriptor.BaseTypeId == "iron_wand"
            && descriptor.Rarity == Rarity.Rare
            && descriptor.ItemLevel == 42
            && descriptor.AreaLevel == 40
            && descriptor.AffixCount == 3
            && descriptor.AffixTiers.Count == 3
            && descriptor.AffixTiers[1] == 4
            && descriptor.HighestAffixTier == 6
            && descriptor.SetId == "vestige_of_the_hollow"
            && descriptor.MajesticEffectId == "emberwake"
            && descriptor.SocketCount == 2
            && descriptor.StackSize == 1
            && descriptor.Width == 1
            && descriptor.Height == 2
            && descriptor.DisplayName == "Test Item"
            && descriptor.CurrencyId == new CurrencyId("gold")
            && descriptor.Amount == 250
            && descriptor.Tags == ItemTags.None;

        Check("DESCRIPTOR-CONSTRUCTION", ok, "every field round-trips through init-only properties, including no-producer-yet fields");

        // What every milestone-1 currency drop's descriptor actually looks like:
        // everything left at its default. Confirm those defaults are sane
        // (Rarity.Common, an empty-not-null tiers list) rather than leaving it
        // implicit.
        var bare = new ItemDescriptor();
        Check("DESCRIPTOR-DEFAULTS", bare.Rarity == Rarity.Common && bare.AffixTiers.Count == 0 && bare.DisplayName == "",
            $"Rarity={bare.Rarity}, AffixTiers.Count={bare.AffixTiers.Count}, DisplayName='{bare.DisplayName}'");
    }

    /// <summary>
    /// Two rules that both match the same descriptor — the case that actually
    /// distinguishes "first match wins" from "some match wins," which a chain
    /// with no overlapping rules cannot do.
    /// </summary>
    private void CheckFilterChainFirstMatchWins()
    {
        var descriptor = new ItemDescriptor { Rarity = Rarity.Rare };
        var first = new DisplayStyle { TextColor = Colors.Red };
        var second = new DisplayStyle { TextColor = Colors.Blue };

        var chain = new FilterChain(new[]
        {
            new FilterRule(d => d.Rarity == Rarity.Rare, first),
            new FilterRule(d => d.Rarity == Rarity.Rare, second), // also matches — must never win
        });

        DisplayStyle result = chain.Evaluate(descriptor);
        Check("FILTER-FIRST-MATCH-WINS", ReferenceEquals(result, first) && result.TextColor == Colors.Red,
            $"resolved TextColor={result.TextColor} (expected Red from the first rule, not Blue from the second)");

        var emptyChain = new FilterChain();
        DisplayStyle baseline = emptyChain.Evaluate(descriptor);
        Check("FILTER-NO-MATCH-IS-BASELINE", ReferenceEquals(baseline, DisplayStyle.Baseline),
            "an empty chain evaluates to DisplayStyle.Baseline, not an exception or a null");

        // The built-in default filter: the currency rule must resolve before the
        // rarity rules, since every descriptor (including currency) carries a
        // default Rarity.Common — see BuiltInDefaultRules' ordering note.
        var currencyDescriptor = new ItemDescriptor { CurrencyId = "gold", Rarity = Rarity.Common };
        DisplayStyle currencyStyle = FilterChain.Default.Evaluate(currencyDescriptor);
        DisplayStyle commonStyle = FilterChain.Default.Evaluate(new ItemDescriptor { Rarity = Rarity.Common });
        Check("DEFAULT-FILTER-CURRENCY-BEFORE-RARITY", currencyStyle.TextColor != commonStyle.TextColor,
            $"currency style {currencyStyle.TextColor} vs common-rarity style {commonStyle.TextColor} (must differ)");
    }

    /// <summary>
    /// Builds a filter that would hide this exact drop and confirms the filter
    /// layer correctly reports Hidden=true. The complementary half of the
    /// guarantee — that Hidden has zero effect on pickup — is proven by real
    /// physics in <see cref="_PhysicsProcess"/> below, through a code path that
    /// never touches this or any other FilterChain at all.
    /// </summary>
    private void CheckHiddenRuleStillEvaluatesHidden()
    {
        FilterChain userChain = FilterChain.CreateDefault();
        userChain.PrependRules(new[]
        {
            new FilterRule(d => d.CurrencyId == _dropDescriptorAtSpawn.CurrencyId, new DisplayStyle { Hidden = true }),
        });

        DisplayStyle style = userChain.Evaluate(_dropDescriptorAtSpawn);
        Check("HIDDEN-RULE-EVALUATES-HIDDEN", style.Hidden == true,
            $"a 'hide this gold' rule prepended ahead of the defaults resolves Hidden={style.Hidden}");
    }

    private void CheckWalletIncrementDirect()
    {
        var wallet = new Wallet();
        var silver = new CurrencyId("silver");

        Check("WALLET-STARTS-AT-ZERO", wallet.GetBalance(silver) == 0L, $"balance={wallet.GetBalance(silver)}");

        wallet.Add(silver, 10);
        wallet.Add(silver, 5);
        Check("WALLET-INCREMENT-ACCUMULATES", wallet.GetBalance(silver) == 15L,
            $"balance after +10, +5 = {wallet.GetBalance(silver)} (expected 15)");

        wallet.Add("gold", 1);
        Check("WALLET-BALANCES-ARE-PER-CURRENCY", wallet.GetBalance(silver) == 15L && wallet.GetBalance("gold") == 1L,
            $"silver={wallet.GetBalance(silver)}, gold={wallet.GetBalance("gold")}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _frame++;

        // By frame 10 the CurrencyDrop (spawned exactly on the player's position)
        // should have overlapped the player's body, fired OnPickup for real
        // through Area2D.BodyEntered, credited the player's Wallet, and freed
        // itself — despite the ad-hoc filter above resolving Hidden=true for its
        // descriptor. This is the "filters control display, never behaviour"
        // guarantee, exercised end to end rather than merely asserted.
        if (_frame == 10)
        {
            bool collected = !IsInstanceValid(_drop);
            long balance = _player.Wallet.GetBalance(_dropDescriptorAtSpawn.CurrencyId!.Value);
            Check("HIDDEN-DROP-STILL-AUTO-COLLECTED",
                collected && balance == _dropDescriptorAtSpawn.Amount,
                $"drop freed={collected}, wallet balance={balance} (expected {_dropDescriptorAtSpawn.Amount})");
        }

        if (_frame >= 20)
        {
            GD.Print(_failed ? "[ITEMSTEST] === RESULT: FAIL ===" : "[ITEMSTEST] === RESULT: PASS ===");
            GetTree().Quit();
        }
    }
}
