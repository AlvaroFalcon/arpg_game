using Godot;
using Godot.Collections;
using PathToExile.Stats;

namespace PathToExile.Tools;

/// <summary>
/// One-shot resource-authoring tool: builds the target-dummy <see cref="StatBlock"/>
/// variants in code and saves them with <see cref="ResourceSaver"/>, then quits.
/// Same rationale as <see cref="SeedFireball"/> — a StatBlock containing a nested
/// Resource array (<see cref="StatBlock.Entries"/>) is exactly the kind of thing
/// Godot's own serializer should produce, not something to hand-type sub_resource
/// blocks for.
///
/// Run with:
///   dotnet build PathToExile.csproj
///   ./scripts/run_headless.sh res://scenes/tools/SeedStatBlocks.tscn --quit-after 2
///
/// Safe to re-run — always overwrites the four res://resources/stats/*.tres files.
/// These four exist because docs/design/stats.md's second pass moved base stat
/// values off per-instance scene-node float overrides
/// (Stats.BaseMaxLife = 105.0, etc. — which stopped compiling once that field was
/// removed) and onto StatBlock resources assigned to ActorStats.Bases, which is
/// also the intended per-archetype authoring path once real enemies exist.
/// </summary>
public partial class SeedStatBlocks : Node
{
    public override void _Ready()
    {
        Save("TargetDummy30", 30f);
        Save("TargetDummy105", 105f);
        Save("TargetDummy210", 210f);
        Save("TargetDummy420", 420f);

        GetTree().Quit();
    }

    private static void Save(string name, float maxLife)
    {
        var block = new StatBlock
        {
            Entries = new Array<StatBlockEntry>
            {
                new() { Id = StatId.MaxLife, BaseValue = maxLife },
            },
        };

        string path = $"res://resources/stats/{name}.tres";
        Error err = ResourceSaver.Save(block, path);
        GD.Print(err == Error.Ok
            ? $"[SEED] Saved {path}"
            : $"[SEED] FAILED to save {path}: {err}");
    }
}
