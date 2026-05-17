using Godot;

// Page de remap des touches — modale plein écran.
//
// UX :
//   - Une ligne par action (libellé à gauche, bouton "touche actuelle" à droite).
//   - Clic sur le bouton → mode capture : "Appuie sur une touche…".
//   - Premier InputEventKey ou InputEventMouseButton reçu → applique + sauve.
//   - Échap pendant la capture → annule ; Échap hors capture → ferme la page.
//
// Une seule capture à la fois : on garde une référence au bouton en cours
// pour pouvoir refléter l'état "[ ATTENTE ]" et restaurer après.
public partial class PageRemap : Control
{
	[Signal] public delegate void FermeEventHandler();

	private static readonly Color CouleurFond    = new Color(0.025f, 0.035f, 0.055f, 0.97f);
	private static readonly Color CouleurAccent  = new Color(0.55f, 0.93f, 1f, 1f);
	private static readonly Color CouleurTexte   = new Color(0.85f, 0.97f, 1f, 1f);
	private static readonly Color CouleurLabel   = new Color(0.7f, 0.85f, 0.92f, 0.9f);
	private static readonly Color CouleurCapture = new Color(1f, 0.85f, 0.3f, 1f);

	private Button boutonEnCapture;
	private string actionEnCapture;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		var fond = new ColorRect { Color = CouleurFond };
		fond.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(fond);

		var center = new CenterContainer();
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
		vbox.AddThemeConstantOverride("separation", 6);
		center.AddChild(vbox);

		var titre = new Label
		{
			Text = "REMAP DES TOUCHES",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		titre.AddThemeFontSizeOverride("font_size", 40);
		titre.AddThemeColorOverride("font_color", CouleurAccent);
		vbox.AddChild(titre);

		var hint = new Label
		{
			Text = "Clique sur une touche pour la remplacer.",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		hint.AddThemeColorOverride("font_color", CouleurLabel);
		hint.AddThemeFontSizeOverride("font_size", 14);
		vbox.AddChild(hint);

		vbox.AddChild(new HSeparator());

		// Grille 2 colonnes [action | bouton]
		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 24);
		grid.AddThemeConstantOverride("v_separation", 6);
		vbox.AddChild(grid);

		foreach (var (action, libelle) in RemapJeu.ActionsRemappables)
			AjouterLigne(grid, action, libelle);

		vbox.AddChild(new HSeparator());

		var hboxBtn = new HBoxContainer();
		hboxBtn.AddThemeConstantOverride("separation", 12);
		hboxBtn.Alignment = BoxContainer.AlignmentMode.Center;

		var btnReset = CreerBouton("RÉINITIALISER");
		btnReset.Pressed += OnResetPressed;
		hboxBtn.AddChild(btnReset);

		var btnRetour = CreerBouton("RETOUR");
		btnRetour.Pressed += OnRetourPressed;
		hboxBtn.AddChild(btnRetour);

		vbox.AddChild(hboxBtn);
		btnRetour.GrabFocus();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Mode capture : on intercepte la prochaine touche/clic.
		if (boutonEnCapture != null)
		{
			if (@event.IsActionPressed(InputBindings.Pause) || @event.IsActionPressed("ui_cancel"))
			{
				FinirCapture(annule: true);
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventKey k && k.Pressed && !k.Echo)
			{
				RemapJeu.Definir(actionEnCapture, k);
				FinirCapture(annule: false);
				GetViewport().SetInputAsHandled();
				return;
			}
			if (@event is InputEventMouseButton m && m.Pressed)
			{
				RemapJeu.Definir(actionEnCapture, m);
				FinirCapture(annule: false);
				GetViewport().SetInputAsHandled();
				return;
			}
			return;
		}

		// Hors capture : Échap ferme la page.
		if (@event.IsActionPressed(InputBindings.Pause) || @event.IsActionPressed("ui_cancel"))
		{
			OnRetourPressed();
			GetViewport().SetInputAsHandled();
		}
	}

	private void AjouterLigne(GridContainer grid, string action, string libelle)
	{
		var lAction = new Label { Text = libelle, CustomMinimumSize = new Vector2(360, 0) };
		lAction.AddThemeColorOverride("font_color", CouleurLabel);
		grid.AddChild(lAction);

		var btn = new Button
		{
			Text = RemapJeu.Libelle(action),
			CustomMinimumSize = new Vector2(180, 32),
		};
		btn.SetMeta("action_id", action); // pour retrouver le bouton après reset
		btn.AddThemeColorOverride("font_color", CouleurTexte);
		btn.AddThemeFontSizeOverride("font_size", 16);
		btn.Pressed += () => CommencerCapture(btn, action);
		grid.AddChild(btn);
	}

	private void CommencerCapture(Button btn, string action)
	{
		// Annule la capture en cours si on clique sur une autre ligne.
		if (boutonEnCapture != null) FinirCapture(annule: true);

		boutonEnCapture = btn;
		actionEnCapture = action;
		btn.Text = "[ ATTENTE… ]";
		btn.AddThemeColorOverride("font_color", CouleurCapture);
		btn.ReleaseFocus(); // pour que Espace/Entrée comptent comme nouvelle touche
	}

	private void FinirCapture(bool annule)
	{
		if (boutonEnCapture == null) return;
		boutonEnCapture.Text = RemapJeu.Libelle(actionEnCapture);
		boutonEnCapture.AddThemeColorOverride("font_color", CouleurTexte);
		boutonEnCapture = null;
		actionEnCapture = null;
	}

	private void OnResetPressed()
	{
		if (boutonEnCapture != null) FinirCapture(annule: true);
		RemapJeu.ReinitialiserTout();
		// Rafraîchit l'affichage des boutons sans rebuild complet.
		RafraichirToutesLesLignes(this);
	}

	private void RafraichirToutesLesLignes(Node racine)
	{
		// Parcourt l'arbre et met à jour les boutons : on les retrouve par leur
		// callback connecté à CommencerCapture. Simple : on rebuild le texte
		// en relisant InputMap pour chaque action remappable.
		foreach (var (action, _) in RemapJeu.ActionsRemappables)
		{
			RechercherEtMaJBoutonAction(racine, action);
		}
	}

	private void RechercherEtMaJBoutonAction(Node n, string action)
	{
		// On a stocké l'action dans la lambda Pressed ; sans accès direct,
		// on relit simplement le label : si le texte courant correspond à
		// l'ancien libellé d'action, on le met à jour. Plus robuste :
		// itérer par paires action↔bouton via une map. Mais ici plus simple :
		// on rebuild le texte pour tout bouton enfant.
		foreach (var c in n.GetChildren())
		{
			if (c is Button b && b.GetMeta("action_id", "").AsString() == action)
				b.Text = RemapJeu.Libelle(action);
			RechercherEtMaJBoutonAction(c, action);
		}
	}

	private Button CreerBouton(string texte)
	{
		var btn = new Button
		{
			Text = texte,
			CustomMinimumSize = new Vector2(180, 48),
		};
		btn.AddThemeFontSizeOverride("font_size", 18);
		return btn;
	}

	private void OnRetourPressed()
	{
		if (boutonEnCapture != null) FinirCapture(annule: true);
		EmitSignal(SignalName.Ferme);
		QueueFree();
	}
}
