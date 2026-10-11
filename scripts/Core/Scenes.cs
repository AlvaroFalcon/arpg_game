#nullable enable
using Godot;

namespace PathToExile.Core;

/// <summary>Scene paths and transitions in one place (docs/design/menus.md -> "Flow").</summary>
public static class Scenes
{
    public const string MainMenu = "res://scenes/menus/MainMenu.tscn";
    public const string Hideout = "res://scenes/Hideout.tscn";
    public const string Run = "res://scenes/run/Run.tscn";
    public const string Sandbox = "res://scenes/Main.tscn";

    public static void Go(Node context, string path)
    {
        UI.PanelStack.Clear();
        context.GetTree().Paused = false;
        context.GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, path);
    }
}
