#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using ArpgGame.Characters;
using ArpgGame.Progression;
using ArpgGame.Skills;

namespace ArpgGame.Tools;

/// <summary>Writes every skill and its upgrade tree as two tab-separated tables to
/// user://skill_trees_skills.tsv and user://skill_trees_nodes.tsv (for the owner's editable sheet),
/// and as user://skill_trees.json — the data of the visual editor (tools/skill-tree-editor/,
/// docs/design/skills.md -> "The skill tree editor").</summary>
public partial class DumpSkillTrees : Node
{
    private static CastContext BaseContext(SkillDefinition d)
    {
        var c = new CastContext(null!, null!, Combat.Faction.Player, Vector2.Zero, Vector2.Right) { Tags = d.Tags };
        if (d is ProjectileSkillDefinition p)
        {
            c.Projectile = ProjectileParams.FromDefinition(p);
        }

        if (d is AuraSkillDefinition a)
        {
            c.Aura = new AuraParams();
            AuraCatalog.Seed(a.Kind, c.Aura);
        }

        return c;
    }

    private static Dictionary<string, float> Snapshot(CastContext c) => new()
    {
        ["damage"] = c.DamageMultiplier, ["cost"] = c.CostMultiplier, ["cooldown"] = c.CooldownMultiplier, ["useTime"] = c.UseTimeMultiplier,
        ["area"] = c.AreaMultiplier, ["duration"] = c.DurationMultiplier, ["range"] = c.RangeMultiplier, ["igniteDamage"] = c.IgniteDamageMultiplier,
        ["extraHits"] = c.ExtraCount + (c.Projectile?.Count ?? 0), ["repeats"] = c.Repeats, ["totems"] = c.ExtraTotems,
        ["ignite"] = c.IgniteChanceBonus, ["shock"] = c.ShockChanceBonus, ["freeze"] = c.FreezeChanceBonus, ["bleed"] = c.BleedChance,
        ["pierce"] = c.Projectile?.Pierce ?? 0, ["chains"] = c.Projectile?.Chains ?? 0, ["forks"] = c.Projectile?.Forks ?? 0, ["bounces"] = c.Projectile?.Bounces ?? 0,
        ["projectileSpeed"] = c.Projectile?.Speed ?? 1f, ["projectileRange"] = c.Projectile?.Range ?? 1f,
        ["burst"] = c.Projectile is { ExplodeRadius: > 0f } ? 1f : 0f, ["burstShare"] = c.Projectile?.ExplodeDamageFraction ?? 0f, ["killBlast"] = c.KillBlastFraction,
        ["aftershocks"] = c.Aftershocks, ["knockback"] = c.ExtraKnockback, ["invulnerable"] = c.ExtraInvulnerability, ["oilSlow"] = c.OilSlow,
        ["splash"] = c.DetonationSplash, ["stacksKept"] = c.StacksKept, ["heal"] = c.Aura?.HealFraction ?? 0f, ["drain"] = c.Aura?.LifeDrainMultiplier ?? 1f,
        ["coldShare"] = c.ColdConversion, ["charges"] = c.ExtraCharges, ["rewind"] = c.RewindWindow,
        ["passive"] = c.Aura?.PassiveMultiplier ?? 1f, ["active"] = c.Aura?.ActiveMultiplier ?? 1f, ["potency"] = c.AfflictionPotency + c.CursePotencyBonus,
    };

    /// <summary>What one rank of a node does to a cast, as numbers the editor's analysis combines:
    /// multipliers as factors, counts and chances as sums, bursts as a floor (the code takes the max).</summary>
    private static Godot.Collections.Dictionary EffectsOf(SkillDefinition d, SkillTreeNode n, int rank)
    {
        CastContext before = BaseContext(d);
        Dictionary<string, float> a = Snapshot(before);
        CastContext after = BaseContext(d);
        n.Apply(after, rank);
        Dictionary<string, float> b = Snapshot(after);
        var mult = new HashSet<string> { "damage", "cost", "cooldown", "useTime", "area", "duration", "range", "igniteDamage", "projectileSpeed", "projectileRange", "passive", "active", "drain" };
        var max = new HashSet<string> { "burst", "burstShare", "killBlast", "oilSlow", "splash", "stacksKept" };
        var fx = new Godot.Collections.Dictionary();
        foreach ((string k, float v0) in a)
        {
            float v1 = b[k];
            if (Mathf.IsEqualApprox(v0, v1))
            {
                continue;
            }

            fx[k] = mult.Contains(k) ? (v0 != 0f ? v1 / v0 : v1) : max.Contains(k) ? v1 : v1 - v0;
        }

        if (after.Tags != before.Tags)
        {
            fx["converts"] = Combat.DamageTypeResolution.Resolve(after.Tags).ToString();
        }

        return fx;
    }

