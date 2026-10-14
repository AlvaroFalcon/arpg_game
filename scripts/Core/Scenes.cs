#nullable enable
using Godot;

namespace ArpgGame.Core;

/// <summary>Scene paths and transitions in one place (docs/design/menus.md -> "Flow").</summary>
public static class Scenes
{
    public const string MainMenu = "res://scenes/menus/MainMenu.tscn";
    public const string Hideout = "res://scenes/Hideout.tscn";
    public const string Run = "res://scenes/run/Run.tscn";
    public const string Reality = "res://scenes/reality/Reality.tscn";
    public const string Sandbox = "res://scenes/Main.tscn";

    public static void Go(Node context, string path)
    {
        UI.PanelStack.Clear();
        SceneTree tree = context.GetTree();
        tree.Paused = false;
        // Heavy destinations go through the loading screen (docs/design/menus.md ->
        // "Loading screen"): a reality generates its cavern on load.
        (string Title, string Subtitle)? loading = path switch
        {
            Reality => ("The Mirror opens", "A new reality takes shape…"),
            Run => ("Survival Rift", "The arena stirs…"),
            Hideout => ("Your hideout", "Returning home…"),
            _ => null,
        };
        if (loading is { } l)
        {
            UI.LoadingScreen.Go(tree, path, l.Title, l.Subtitle);
            return;
        }

        tree.CallDeferred(SceneTree.MethodName.ChangeSceneToFile, path);
    }
}
