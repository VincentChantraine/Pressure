using Godot;

// À attacher à un CanvasLayer.
// Enfants attendus (tous optionnels, le HUD s'adapte) :
// - Label "Titre" (ex: "LABYRINTHE")
// - Label "Instructions" (ex: "← ↑ → ↓ pour déplacer la bille")
// - Label "Etat" (🎯 En cours / ✓ Réussi)
// - Label "Message" (succès éphémère)
// - TextureRect "Icone" (image labyrinthe puis image révélée à la réussite)
public partial class LabyrintheHud : CanvasLayer
{
	[Export] public NodePath LabyrinthePath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath InstructionsLabelPath;
	[Export] public NodePath EtatLabelPath;
	[Export] public NodePath MessageLabelPath;
	[Export] public NodePath IconePath;

	// Les deux images à glisser dans l'inspecteur.
	[Export] public Texture2D ImageLabyrinthe;   // affichée tant que l'épreuve n'est pas réussie
	[Export] public Texture2D ImageReussite;     // affichée une fois la sortie atteinte

	// Si true : la HUD n'est visible que quand le joueur est dans la zone du labyrinthe.
	[Export] public bool VisibleSeulementDansZone = true;

	[Export(PropertyHint.MultilineText)] public string IndiceSuivant = "Fonce à la sortie.";
	[Export] public float DureeMessageReussite = 12f;

	private LabyrintheBille labyrinthe;
	private Label titre;
	private Label instructionsLabel;
	private Label etatLabel;
	private Label messageLabel;
	private TextureRect icone;

	private float messageTimer = 0f;

	public override void _Ready()
	{
		if (LabyrinthePath != null && !LabyrinthePath.IsEmpty)
			labyrinthe = GetNode<LabyrintheBille>(LabyrinthePath);
		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (InstructionsLabelPath != null) instructionsLabel = GetNodeOrNull<Label>(InstructionsLabelPath);
		if (EtatLabelPath != null) etatLabel = GetNodeOrNull<Label>(EtatLabelPath);
		if (MessageLabelPath != null) messageLabel = GetNodeOrNull<Label>(MessageLabelPath);
		if (IconePath != null) icone = GetNodeOrNull<TextureRect>(IconePath);

		if (labyrinthe != null)
		{
			labyrinthe.JoueurEntreZone += OnJoueurEntreZone;
			labyrinthe.JoueurSortZone += OnJoueurSortZone;
			labyrinthe.EpreuveReussie += OnEpreuveReussie;
			labyrinthe.BilleReset += OnBilleReset;
		}

		if (titre != null) titre.Text = "🧩 LABYRINTHE";
		if (instructionsLabel != null) instructionsLabel.Text = "← ↑ → ↓  déplace la bille";
		if (messageLabel != null) messageLabel.Text = "";

		RafraichirEtat();

		// Image : labyrinthe par défaut, cachée au départ (n'apparaît que dans la zone)
		if (icone != null)
		{
			icone.Texture = ImageLabyrinthe;
			icone.Visible = false;
		}

		// État initial : caché si on attend d'être dans la zone
		if (VisibleSeulementDansZone)
			Visible = false;
	}

	public override void _Process(double delta)
	{
		if (messageTimer > 0f)
		{
			messageTimer -= (float)delta;
			if (messageTimer <= 0f && messageLabel != null)
				messageLabel.Text = "";
		}
	}

	private void RafraichirEtat()
	{
		if (etatLabel == null || labyrinthe == null) return;
		etatLabel.Text = labyrinthe.EstReussie ? "✓ Sortie atteinte" : "🎯 En cours";
	}

	private void OnEpreuveReussie()
	{
		string msg = "🏁 LABYRINTHE RÉUSSI !";
		if (!string.IsNullOrEmpty(IndiceSuivant))
			msg += "\n➤ " + IndiceSuivant;

		ShowMessage(msg, DureeMessageReussite);
		RafraichirEtat();

		if (instructionsLabel != null) instructionsLabel.Text = "";

		// Bascule l'image : labyrinthe → réussite
		if (icone != null && ImageReussite != null)
			icone.Texture = ImageReussite;
	}

	private void OnBilleReset()
	{
		if (labyrinthe != null && !labyrinthe.EstReussie)
			ShowMessage("↺ Bille remise au départ", 1.2f);
	}

	private void OnJoueurEntreZone()
	{
		if (VisibleSeulementDansZone) Visible = true;
		if (icone != null) icone.Visible = true;
		RafraichirEtat();
	}

	private void OnJoueurSortZone()
	{
		if (VisibleSeulementDansZone) Visible = false;
	}

	private void ShowMessage(string msg, float duration)
	{
		if (messageLabel != null) messageLabel.Text = msg;
		messageTimer = duration;
	}
}