    public override void _Ready()
    {
        var skills = new StringBuilder("Skill\tName\tType\tTags\tUnlock level\tDamage\tUse time\tCooldown\tMana\tDescription\n");
        var nodes = new StringBuilder("Skill\tBranch\tColumn\tNode\tKind\tRanks\tRank 1\tRank 2\tRank 3\tRequires\tPoints needed\tEither/or group\n");
        var json = new Godot.Collections.Array();
        foreach (SkillEntry e in UpgradeCatalog.Skills)
        {
            SkillDefinition d = e.Definition;
            int unlock = GemShop.Find(e.Id, false)?.Level ?? 0;
            float mana = d.Costs.Count > 0 ? d.Costs[0].Amount : 0f;
            string type = d switch
            {
                ProjectileSkillDefinition => "Projectile", AreaSkillDefinition => "Area", MeleeSkillDefinition => "Melee", OrbitSkillDefinition => "Orbit", CurseSkillDefinition => "Curse",
                AuraSkillDefinition a => a.IsToggle ? "Aura (toggle)" : "Aura", MovementSkillDefinition => "Movement", TotemSkillDefinition => "Totem", AfflictionSkillDefinition => "Affliction",
                _ => d.GetType().Name,
            };
            var jsonNodes = new Godot.Collections.Array();
            if (SkillTrees.For(e.Id) is { } t)
            {
                foreach (SkillTreeNode n in t.Nodes.OrderBy(n => n.Branch).ThenBy(n => n.Depth))
                {
                    var ranks = new Godot.Collections.Array();
                    for (int i = 1; i <= n.MaxRank; i++)
                    {
                        ranks.Add(n.Describe(i));
                    }

                    var effects = new Godot.Collections.Array();
                    for (int i = 1; i <= n.MaxRank; i++)
                    {
                        effects.Add(EffectsOf(d, n, i));
                    }

                    jsonNodes.Add(new Godot.Collections.Dictionary
                    {
                        ["effects"] = effects,
                        ["id"] = n.Id, ["name"] = n.Name, ["kind"] = n.Kind.ToString(), ["branch"] = n.Branch, ["depth"] = n.Depth,
                        ["ranks"] = ranks, ["requires"] = n.Requires ?? "", ["requiresRanks"] = n.Requires is null ? 0 : n.RequiresRanks, ["exclusive"] = n.Exclusive ?? "",
                        ["parents"] = new Godot.Collections.Array(n.Parents.Select(p => Variant.From(new Godot.Collections.Dictionary { ["id"] = p.Id, ["ranks"] = p.Ranks }))),
                    });
                }
            }

            json.Add(new Godot.Collections.Dictionary
            {
                ["id"] = e.Id, ["name"] = d.DisplayName, ["type"] = type, ["tags"] = d.Tags.ToString(), ["unlock"] = unlock, ["damage"] = d.BaseDamage,
                ["useTime"] = d.BaseUseTime, ["cooldown"] = d.Cooldown, ["mana"] = mana, ["description"] = d.Description,
                ["hits"] = d switch { ProjectileSkillDefinition pr => pr.Count, MeleeSkillDefinition me => me.Strikes, OrbitSkillDefinition or => or.Count, _ => 1 },
                ["burst"] = d is ProjectileSkillDefinition { ExplodeRadius: > 0f } ? 1 : 0,
                ["burstShare"] = d is ProjectileSkillDefinition px ? px.ExplodeDamageFraction : 0f,
                ["damagePerLevel"] = SkillInstance.DamagePerLevel, ["costPerLevel"] = SkillInstance.CostPerLevel, ["auraPerLevel"] = AuraCatalog.EffectPerLevel,
                ["branches"] = new Godot.Collections.Array(SkillTrees.For(e.Id)?.Branches.Select(b => Variant.From(b)) ?? Enumerable.Empty<Variant>()),
                ["nodes"] = jsonNodes,
            });
            skills.Append($"{e.Id}\t{d.DisplayName}\t{type}\t{d.Tags}\t{unlock}\t{d.BaseDamage}\t{d.BaseUseTime}\t{d.Cooldown}\t{mana}\t{d.Description}\n");
            if (SkillTrees.For(e.Id) is not { } tree)
            {
                continue;
            }

            foreach (SkillTreeNode n in tree.Nodes.OrderBy(n => n.Branch).ThenBy(n => n.Depth))
            {
                string r(int i) => i <= n.MaxRank ? n.Describe(i) : "";
                string req = n.Requires is { } p ? tree.Node(p)?.Name ?? p : "";
                nodes.Append($"{d.DisplayName}\t{tree.Branches[n.Branch]}\t{n.Depth + 1}\t{n.Name}\t{n.Kind}\t{n.MaxRank}\t{r(1)}\t{r(2)}\t{r(3)}\t{req}\t{(n.Requires is null ? "" : n.RequiresRanks.ToString())}\t{n.Exclusive ?? ""}\n");
            }
        }

        using (var f = FileAccess.Open("user://skill_trees_skills.tsv", FileAccess.ModeFlags.Write)) { f.StoreString(skills.ToString()); }
        using (var f = FileAccess.Open("user://skill_trees_nodes.tsv", FileAccess.ModeFlags.Write)) { f.StoreString(nodes.ToString()); }
        using (var f = FileAccess.Open("user://skill_trees.json", FileAccess.ModeFlags.Write)) { f.StoreString(Json.Stringify(new Godot.Collections.Dictionary { ["skills"] = json }, "  ")); }
        GD.Print("[DUMP] done");
        GetTree().Quit();
    }
}
