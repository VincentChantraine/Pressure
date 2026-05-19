using Godot;

// À attacher au CanvasLayer enfant de PorteRonde (ou ailleurs).
// Écoute les signaux du PorteRondeTP (sur Area3DTP) pour afficher un prompt quand
// le joueur est dans la zone, et le cacher pendant/après la téléportation.
public partial class PorteRondeHud : CanvasLayer
{
	[Export] public NodePath TriggerPath;     // Vers le Area3DTP qui porte le script PorteRondeTP
	[Export] public NodePath LabelPath;       // Label à afficher

	// Vide par défaut : doublon avec le libellé du SurvolHud ("Franchir la porte").
	// Conservé en export pour pouvoir réactiver une phrase spécifique si besoin.
	[Export] public string TextePrompt = "";
	[Export] public bool VisibleSeulementDansZone = true;

	// Pilotage raycast : le HUD apparaît quand le joueur regarde la porte ronde.
	// On garde quand même un délai de masquage pour éviter le clignotement quand
	// on tourne brièvement la tête.
	[Export] public float DelaiMasquage = 0.25f;

	private PorteRondeTP trigger;
	private Label label;
	private float tempsHorsRegard = 0f;
	private bool teleportationLancee = false;

	public override void _Ready()
	{
		if (TriggerPath != null && !TriggerPath.IsEmpty)
			trigger = GetNodeOrNull<PorteRondeTP>(TriggerPath);

		if (LabelPath != null && !LabelPath.IsEmpty)
			label = GetNodeOrNull<Label>(LabelPath);

		if (label != null)
			label.Text = TextePrompt;

		// On garde uniquement TeleportationDeclenchee : visibilité passée au raycast.
		if (trigger != null)
			trigger.TeleportationDeclenchee += OnTeleportationDeclenchee;

		if (VisibleSeulementDansZone)
			Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!VisibleSeulementDansZone || trigger == null || teleportationLancee)
			return;

		bool regarde = Survol3D.CibleCourante == trigger;
		if (regarde)
		{
			tempsHorsRegard = 0f;
			Visible = true;
		}
		else
		{
			tempsHorsRegard += (float)delta;
			if (tempsHorsRegard >= DelaiMasquage)
				Visible = false;
		}
	}

	private void OnTeleportationDeclenchee()
	{
		// Cache le HUD dès le début de la séquence pour qu'il ne reste pas affiché
		// par-dessus le fondu noir, et bloque le pilotage raycast pour la suite.
		teleportationLancee = true;
		Visible = false;
	}
}
