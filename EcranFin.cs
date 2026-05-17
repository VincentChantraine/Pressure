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

	// Conteneur VBox dans lequel on injecte une ligne par salle validée
	// (label gauche = nom, label droite = durée).
	[Export] public NodePath DetailsSallesPath;
	// Label affichant le meilleur temps persistant (et "Nouveau record !" si battu).
	[Export] public NodePath RecordLabelPath;

	[Export] public Color CouleurVictoire = new Color(0.4f, 1f, 0.5f);
	[Export] public Color CouleurDefaite  = new Color(1f, 0.35f, 0.35f);
	[Export] public Color CouleurRecord   = new Color(1f, 0.85f, 0.3f);

	private Label titreLabel;
	private Label sousTitreLabel;
	private Label tempsLabel;
	private Button boutonRejouer;
	private Button boutonMenu;
	private Button boutonQuitter;
	private VBoxContainer detailsSalles;
	private Label recordLabel;

	public override void _Ready()
	{
		titreLabel     = GetNodeOrNull<Label>(TitreLabelPath);
		sousTitreLabel = GetNodeOrNull<Label>(SousTitreLabelPath);
		tempsLabel     = GetNodeOrNull<Label>(TempsLabelPath);
		boutonRejouer  = GetNodeOrNull<Button>(BoutonRejouerPath);
		boutonMenu     = GetNodeOrNull<Button>(BoutonMenuPath);
		boutonQuitter  = GetNodeOrNull<Button>(BoutonQuitterPath);
		detailsSalles  = GetNodeOrNull<VBoxContainer>(DetailsSallesPath);
		recordLabel    = GetNodeOrNull<Label>(RecordLabelPath);

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

		AfficherDetailsSalles(gs);
		AfficherRecord(gs, resultat, temps);
	}

	private void AfficherDetailsSalles(GameState gs)
	{
		if (detailsSalles == null) return;
		// Nettoyage défensif : si la scène contient des enfants exemples laissés
		// par le designer dans l'éditeur, on repart d'un VBox vide.
		foreach (Node enfant in detailsSalles.GetChildren())
			enfant.QueueFree();

		if (gs == null || gs.OrdreSallesValidees.Count == 0)
		{
			AjouterLigneInfo("Aucune salle validée.", "—");
			return;
		}

		float precedent = 0f;
		for (int i = 0; i < gs.OrdreSallesValidees.Count; i++)
		{
			string id = gs.OrdreSallesValidees[i];
			float tValidation = gs.TempsValidationParSalle.TryGetValue(id, out var v) ? v : 0f;
			float duree = Mathf.Max(0f, tValidation - precedent);
			precedent = tValidation;
			AjouterLigneInfo(FormaterNomSalle(id), FormaterTemps(duree));
		}
	}

	private static string FormaterNomSalle(string id)
	{
		if (string.IsNullOrEmpty(id)) return "—";
		if (id.StartsWith("salle_", System.StringComparison.OrdinalIgnoreCase))
			return "Salle " + id.Substring(6);
		return char.ToUpper(id[0]) + id.Substring(1).Replace('_', ' ');
	}

	private void AjouterLigneInfo(string libelle, string valeur)
	{
		var ligne = new HBoxContainer();
		ligne.AddThemeConstantOverride("separation", 14);

		var labelGauche = new Label
		{
			Text = libelle,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		labelGauche.AddThemeColorOverride("font_color", new Color(0.85f, 0.94f, 1f, 0.9f));
		labelGauche.AddThemeFontSizeOverride("font_size", 18);

		var labelDroite = new Label
		{
			Text = valeur,
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		labelDroite.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
		labelDroite.AddThemeFontSizeOverride("font_size", 18);

		ligne.AddChild(labelGauche);
		ligne.AddChild(labelDroite);
		detailsSalles.AddChild(ligne);
	}

	private void AfficherRecord(GameState gs, GameState.ResultatPartie resultat, float temps)
	{
		if (recordLabel == null) return;

		// On n'enregistre un record que pour une vraie victoire (temps > 0).
		bool nouveauRecord = false;
		if (resultat == GameState.ResultatPartie.Victoire && temps > 0f)
		{
			// Reconstruit le détail "durée par salle" depuis l'ordre chronologique
			// pour le persister avec le record (affiché ensuite dans MenuPrincipal).
			var ids    = new System.Collections.Generic.List<string>();
			var durees = new System.Collections.Generic.List<float>();
			if (gs != null)
			{
				float precedent = 0f;
				foreach (string id in gs.OrdreSallesValidees)
				{
					float t = gs.TempsValidationParSalle.TryGetValue(id, out var v) ? v : 0f;
					ids.Add(id);
					durees.Add(Mathf.Max(0f, t - precedent));
					precedent = t;
				}
			}
			nouveauRecord = ParametresJeu.EnregistrerTempsVictoire(temps, ids, durees);
		}

		float record = ParametresJeu.MeilleurTempsVictoire;
		if (record <= 0f)
		{
			recordLabel.Text = "Meilleur temps : —";
			recordLabel.Modulate = new Color(1, 1, 1, 0.7f);
		}
		else if (nouveauRecord)
		{
			recordLabel.Text = $"★ NOUVEAU RECORD : {FormaterTemps(record)}";
			recordLabel.Modulate = CouleurRecord;
		}
		else
		{
			recordLabel.Text = $"Meilleur temps : {FormaterTemps(record)}";
			recordLabel.Modulate = new Color(1, 1, 1, 0.85f);
		}
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
