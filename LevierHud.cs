using Godot;

// À attacher à un CanvasLayer.
// Enfants attendus (tous optionnels) :
// - Label "Titre" (ex: "LEVIER DE PURGE")
// - Label "Progression" (étape courante / total)
// - Label "Message" (succès/échec éphémère)
// - TextureRect "Icone" (levier en position / levier activé)
// - ProgressBar "Manometre" (jauge 0 → Sequence.Length)
public partial class LevierHud : CanvasLayer
{
	[Export] public NodePath LevierPath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath ProgressionLabelPath;
	[Export] public NodePath MessageLabelPath;
	[Export] public NodePath IconePath;
	[Export] public NodePath ManometrePath;

	[Export] public Texture2D ImageLevierRepos;
	[Export] public Texture2D ImageLevierActive;

	[Export] public bool VisibleSeulementDansZone = true;
	
	[Export(PropertyHint.MultilineText)] public string IndiceSuivant = "Va voir dans la salle 2 et sois à l'écoute.";
	[Export] public float DureeMessageActivation = 12f;

	private LevierSequence levier;
	private Label titre;
	private Label progressionLabel;
	private Label messageLabel;
	private TextureRect icone;
	private ProgressBar manometre;

	private float messageTimer = 0f;

	public override void _Ready()
	{
		if (LevierPath != null && !LevierPath.IsEmpty)
			levier = GetNode<LevierSequence>(LevierPath);

		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (ProgressionLabelPath != null) progressionLabel = GetNodeOrNull<Label>(ProgressionLabelPath);
		if (MessageLabelPath != null) messageLabel = GetNodeOrNull<Label>(MessageLabelPath);
		if (IconePath != null) icone = GetNodeOrNull<TextureRect>(IconePath);
		if (ManometrePath != null) manometre = GetNodeOrNull<ProgressBar>(ManometrePath);

		if (levier != null)
		{
			levier.EtapeValidee += OnEtapeValidee;
			levier.SequenceRatee += OnSequenceRatee;
			levier.LevierActive += OnLevierActive;
			levier.ProgressionChangee += OnProgressionChangee;
			levier.JoueurEntreZone += OnJoueurEntreZone;
			levier.JoueurSortZone += OnJoueurSortZone;

			if (manometre != null)
			{
				manometre.MinValue = 0;
				manometre.MaxValue = levier.GetSequence().Length;
				manometre.Value = 0;
			}
		}

		if (titre != null) titre.Text = "⚙ LEVIER";
		if (messageLabel != null) messageLabel.Text = "";

		if (icone != null)
		{
			icone.Texture = ImageLevierRepos;
			icone.Visible = false;
		}

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

	private void OnProgressionChangee(int idx, int sensAttendu)
	{
		if (levier == null) return;
		var seq = levier.GetSequence();
		if (progressionLabel != null)
			progressionLabel.Text = $"Étape {idx + 1}/{seq.Length}";
		if (manometre != null)
			manometre.Value = idx;
	}

	private void OnEtapeValidee(int idx)
	{
		ShowMessage($"✓ Étape {idx + 1}", 1.2f);
	}

	private void OnSequenceRatee(string raison)
	{
		ShowMessage($"✗ {raison}", 2.0f);
		if (manometre != null) manometre.Value = 0;
	}

	private void OnLevierActive(string levierId)
	{
		string msg = "🟢 LEVIER ACTIVÉ — fuite ralentie";
		if (!string.IsNullOrEmpty(IndiceSuivant))
			msg += "\n➤ " + IndiceSuivant;

		ShowMessage(msg, DureeMessageActivation);

		if (progressionLabel != null) progressionLabel.Text = "";
		if (manometre != null && levier != null)
			manometre.Value = levier.GetSequence().Length;
		if (icone != null && ImageLevierActive != null)
			icone.Texture = ImageLevierActive;
	}

	private void OnJoueurEntreZone()
	{
		if (VisibleSeulementDansZone) Visible = true;
		if (icone != null) icone.Visible = true;
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
