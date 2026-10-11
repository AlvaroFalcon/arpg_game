using Godot;

namespace PathToExile;

public partial class Main : Node
{
	public override void _Ready()
	{
		GD.Print("Path to Exile: Main scene loaded (C#/.NET, Godot 4).");

		// The sandbox is reachable from the main menu now, so it needs a way
		// back out (docs/design/menus.md -> "Pause").
		AddChild(new UI.PauseMenu { LeaveText = "Main menu", OnLeave = () => Core.Scenes.Go(this, Core.Scenes.MainMenu) });
	}
}
