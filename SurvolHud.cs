using Godot;

// HUD léger au centre de l'écran :
//   - petit réticule toujours visible (point cyan) ;
//   - libellé contextuel sous le réticule quand un interactif est ciblé
//     (lu depuis Survol3D.LibelleCourant).
//
// Instancié par PartieManager dans un CanvasLayer dédié.
public partial class SurvolHud : Control
{
	[Export] public Color CouleurReticule = new Color(0.55f, 0.93f, 1f, 0.55f);
	[Export] public Color CouleurReticuleSurvol = new Color(1f, 1f, 1f, 0.95f);
	[Export] public float TailleReticule = 4f;
	// Anneau plus gros qui apparaît autour du point quand on survole un interactif :
	// un changement de forme/taille se voit beaucoup mieux qu'un simple changement
	// de couleur sur 4 pixels.
	[Export] public float TailleAnneauSurvol = 11f;
	[Export] public float EpaisseurAnneauSurvol = 2f;

	private Label labelPrompt;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		labelPrompt = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			// Center plutôt que Top : avec une boîte de taille minimum (cf. _Process),
			// le texte est rendu pile au milieu vertical de cette boîte, indépendamment
			// des métriques de la police (ascender/descender) qui font dériver le Top.
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		labelPrompt.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 1f));
		labelPrompt.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
		labelPrompt.AddThemeConstantOverride("outline_size", 4);
		labelPrompt.AddThemeFontSizeOverride("font_size", 16);
		AddChild(labelPrompt);
	}

	public override void _Process(double delta)
	{
		QueueRedraw();

		// Label prioritaire : visible dès qu'on regarde un interactif, quelle que
		// soit la situation. Les HUDs de puzzle (PorteHud, CoffreHud…) se masquent
		// au lieu de doubler le prompt — cf. BasePuzzleHud.OnJoueurEntreZone.
		var libelle = Survol3D.LibelleCourant ?? "";
		labelPrompt.Visible = libelle.Length > 0;
		if (labelPrompt.Visible)
		{
			labelPrompt.Text = libelle;
			Vector2 centre = (GetViewportRect().Size * 0.5f).Round();
			// On force la Size à la taille minimum : sinon la Label garde une
			// hauteur auto qui varie selon le contenu / l'historique du contrôle,
			// et VerticalAlignment.Center s'aligne alors à des Y différents d'un
			// libellé à l'autre → le texte "saute" verticalement.
			var taille = labelPrompt.GetMinimumSize();
			labelPrompt.Size = taille;
			// La boîte est centrée à 40px sous le réticule (haut de la boîte = +28
			// environ pour une police 16) : assez loin pour ne pas tirer l'œil mais
			// proche pour rester lisible en périphérie.
			float yCentre = centre.Y + 40f;
			labelPrompt.Position = new Vector2(
				centre.X - taille.X * 0.5f,
				yCentre - taille.Y * 0.5f);
		}
	}

	public override void _Draw()
	{
		// Arrondi aux pixels entiers : DrawCircle sur un centre fractionnaire
		// (ex. 960.5) donne un blob asymétrique pour un petit rayon.
		Vector2 centre = (GetViewportRect().Size * 0.5f).Round();
		bool survol = Survol3D.CibleCourante != null;
		Color c = survol ? CouleurReticuleSurvol : CouleurReticule;
		DrawCircle(centre, TailleReticule, c);
		if (survol)
		{
			DrawArc(centre, TailleAnneauSurvol, 0f, Mathf.Tau,
				32, c, EpaisseurAnneauSurvol, antialiased: true);
		}
	}
}
