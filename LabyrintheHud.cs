using Godot;

// À attacher à un CanvasLayer.
// Enfants attendus (tous optionnels, le HUD s'adapte) :
// - Label "Titre" (ex: "LABYRINTHE")
// - Label "Instructions" (ex: "← ↑ → ↓ pour déplacer la bille")
// - Label "Etat" (🎯 En cours / ✓ Réussi)
// - Label "Message" (succès éphémère)
// - TextureRect "Icone" (image labyrinthe puis image révélée à la réussite)
public partial class LabyrintheHud : BasePuzzleHud
{
	[Export] public NodePath LabyrinthePath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath InstructionsLabelPath;
	[Export] public NodePath ResetHintLabelPath;
	[Export] public NodePath EtatLabelPath;
	[Export] public NodePath IconePath;

	// Les deux images à glisser dans l'inspecteur.
	[Export] public Texture2D ImageLabyrinthe;   // affichée tant que l'épreuve n'est pas réussie
	[Export] public Texture2D ImageReussite;     // affichée une fois la sortie atteinte

	[Export(PropertyHint.MultilineText)] public string IndiceSuivant = "Récupère le badge 2.";
	[Export] public float DureeMessageReussite = 12f;

	private LabyrintheBille labyrinthe;
	private Label titre;
	private Label instructionsLabel;
	private Label resetHintLabel;
	private Label etatLabel;
	private TextureRect icone;

	protected override void OnHudReady()
	{
		if (LabyrinthePath != null && !LabyrinthePath.IsEmpty)
			labyrinthe = GetNode<LabyrintheBille>(LabyrinthePath);
		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (InstructionsLabelPath != null) instructionsLabel = GetNodeOrNull<Label>(InstructionsLabelPath);
		if (ResetHintLabelPath != null) resetHintLabel = GetNodeOrNull<Label>(ResetHintLabelPath);
		if (EtatLabelPath != null) etatLabel = GetNodeOrNull<Label>(EtatLabelPath);
		if (IconePath != null) icone = GetNodeOrNull<TextureRect>(IconePath);

		if (labyrinthe != null)
		{
			labyrinthe.JoueurEntreZone += OnJoueurEntreZone;
			labyrinthe.JoueurSortZone += OnJoueurSortZone;
			labyrinthe.EpreuveReussie += OnEpreuveReussie;
			labyrinthe.BilleReset += OnBilleReset;
		}

		if (titre != null) titre.Text = "🧩 LABYRINTHE";
		if (instructionsLabel != null) instructionsLabel.Text = "← ↑ → ↓  déplacer";
		if (resetHintLabel != null) resetHintLabel.Text = "[R]  réinitialise la bille";

		RafraichirEtat();

		// Image : labyrinthe par défaut, cachée au départ (n'apparaît que dans la zone)
		if (icone != null)
		{
			icone.Texture = ImageLabyrinthe;
			icone.Visible = false;
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
		if (resetHintLabel != null) resetHintLabel.Text = "";

		// Bascule l'image : labyrinthe → réussite
		if (icone != null && ImageReussite != null)
			icone.Texture = ImageReussite;
	}

	private void OnBilleReset()
	{
		if (labyrinthe != null && !labyrinthe.EstReussie)
			ShowMessage("↺ Bille remise au départ", 1.2f);
	}

	protected override void OnJoueurEntreZone()
	{
		base.OnJoueurEntreZone();
		if (icone != null) icone.Visible = true;
		RafraichirEtat();
	}
}
