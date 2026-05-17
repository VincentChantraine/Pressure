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

	private PorteRondeTP trigger;
	private Label label;

	public override void _Ready()
	{
		if (TriggerPath != null && !TriggerPath.IsEmpty)
			trigger = GetNodeOrNull<PorteRondeTP>(TriggerPath);

		if (LabelPath != null && !LabelPath.IsEmpty)
			label = GetNodeOrNull<Label>(LabelPath);

		if (label != null)
			label.Text = TextePrompt;

		if (trigger != null)
		{
			trigger.JoueurEntreZone += OnJoueurEntreZone;
			trigger.JoueurSortZone += OnJoueurSortZone;
			trigger.TeleportationDeclenchee += OnTeleportationDeclenchee;
		}

		if (VisibleSeulementDansZone)
			Visible = false;
	}

	private void OnJoueurEntreZone()
	{
		if (VisibleSeulementDansZone)
			Visible = true;
	}

	private void OnJoueurSortZone()
	{
		if (VisibleSeulementDansZone)
			Visible = false;
	}

	private void OnTeleportationDeclenchee()
	{
		// Cache le HUD dès le début de la séquence pour qu'il ne reste pas affiché
		// par-dessus le fondu noir.
		Visible = false;
	}
}
