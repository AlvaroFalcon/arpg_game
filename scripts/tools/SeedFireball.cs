using Godot;
using Godot.Collections;
using ArpgGame.Combat;
using ArpgGame.Skills;
using ArpgGame.Stats;

namespace ArpgGame.Tools;

/// <summary>
/// One-shot resource-authoring tool: builds the Fireball skill definition in code
/// and saves it with <see cref="ResourceSaver"/>, then quits. Exists because
/// hand-writing a `.tres` containing a nested Resource array (the Costs list)
/// correctly is exactly the kind of thing Godot's own serializer should produce,
/// not something to get right by typing sub_resource blocks by hand.
///
/// Run with:
///   dotnet build ArpgGame.csproj
///   ./scripts/run_headless.sh res://scenes/tools/SeedFireball.tscn --quit-after 2
///
/// Safe to re-run — always overwrites res://resources/skills/Fireball.tres.
/// Numbers are placeholders; docs/design/skills.md is explicit that first-pass
/// numbers, tuned against a stationary dummy, are non-binding.
/// </summary>
public partial class SeedFireball : Node
{
    public override void _Ready()
    {
        var manaCost = new CostEntry
        {
            Resource = ResourceType.Mana,
            Amount = 8f,
            Mode = CostMode.Flat,
        };

        var fireball = new ProjectileSkillDefinition
        {
            DisplayName = "Fireball",
            BaseDamage = 35f,
            BaseUseTime = 0.4f,
            Tags = SkillTags.Fire | SkillTags.Projectile | SkillTags.Spell,
            Costs = new Array<CostEntry> { manaCost },
            Cooldown = 0f,
            MaxCharges = 1,
            MovementDuringUse = MovementDuringUse.Free,
            Speed = 600f,
            Range = 500f,
            SpawnOffset = 20f,
        };

        const string path = "res://resources/skills/Fireball.tres";
        Error err = ResourceSaver.Save(fireball, path);
        GD.Print(err == Error.Ok
            ? $"[SEED] Saved {path}"
            : $"[SEED] FAILED to save {path}: {err}");

        GetTree().Quit();
    }
}
