using Godot;

// Écran de fin — affiché après une victoire ou une défaite.
// Lit GameState.DernierResultat et GameState.DernierTempsEcoule.
public partial class EcranFin : Control
{
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath SousTitreLabelPath;
	[Export] public NodePath TempsLabelPath;
	[Export] public NodePath BoutonRejouerPath;
	[Export] public NodePath BoutonMenuPath;
	[Export] public NodePath BoutonQuitterPath;

	[Export] public Color CouleurVictoire = new Color(0.4f, 1f, 0.5f);
	[Export] public Color CouleurDefaite  = new Color(1f, 0.35f, 0.35f);

	private Label titreLabel;
	private Label sousTitreLabel;
	private Label tempsLabel;
	private Button boutonRejouer;
	private Button boutonMenu;
	private Button boutonQuitter;

	public override void _Ready()
	{
		titreLabel     = GetNodeOrNull<Label>(TitreLabelPath);
		sousTitreLabel = GetNodeOrNull<Label>(SousTitreLabelPath);
		tempsLabel     = GetNodeOrNull<Label>(TempsLabelPath);
		boutonRejouer  = GetNodeOrNull<Button>(BoutonRejouerPath);
		boutonMenu     = GetNodeOrNull<Button>(BoutonMenuPath);
		boutonQuitter  = GetNodeOrNull<Button>(BoutonQuitterPath);

		if (boutonRejouer != null)
		{
			boutonRejouer.Pressed += OnRejouerPressed;
			boutonRejouer.GrabFocus();
		}
		if (boutonMenu != null)
			boutonMenu.Pressed += OnMenuPressed;
		if (boutonQuitter != null)
			boutonQuitter.Pressed += OnQuitterPressed;

		AfficherResultat();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void AfficherResultat()
	{
		var gs = GameState.Instance;
		var resultat = gs?.DernierResultat ?? GameState.ResultatPartie.Aucun;
		float temps = gs?.DernierTempsEcoule ?? 0f;

		if (titreLabel != null)
		{
			switch (resultat)
			{
				case GameState.ResultatPartie.Victoire:
					titreLabel.Text = "VICTOIRE";
					titreLabel.Modulate = CouleurVictoire;
					if (sousTitreLabel != null)
						sousTitreLabel.Text = "Vous avez repris les commandes du BT-01.";
					break;
				case GameState.ResultatPartie.Defaite:
					titreLabel.Text = "DÉFAITE";
					titreLabel.Modulate = CouleurDefaite;
					if (sousTitreLabel != null)
						sousTitreLabel.Text = "Le temps est écoulé.";
					break;
				default:
					titreLabel.Text = "PARTIE TERMINÉE";
					if (sousTitreLabel != null)
						sousTitreLabel.Text = "";
					break;
			}
		}

		if (tempsLabel != null)
			tempsLabel.Text = FormaterTemps(temps);
	}

	private static string FormaterTemps(float secondes)
	{
		int total = Mathf.FloorToInt(secondes);
		int min = total / 60;
		int sec = total % 60;
		return $"{min:00}:{sec:00}";
	}

	private void OnRejouerPressed()
	{
		if (GameState.Instance != null)
			GameState.Instance.LancerPartie();
		else
			GetTree().ChangeSceneToFile("res://node_3d.tscn");
	}

	private void OnMenuPressed()
	{
		if (GameState.Instance != null)
			GameState.Instance.ChargerMenu();
		else
			GetTree().ChangeSceneToFile("res://MenuPrincipal.tscn");
	}

	private void OnQuitterPressed()
	{
		GetTree().Quit();
	}
}
