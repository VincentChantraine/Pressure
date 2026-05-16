using Godot;

// Menu principal — point d'entrée du jeu.
// À placer comme scène de démarrage dans Project Settings → Application → Run.
public partial class MenuPrincipal : Control
{
	[Export] public NodePath BoutonJouerPath;
	[Export] public NodePath BoutonQuitterPath;

	private Button boutonJouer;
	private Button boutonQuitter;

	public override void _Ready()
	{
		boutonJouer = GetNodeOrNull<Button>(BoutonJouerPath);
		boutonQuitter = GetNodeOrNull<Button>(BoutonQuitterPath);

		if (boutonJouer != null)
		{
			boutonJouer.Pressed += OnJouerPressed;
			boutonJouer.GrabFocus();
		}
		else GD.PrintErr("[MenuPrincipal] Bouton Jouer introuvable.");

		if (boutonQuitter != null)
			boutonQuitter.Pressed += OnQuitterPressed;
		else GD.PrintErr("[MenuPrincipal] Bouton Quitter introuvable.");

		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void OnJouerPressed()
	{
		if (GameState.Instance != null)
			GameState.Instance.LancerPartie();
		else
			GetTree().ChangeSceneToFile("res://node_3d.tscn");
	}

	private void OnQuitterPressed()
	{
		GetTree().Quit();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
			OnQuitterPressed();
	}
}
