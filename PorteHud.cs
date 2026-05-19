using Godot;

// À attacher à un CanvasLayer, UN par porte.
// Enfants attendus (tous optionnels, le HUD s'adapte) :
// - Label "Titre" (ex: "PORTE 1")
// - Label "Etat" (🔒 Verrouillée / 🔓 Déverrouillée)
// - Label "BadgeRequis" (ex: "Badge 1 requis" ou "Badge 2 + 6/6 salles")
// - Label "Message" (feedback scan éphémère)
// - TextureRect "Icone" (verrou fermé / ouvert)
public partial class PorteHud : BasePuzzleHud
{
	[Export] public NodePath PortePath;
	[Export] public NodePath TitreLabelPath;
	[Export] public NodePath EtatLabelPath;
	[Export] public NodePath BadgeRequisLabelPath;
	[Export] public NodePath IconePath;

	[Export] public Texture2D ImageVerrouFerme;
	[Export] public Texture2D ImageVerrouOuvert;

	private Porte porte;
	private Label titre;
	private Label etatLabel;
	private Label badgeRequisLabel;
	private TextureRect icone;

	protected override void OnHudReady()
	{
		if (PortePath != null && !PortePath.IsEmpty)
			porte = GetNode<Porte>(PortePath);

		if (TitreLabelPath != null) titre = GetNodeOrNull<Label>(TitreLabelPath);
		if (EtatLabelPath != null) etatLabel = GetNodeOrNull<Label>(EtatLabelPath);
		if (BadgeRequisLabelPath != null) badgeRequisLabel = GetNodeOrNull<Label>(BadgeRequisLabelPath);
		if (IconePath != null) icone = GetNodeOrNull<TextureRect>(IconePath);

		if (porte != null)
		{
			// Visibilité pilotée par le raycast Survol3D : le HUD apparaît
			// quand le joueur regarde la porte. On ne s'abonne plus aux signaux
			// de zone pour la visibilité.
			CibleSurvol = porte;

			porte.PorteDeverrouillee += OnPorteDeverrouillee;
			porte.PorteOuverte += OnPorteOuverte;
			porte.PorteFermee += OnPorteFermee;
			porte.ScanRefuse += OnScanRefuse;

			// État initial
			RafraichirTitre();
			RafraichirEtat();
			RafraichirBadgeRequis();
			RafraichirIcone();
		}
	}

	protected override void OnHudProcess(double delta)
	{
		// Rafraîchissement dynamique : le statut du prérequis peut changer pendant
		// que le joueur est dans la zone (porte finale, ou porte avec salle prérequise).
		if (porte != null && !porte.EstDeverrouille
			&& (porte.EstPorteFinale || !string.IsNullOrEmpty(porte.SallePrerequise)))
			RafraichirBadgeRequis();
	}

	private void RafraichirTitre()
	{
		if (titre == null || porte == null) return;
		string nom = porte.PorteId.ToUpper().Replace("_", " ");
		titre.Text = porte.EstPorteFinale ? $"🏁 {nom}" : $"🚪 {nom}";
	}

	private void RafraichirEtat()
	{
		if (etatLabel == null || porte == null) return;
		if (porte.EstDeverrouille)
			etatLabel.Text = porte.EstOuverte ? "🔓 Ouverte" : "🔓 Déverrouillée";
		else
			etatLabel.Text = "🔒 Verrouillée";
	}

	private void RafraichirBadgeRequis()
	{
		if (badgeRequisLabel == null || porte == null) return;

		if (porte.EstDeverrouille)
		{
			badgeRequisLabel.Text = "";
			return;
		}

		if (porte.EstPorteFinale)
		{
			int visitees = GameState.Instance?.SallesVisitees.Count ?? 0;
			string check = visitees >= 6 ? "✓" : "✗";
			badgeRequisLabel.Text = $"Badge 2  |  {visitees}/6 {check}";
		}
		else if (porte.NecessiteBadge1)
		{
			if (!string.IsNullOrEmpty(porte.SallePrerequise))
			{
				string check = porte.PrerequisRempli ? "✓" : "✗";
				string nomSalle = porte.NumeroSallePrerequise > 0
					? $"S{porte.NumeroSallePrerequise}"
					: porte.SallePrerequise.Replace("_", " ");
				badgeRequisLabel.Text = $"Badge 1  |  {nomSalle} {check}";
			}
			else
			{
				badgeRequisLabel.Text = "Badge 1";
			}
		}
		else
		{
			badgeRequisLabel.Text = "";
		}
	}

	private void RafraichirIcone()
	{
		if (icone == null) return;
		if (porte == null) return;
		if (porte.EstDeverrouille && ImageVerrouOuvert != null)
			icone.Texture = ImageVerrouOuvert;
		else if (ImageVerrouFerme != null)
			icone.Texture = ImageVerrouFerme;
	}

	protected override void OnHudVisible()
	{
		// Rafraîchit l'état affiché quand le HUD apparaît (le statut peut avoir
		// changé entre deux survols : badge récupéré, salle prérequise validée…).
		RafraichirEtat();
		RafraichirBadgeRequis();
	}

	private void OnPorteDeverrouillee()
	{
		RafraichirEtat();
		RafraichirBadgeRequis();
		RafraichirIcone();
	}

	private void OnPorteOuverte()
	{
		RafraichirEtat();
	}

	private void OnPorteFermee()
	{
		RafraichirEtat();
	}

	private void OnScanRefuse(string raison)
	{
		ShowMessage($"✗ {raison}", 2.0f);
	}
}
