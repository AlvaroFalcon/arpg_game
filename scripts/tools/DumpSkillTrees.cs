#nullable enable
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

                    jsonNodes.Add(new Godot.Collections.Dictionary
                    {
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
