using Godot;
using System.Text;

// À attacher à un CanvasLayer.
// Enfants attendus :
// - Label "Titre" (ex: "COFFRE")
// - Label "Combinaison" (affiche la séquence attendue, ou des ? si masquée)
// - Label "Progression" (affiche le chiffre courant + crans + sens)
// - Label "Message" (succès / échec)
// - TextureRect "VerrouImage" (affiche le verrou fermé puis le code après ouverture)
public partial class CoffreHud : CanvasLayer
{
	[Export] public NodePath CoffrePath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath CombinaisonLabelPath;
	[Export] public NodePath ProgressionLabelPath;
	[Export] public NodePath MessageLabelPath;
	[Export] public NodePath VerrouImagePath;

	// Les deux images à glisser dans l'inspecteur.
	[Export] public Texture2D ImageVerrou;   // affichée tant que le coffre est fermé
	[Export] public Texture2D ImageCode;     // affichée une fois le coffre ouvert

	// Si true : affiche "? ?→ ?←" au lieu des vrais chiffres.
	// À utiliser quand la combinaison sera écrite sur un mur ailleurs.
	[Export] public bool MasquerCombinaison = false;

	// Si true : la HUD n'est visible que quand le joueur est dans la zone du coffre.
	[Export] public bool VisibleSeulementDansZone = true;

	private Coffre coffre;
	private Label titre;
	private Label combinaisonLabel;
	private Label progressionLabel;
	private Label messageLabel;
	private TextureRect verrouImage;

	private float messageTimer = 0f;

	public override void _Ready()
	{
		if (CoffrePath != null && !CoffrePath.IsEmpty)
			coffre = GetNode<Coffre>(CoffrePath);
		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (CombinaisonLabelPath != null) combinaisonLabel = GetNodeOrNull<Label>(CombinaisonLabelPath);
		if (ProgressionLabelPath != null) progressionLabel = GetNodeOrNull<Label>(ProgressionLabelPath);
		if (MessageLabelPath != null) messageLabel = GetNodeOrNull<Label>(MessageLabelPath);
		if (VerrouImagePath != null) verrouImage = GetNodeOrNull<TextureRect>(VerrouImagePath);

		if (coffre != null)
		{
			coffre.ChiffreValide += OnChiffreValide;
			coffre.CombinaisonRatee += OnCombinaisonRatee;
			coffre.CoffreOuvert += OnCoffreOuvert;
			coffre.ProgressionChangee += OnProgressionChangee;
			coffre.JoueurEntreZone += OnJoueurEntreZone;
			coffre.JoueurSortZone += OnJoueurSortZone;

			AfficherCombinaison();
		}

		if (titre != null) titre.Text = "🔒 COFFRE";
		if (messageLabel != null) messageLabel.Text = "";

		// Image : verrou par défaut, cachée au départ (n'apparaît que dans la zone)
		if (verrouImage != null)
		{
			verrouImage.Texture = ImageVerrou;
			verrouImage.Visible = false;
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

	private void AfficherCombinaison()
	{
		if (combinaisonLabel == null || coffre == null) return;
		var combi = coffre.GetCombinaison();
		var sb = new StringBuilder();
		for (int i = 0; i < combi.Length; i++)
		{
			string fleche = (i % 2 == 0) ? "←" : "→";
			string chiffre = MasquerCombinaison ? "?" : combi[i].ToString();
			sb.Append($"{chiffre}{fleche}");
			if (i < combi.Length - 1) sb.Append("  ");
		}
		combinaisonLabel.Text = sb.ToString();
	}

	private void OnProgressionChangee(int idx, int crans, int sensAttendu)
	{
		if (progressionLabel == null || coffre == null) return;
		var combi = coffre.GetCombinaison();
		string cibleChiffre = MasquerCombinaison ? "?" : combi[idx].ToString();
		string cibleFleche = (sensAttendu < 0 ? "←" : "→");
		string sensActuel = crans == 0 ? "·" : (crans < 0 ? "←" : "→");
		progressionLabel.Text = $"#{idx + 1}/{combi.Length}   cible: {cibleChiffre}{cibleFleche}   en cours: {Mathf.Abs(crans)}{sensActuel}";
	}

	private void OnChiffreValide(int idx)
	{
		ShowMessage($"✓ Chiffre {idx + 1} validé", 1.2f);
	}

	private void OnCombinaisonRatee(string raison)
	{
		ShowMessage($"✗ {raison}", 2.0f);
	}

	private void OnCoffreOuvert()
	{
		ShowMessage("🔓 COFFRE OUVERT !", 10f);
		if (progressionLabel != null) progressionLabel.Text = "";

		// Bascule l'image : verrou → code
		if (verrouImage != null && ImageCode != null)
			verrouImage.Texture = ImageCode;
	}

	private void OnJoueurEntreZone()
	{
		if (VisibleSeulementDansZone) Visible = true;
		if (verrouImage != null) verrouImage.Visible = true;
	}

	private void OnJoueurSortZone()
	{
		if (VisibleSeulementDansZone) Visible = false;
		// On garde verrouImage.Visible tel quel, il sera caché avec tout le CanvasLayer
	}

	private void ShowMessage(string msg, float duration)
	{
		if (messageLabel != null) messageLabel.Text = msg;
		messageTimer = duration;
	}
}
