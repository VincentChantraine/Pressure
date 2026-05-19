using Godot;
using System.Text;

// À attacher à un CanvasLayer.
// Enfants attendus :
// - Label "Titre" (ex: "COFFRE")
// - Label "Combinaison" (affiche la séquence attendue, ou des ? si masquée)
// - Label "Progression" (affiche le chiffre courant + crans + sens)
// - Label "Message" (succès / échec)
// - TextureRect "VerrouImage" (affiche le verrou fermé puis le code après ouverture)
public partial class CoffreHud : BasePuzzleHud
{
	[Export] public NodePath CoffrePath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath CombinaisonLabelPath;
	[Export] public NodePath ProgressionLabelPath;
	[Export] public NodePath VerrouImagePath;
	[Export] public NodePath IndicePath;

	// Les deux images à glisser dans l'inspecteur.
	[Export] public Texture2D ImageVerrou;   // affichée tant que le coffre est fermé
	[Export] public Texture2D ImageCode;     // affichée une fois le coffre ouvert

	// Si true : affiche "? ?→ ?←" au lieu des vrais chiffres.
	// À utiliser quand la combinaison sera écrite sur un mur ailleurs.
	[Export] public bool MasquerCombinaison = false;

	private Coffre coffre;
	private Label titre;
	private Label combinaisonLabel;
	private Label progressionLabel;
	private TextureRect verrouImage;
	private TextureRect indice;

	protected override void OnHudReady()
	{
		if (CoffrePath != null && !CoffrePath.IsEmpty)
			coffre = GetNode<Coffre>(CoffrePath);
		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (CombinaisonLabelPath != null) combinaisonLabel = GetNodeOrNull<Label>(CombinaisonLabelPath);
		if (ProgressionLabelPath != null) progressionLabel = GetNodeOrNull<Label>(ProgressionLabelPath);
		if (VerrouImagePath != null) verrouImage = GetNodeOrNull<TextureRect>(VerrouImagePath);
		if (IndicePath != null) indice = GetNodeOrNull<TextureRect>(IndicePath);

		if (coffre != null)
		{
			// Visibilité pilotée par le raycast Survol3D (cf. BasePuzzleHud).
			CibleSurvol = coffre;

			coffre.ChiffreValide += OnChiffreValide;
			coffre.CombinaisonRatee += OnCombinaisonRatee;
			coffre.CoffreOuvert += OnCoffreOuvert;
			coffre.ProgressionChangee += OnProgressionChangee;

			AfficherCombinaison();
		}

		if (titre != null) titre.Text = "🔒 COFFRE";

		// Image : verrou par défaut, cachée au départ (n'apparaît que dans la zone)
		if (verrouImage != null)
		{
			verrouImage.Texture = ImageVerrou;
			verrouImage.Visible = false;
		}

		if (indice != null) indice.Visible = false;
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
		progressionLabel.Text = $"#{idx + 1}/{combi.Length}   {cibleChiffre}{cibleFleche}   {Mathf.Abs(crans)}{sensActuel}";
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

		if (verrouImage != null) verrouImage.Visible = false;
		if (indice != null) indice.Visible = true;
	}

	protected override void OnHudVisible()
	{
		if (verrouImage != null) verrouImage.Visible = true;
	}

	// Visibilité du HUD désormais pilotée par le raycast (cf. BasePuzzleHud) :
	// verrouImage est rendue visible en même temps que le CanvasLayer.
}
