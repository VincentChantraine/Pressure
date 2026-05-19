using Godot;

// À attacher à un CanvasLayer.
// Enfants attendus (tous optionnels) :
// - Label "Titre"      (ex: "COMMANDES")
// - Label "Prompt"     (ex: "Appuie sur le bouton joystick pour reprendre les commandes")
// - TextureRect "Icone" (image à afficher quand le joueur est dans la zone)
public partial class VictoireCommandeHud : CanvasLayer
{
	[Export] public NodePath TriggerPath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath PromptLabelPath;
	[Export] public NodePath IconePath;

	[Export] public Texture2D ImageCommande;

	[Export] public string TitreTexte = "🎛️ COMMANDES";
	// Vide par défaut : doublon avec le libellé du SurvolHud
	// ("Reprendre les commandes"). Le titre, lui, reste unique.
	[Export] public string PromptTexte = "";

	// Si true : HUD visible uniquement quand le joueur regarde les commandes
	// (pilotage raycast Survol3D — anciennement piloté par la zone d'Area3D).
	[Export] public bool VisibleSeulementDansZone = true;
	[Export] public float DelaiMasquage = 0.25f;

	private VictoireCommandeTrigger trigger;
	private Label titre;
	private Label prompt;
	private TextureRect icone;
	private float tempsHorsRegard = 0f;
	private bool etaitVisible = false;

	public override void _Ready()
	{
		if (TriggerPath != null && !TriggerPath.IsEmpty)
			trigger = GetNodeOrNull<VictoireCommandeTrigger>(TriggerPath);

		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (PromptLabelPath != null) prompt = GetNodeOrNull<Label>(PromptLabelPath);
		if (IconePath != null) icone = GetNodeOrNull<TextureRect>(IconePath);

		if (titre != null) titre.Text = TitreTexte;
		if (prompt != null) prompt.Text = PromptTexte;

		if (icone != null)
		{
			if (ImageCommande != null) icone.Texture = ImageCommande;
			icone.Visible = false;
		}

		// Visibilité pilotée par le raycast (cf. _Process). On garde seulement
		// le signal de victoire pour mettre à jour le prompt.
		if (trigger != null)
			trigger.VictoireDeclenchee += OnVictoireDeclenchee;
		else
			GD.PrintErr("[VictoireCommandeHud] TriggerPath non assigné ou nœud introuvable.");

		if (VisibleSeulementDansZone)
			Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!VisibleSeulementDansZone || trigger == null) return;

		bool regarde = Survol3D.CibleCourante == trigger;
		if (regarde)
		{
			tempsHorsRegard = 0f;
			if (!etaitVisible)
			{
				Visible = true;
				if (icone != null) icone.Visible = true;
				if (prompt != null) prompt.Text = PromptTexte;
				etaitVisible = true;
			}
		}
		else
		{
			tempsHorsRegard += (float)delta;
			if (etaitVisible && tempsHorsRegard >= DelaiMasquage)
			{
				Visible = false;
				if (icone != null) icone.Visible = false;
				etaitVisible = false;
			}
		}
	}

	private void OnVictoireDeclenchee()
	{
		if (prompt != null) prompt.Text = "✓ Commandes reprises";
	}
}
