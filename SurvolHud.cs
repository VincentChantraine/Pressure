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

	private Label labelPrompt;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		labelPrompt = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Top,
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
			Vector2 centre = Size * 0.5f;
			var taille = labelPrompt.GetMinimumSize();
			labelPrompt.Position = new Vector2(centre.X - taille.X * 0.5f, centre.Y + 20f);
		}
	}

	public override void _Draw()
	{
		Vector2 centre = Size * 0.5f;
		Color c = Survol3D.CibleCourante != null ? CouleurReticuleSurvol : CouleurReticule;
		DrawCircle(centre, TailleReticule, c);
	}
}
