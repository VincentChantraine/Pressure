using Godot;
 
// À attacher à un CanvasLayer.
// Enfants attendus (tous optionnels) :
// - Label "Titre"          → ex: "🔧 VANNE"
// - Label "Progression"    → ex: "⟳ 5/12 quarts  (1 tour + 1/4)"
// - ProgressBar "Barre"    → 0..100, rempli au fur et à mesure
// - Label "Message"        → succès / feedback transitoire
// - TextureRect "VanneImage" → visuel de vanne ouverte / fermée (optionnel)
public partial class VanneHud : CanvasLayer
{
	[Export] public NodePath VannePath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath ProgressionLabelPath;
	[Export] public NodePath BarrePath;
	[Export] public NodePath MessageLabelPath;
	[Export] public NodePath VanneImagePath;
 
	// Images optionnelles (comme CoffreHud).
	[Export] public Texture2D ImageVanneOuverte;   // tant que la vanne fuit
	[Export] public Texture2D ImageVanneFermee;    // une fois fermée
 
	// Si true : la HUD n'est visible que quand le joueur est dans la zone.
	[Export] public bool VisibleSeulementDansZone = true;
	
	[Export(PropertyHint.MultilineText)] public string IndiceSuivant = "La salle 6 a besoin de toi.";
	[Export] public float DureeMessageFermeture = 12f;
 
	private VanneRotation vanne;
	private Label titre;
	private Label progressionLabel;
	private ProgressBar barre;
	private Label messageLabel;
	private TextureRect vanneImage;
 
	private float messageTimer = 0f;
 
	public override void _Ready()
	{
		if (VannePath != null && !VannePath.IsEmpty)
			vanne = GetNode<VanneRotation>(VannePath);
		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (ProgressionLabelPath != null) progressionLabel = GetNodeOrNull<Label>(ProgressionLabelPath);
		if (BarrePath != null) barre = GetNodeOrNull<ProgressBar>(BarrePath);
		if (MessageLabelPath != null) messageLabel = GetNodeOrNull<Label>(MessageLabelPath);
		if (VanneImagePath != null) vanneImage = GetNodeOrNull<TextureRect>(VanneImagePath);
 
		if (vanne != null)
		{
			vanne.QuartValide += OnQuartValide;
			vanne.VanneFermee += OnVanneFermee;
			vanne.JoueurEntreZone += OnJoueurEntreZone;
			vanne.JoueurSortZone += OnJoueurSortZone;
		}
 
		if (titre != null) titre.Text = "🔧 VANNE";
		if (messageLabel != null) messageLabel.Text = "";
 
		if (barre != null)
		{
			barre.MinValue = 0;
			barre.MaxValue = 100;
			barre.Value = 0;
		}
 
		// État initial de l'image
		if (vanneImage != null)
		{
			vanneImage.Texture = ImageVanneOuverte;
			vanneImage.Visible = false;
		}
 
		// Affichage initial de la progression (0/12)
		RafraichirProgression(0);
 
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
 
	private void OnQuartValide(int quartsTotal, int quartsRequis)
	{
		RafraichirProgression(quartsTotal);
 
		// Petit feedback transitoire à chaque tour complet (tous les 4 quarts)
		if (quartsTotal > 0 && quartsTotal % 4 == 0 && quartsTotal < quartsRequis)
		{
			int tour = quartsTotal / 4;
			int tourMax = quartsRequis / 4;
			ShowMessage($"✓ Tour {tour}/{tourMax}", 1.0f);
		}
	}
 
	private void RafraichirProgression(int quartsTotal)
	{
		if (vanne == null) return;
		int requis = vanne.QuartsRequis;
 
		if (progressionLabel != null)
		{
			int toursPleins = quartsTotal / 4;
			int quartsRestants = quartsTotal % 4;
			string detail = "";
			if (toursPleins > 0 && quartsRestants > 0)
				detail = $"  ({toursPleins} tour{(toursPleins > 1 ? "s" : "")} + {quartsRestants}/4)";
			else if (toursPleins > 0)
				detail = $"  ({toursPleins} tour{(toursPleins > 1 ? "s" : "")})";
 
			progressionLabel.Text = $"⟳ {quartsTotal}/{requis}{detail}";
		}
 
		if (barre != null)
			barre.Value = requis > 0 ? (quartsTotal / (float)requis) * 100f : 0f;
	}
 
	private void OnVanneFermee(string vanneId)
	{
		string msg = "🔧 FUITE COLMATÉE !";
		if (!string.IsNullOrEmpty(IndiceSuivant))
			msg += "\n➤ " + IndiceSuivant;

		ShowMessage(msg, DureeMessageFermeture);

		if (progressionLabel != null)
			progressionLabel.Text = $"⟳ {vanne.QuartsRequis}/{vanne.QuartsRequis} ✓";

		if (barre != null) barre.Value = 100f;

		if (vanneImage != null && ImageVanneFermee != null)
			vanneImage.Texture = ImageVanneFermee;
	}
 
	private void OnJoueurEntreZone()
	{
		if (VisibleSeulementDansZone) Visible = true;
		if (vanneImage != null) vanneImage.Visible = true;
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
 
