using Godot;

// Menu principal — point d'entrée du jeu.
// À placer comme scène de démarrage dans Project Settings → Application → Run.
public partial class MenuPrincipal : Control
{
	[Export] public NodePath BoutonJouerPath;
	[Export] public NodePath BoutonQuitterPath;

	private Button boutonJouer;
	private Button boutonQuitter;
	private Button boutonCommandes;
	private Button boutonOptions;

	// Page ouverte (PageOptions ou PageCommandes). Null si menu principal seul.
	// Permet de bloquer l'ouverture d'une 2e page tant que la 1re est affichée.
	private Control pageOuverte;

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

		// Injection des boutons COMMANDES et OPTIONS entre Jouer et Quitter,
		// en copiant le style des boutons existants (StyleBox, font, couleurs).
		InjecterBoutonsAdditionnels();

		// Panneau "MEILLEUR TEMPS" en bas à droite. Caché si aucun record.
		AfficherPanneauRecord();

		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void AfficherPanneauRecord()
	{
		if (ParametresJeu.MeilleurTempsVictoire <= 0f) return;

		var panneau = new VBoxContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
		};
		// Ancrage bas-droite, au-dessus de "BasDroite" / "ESC : QUITTER" du tscn.
		panneau.SetAnchorsPreset(LayoutPreset.BottomRight);
		panneau.OffsetLeft   = -360;
		panneau.OffsetRight  = -36;
		panneau.OffsetTop    = -260;
		panneau.OffsetBottom = -90;
		panneau.GrowHorizontal = GrowDirection.Begin;
		panneau.GrowVertical   = GrowDirection.Begin;
		panneau.AddThemeConstantOverride("separation", 2);
		AddChild(panneau);

		var titre = new Label
		{
			Text = "MEILLEUR TEMPS",
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		titre.AddThemeColorOverride("font_color", new Color(0.4f, 0.78f, 0.9f, 0.85f));
		titre.AddThemeFontSizeOverride("font_size", 16);
		panneau.AddChild(titre);

		var totalLabel = new Label
		{
			Text = FormaterTemps(ParametresJeu.MeilleurTempsVictoire),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		totalLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f, 1f));
		totalLabel.AddThemeFontSizeOverride("font_size", 28);
		panneau.AddChild(totalLabel);

		// Séparateur fin
		var ligne = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 1),
			Color = new Color(0.31f, 0.78f, 0.88f, 0.35f),
		};
		panneau.AddChild(ligne);

		// Détail par salle, si dispo.
		// L'ordre des lignes = ordre de résolution. On n'ajoute pas de compteur
		// "Salle N" devant pour éviter le doublon avec l'id (souvent "salle_1").
		var ids = ParametresJeu.MeilleurSallesIds;
		var durees = ParametresJeu.MeilleurSallesDurees;
		int n = Mathf.Min(ids.Length, durees.Length);
		for (int i = 0; i < n; i++)
		{
			var ligneSalle = new Label
			{
				Text = $"{FormaterNomSalle(ids[i])}   {FormaterTemps(durees[i])}",
				HorizontalAlignment = HorizontalAlignment.Right,
			};
			ligneSalle.AddThemeColorOverride("font_color", new Color(0.78f, 0.92f, 1f, 0.85f));
			ligneSalle.AddThemeFontSizeOverride("font_size", 14);
			panneau.AddChild(ligneSalle);
		}
	}

	private static string FormaterTemps(float secondes)
	{
		int total = Mathf.FloorToInt(secondes);
		return $"{total / 60:00}:{total % 60:00}";
	}

	// Capitalise et nettoie l'id d'une salle pour l'affichage ("salle_1" → "Salle 1").
	private static string FormaterNomSalle(string id)
	{
		if (string.IsNullOrEmpty(id)) return "—";
		if (id.StartsWith("salle_", System.StringComparison.OrdinalIgnoreCase))
			return "Salle " + id.Substring(6);
		// Fallback : majuscule + remplace underscores par espaces (ids type "coffre_principal").
		return char.ToUpper(id[0]) + id.Substring(1).Replace('_', ' ');
	}

	private void InjecterBoutonsAdditionnels()
	{
		if (boutonJouer == null || boutonQuitter == null) return;
		if (boutonJouer.GetParent() is not VBoxContainer vbox) return;

		boutonCommandes = CreerBoutonCopieDe(boutonQuitter, "⌨  COMMANDES");
		boutonCommandes.Pressed += OnCommandesPressed;
		vbox.AddChild(boutonCommandes);
		vbox.MoveChild(boutonCommandes, boutonQuitter.GetIndex());

		boutonOptions = CreerBoutonCopieDe(boutonQuitter, "⚙  OPTIONS");
		boutonOptions.Pressed += OnOptionsPressed;
		vbox.AddChild(boutonOptions);
		vbox.MoveChild(boutonOptions, boutonQuitter.GetIndex());
	}

	// Crée un bouton dont l'apparence (StyleBox, taille, font, couleurs) est
	// recopiée du bouton modèle — évite de dupliquer les définitions de style
	// dans le .tscn ou dans le code.
	private static Button CreerBoutonCopieDe(Button modele, string texte)
	{
		var btn = new Button
		{
			Text = texte,
			CustomMinimumSize = modele.CustomMinimumSize,
			SizeFlagsHorizontal = modele.SizeFlagsHorizontal,
		};

		foreach (var slot in new[] { "normal", "pressed", "hover", "focus" })
		{
			var sb = modele.GetThemeStylebox(slot);
			if (sb != null) btn.AddThemeStyleboxOverride(slot, sb);
		}

		int fs = modele.GetThemeFontSize("font_size");
		if (fs > 0) btn.AddThemeFontSizeOverride("font_size", fs);

		foreach (var c in new[] { "font_color", "font_focus_color", "font_pressed_color", "font_hover_color" })
		{
			btn.AddThemeColorOverride(c, modele.GetThemeColor(c));
		}

		return btn;
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

	private void OnCommandesPressed()
	{
		OuvrirPage(new PageCommandes());
	}

	private void OnOptionsPressed()
	{
		OuvrirPage(new PageOptions());
	}

	private void OuvrirPage(Control page)
	{
		if (pageOuverte != null) return;
		pageOuverte = page;
		AddChild(page);
		// On utilise des delegates différents pour Options et Commandes selon le type.
		if (page is PageOptions po) po.Ferme += OnPageFermee;
		else if (page is PageCommandes pc) pc.Ferme += OnPageFermee;
	}

	private void OnPageFermee()
	{
		pageOuverte = null;
		boutonJouer?.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Esc : si une page est ouverte, elle gère elle-même la fermeture.
		// Sinon, Esc quitte le jeu.
		if (pageOuverte == null && @event.IsActionPressed("ui_cancel"))
			OnQuitterPressed();
	}
}
