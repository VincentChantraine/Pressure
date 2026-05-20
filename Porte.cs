using Godot;

public partial class Porte : Node3D
{
	[Export] public NodePath PivotPath;
	[Export] public NodePath ArduinoPath;
	// Lecteur de badge placé à côté de la porte (Node3D).
	// Si renseigné, la boussole pointe vers le lecteur tant que la porte est
	// verrouillée, puis bascule sur la porte elle-même au déverrouillage.
	// Le lecteur est aussi ajouté au groupe "interactif" tant que verrouillé
	// (highlight + libellé "Scanner le badge" côté survol 3D).
	// Vide → comportement legacy : la porte est toujours l'ancre.
	[Export] public NodePath LecteurBadgePath;

	// --- Sons (à assigner dans l'inspecteur) ---
	[Export] public AudioStream SonOuverture;
	[Export] public AudioStream SonFermeture;
	[Export] public AudioStream SonDeverrouillage;
	[Export] public AudioStream SonRefus;

	// --- Volumes (en dB) ---
	[Export] public float VolumeDbOuverture = -32f;
	[Export] public float VolumeDbFermeture = -32f;
	[Export] public float VolumeDbDeverrouillage = -28f;
	[Export] public float VolumeDbRefus = 0f;

	[Export] public float AngleOuvert = 90.0f;
	[Export] public float VitesseOuverture = 3.0f;

	[Export] public string PorteId = "porte_1";
	[Export] public bool NecessiteBadge1 = true;
	[Export] public bool EstPorteFinale = false;

	// Salle qui doit avoir été visitée avant que cette porte puisse être déverrouillée.
	// Vide = pas de prérequis. Format : "salle_1", "salle_3", etc.
	[Export] public string SallePrerequise = "";
	// Numéro affiché dans le HUD pour la salle prérequise (1..6). 0 = pas affiché.
	[Export] public int NumeroSallePrerequise = 0;

	// Salle située DE L'AUTRE CÔTÉ de cette porte (ex: "salle_1" si la porte mène
	// vers la salle 1). Vide = porte sans salle associée (porte interne, porte finale).
	// Utilisé par la boussole : elle pointe vers cette porte tant que la salle n'est
	// pas validée. Préféré aux puzzles eux-mêmes pour éviter de spoiler la solution.
	[Export] public string SalleQueProtege = "";

	[Signal] public delegate void PorteDeverrouilleeEventHandler();
	[Signal] public delegate void PorteOuverteEventHandler();
	[Signal] public delegate void PorteFermeeEventHandler();
	[Signal] public delegate void ScanRefuseEventHandler(string raison);

	private Node3D pivot;
	private Node3d arduino;  // ⚠️ vérifie que ta classe s'appelle bien Node3d (pas Node3D)
	private Node3D lecteurBadge;

	// Mémorisé en _Ready pour pouvoir basculer l'ancre boussole entre lecteur
	// et porte sans recalculer la convention PorteId → salle.
	private string salleCibleBoussole = "";

	// Players audio créés en code, un par son pour permettre le chevauchement
	private AudioStreamPlayer3D playerOuverture;
	private AudioStreamPlayer3D playerFermeture;
	private AudioStreamPlayer3D playerDeverrouillage;
	private AudioStreamPlayer3D playerRefus;

	private bool estOuverte = false;
	private bool estDeverrouille = false;
	private bool boutonPrecedent = false;

	public bool EstOuverte => estOuverte;
	public bool EstDeverrouille => estDeverrouille;

	public bool PrerequisRempli =>
		string.IsNullOrEmpty(SallePrerequise)
		|| (GameState.Instance != null && GameState.Instance.SallesVisitees.Contains(SallePrerequise));

	public override void _Ready()
	{
		pivot = GetNode<Node3D>(PivotPath);
		arduino = GetNode<Node3d>(ArduinoPath);
		if (LecteurBadgePath != null && !LecteurBadgePath.IsEmpty)
			lecteurBadge = GetNodeOrNull<Node3D>(LecteurBadgePath);

		// Création des players audio
		playerOuverture = CreerPlayer(SonOuverture, VolumeDbOuverture);
		playerFermeture = CreerPlayer(SonFermeture, VolumeDbFermeture);
		playerDeverrouillage = CreerPlayer(SonDeverrouillage, VolumeDbDeverrouillage);
		playerRefus = CreerPlayer(SonRefus, VolumeDbRefus);

		if (!NecessiteBadge1 && !EstPorteFinale)
			estDeverrouille = true;

		// Registre la cible boussole pour la salle qu'elle protège.
		// Écrase l'ancre puzzle (placée par le puzzle dans son _Ready) — c'est voulu :
		// pointer vers la porte / le lecteur est moins spoilant que pointer vers
		// l'objet à manipuler.
		// Fallback : si SalleQueProtege n'est pas configuré dans l'inspecteur, on
		// déduit la salle depuis PorteId (convention "porte_N" → "salle_N").
		// Évite d'avoir à éditer chaque .tscn pour les 6 portes existantes.
		salleCibleBoussole = SalleQueProtege;
		if (string.IsNullOrEmpty(salleCibleBoussole)
			&& !string.IsNullOrEmpty(PorteId)
			&& PorteId.StartsWith("porte_", System.StringComparison.OrdinalIgnoreCase))
		{
			string suffixe = PorteId.Substring("porte_".Length);
			if (int.TryParse(suffixe, out _))
				salleCibleBoussole = "salle_" + suffixe;
		}
		// Cas spécial : la porte de sortie n'a pas de salle_N associée, elle est
		// la cible de la phase "toutes les salles validées" de la boussole.
		// Id réservé "exit_door" — cf. BoussoleHud.
		if (string.IsNullOrEmpty(salleCibleBoussole)
			&& PorteId == "porte_sortie")
		{
			salleCibleBoussole = "exit_door";
		}
		if (string.IsNullOrEmpty(salleCibleBoussole))
		{
			GD.Print($"[Porte] {PorteId} : pas d'ancre boussole (PorteId hors convention 'porte_N').");
		}

		// Survol : la porte est "regardable" — le raycast caméra peut l'afficher
		// comme cible. Le lecteur, s'il est configuré, est ajouté/retiré du groupe
		// dynamiquement dans MettreAJourEtatVerrouillage selon estDeverrouille.
		AddToGroup("interactif");

		// On suit la validation des salles : quand le prérequis devient rempli,
		// on (ré)inscrit l'ancre boussole pour que la flèche aiguille vers cette
		// porte. Cf. MettreAJourEtatVerrouillage.
		if (GameState.Instance != null)
			GameState.Instance.SalleValidee += OnSalleValideeSuivi;

		MettreAJourEtatVerrouillage();
	}

	public override void _ExitTree()
	{
		// Désabonnement obligatoire : GameState est un autoload qui survit à la
		// scène — sans ça, Porte resterait référencée après destruction.
		if (GameState.Instance != null)
			GameState.Instance.SalleValidee -= OnSalleValideeSuivi;
	}

	private void OnSalleValideeSuivi(string salleId)
	{
		// Une salle vient d'être validée ; si c'est notre prérequis, on devient
		// pointable par la boussole → réinscription de l'ancre.
		if (!string.IsNullOrEmpty(SallePrerequise) && salleId == SallePrerequise)
			MettreAJourEtatVerrouillage();
	}

	// Synchronise tout ce qui dépend de l'état verrouillé/déverrouillé : ancre
	// boussole, libellé de survol de la porte, et participation du lecteur au
	// groupe "interactif" (+ son libellé). Appelée en _Ready et à chaque
	// déverrouillage réussi dans TraiterScan.
	private void MettreAJourEtatVerrouillage()
	{
		bool aLecteur = lecteurBadge != null && IsInstanceValid(lecteurBadge);

		// 1. Ancre boussole : lecteur tant que verrouillée, sinon porte.
		// On ne pointe vers cette porte / ce lecteur que si le prérequis de
		// salle est rempli OU si la porte est déjà déverrouillée. Tant que le
		// prérequis manque, scanner ne déclencherait qu'un refus : autant ne
		// pas y aiguiller le joueur. La boussole tombera sur la prochaine salle
		// disponible dans OrdreSalles.
		if (!string.IsNullOrEmpty(salleCibleBoussole) && GameState.Instance != null)
		{
			bool pointable = estDeverrouille || PrerequisRempli;
			if (pointable)
			{
				Node3D ancre = (!estDeverrouille && aLecteur) ? lecteurBadge : (Node3D)this;
				GameState.Instance.RegistrerAncreSalle(salleCibleBoussole, ancre);
			}
			else
			{
				GameState.Instance.AncresSalles.Remove(salleCibleBoussole);
			}
		}

		// 2. Libellé survol 3D de la porte. Quand on a un lecteur dédié et que la
		// porte est verrouillée, la porte elle-même n'est plus l'interaction
		// utile → libellé "Verrouillée" pour aiguiller le joueur vers le lecteur.
		string libellePorte;
		if (estDeverrouille)
			libellePorte = "Ouvrir / Fermer";
		else if (aLecteur)
			libellePorte = "Verrouillée";
		else
			libellePorte = EstPorteFinale ? "Scanner le badge final" : "Scanner le badge";
		SetMeta("libelle_interaction", libellePorte);

		// 3. Lecteur : interactif seulement tant que la porte est verrouillée.
		if (aLecteur)
		{
			if (!estDeverrouille)
			{
				if (!lecteurBadge.IsInGroup("interactif"))
					lecteurBadge.AddToGroup("interactif");
				lecteurBadge.SetMeta("libelle_interaction",
					EstPorteFinale ? "Scanner le badge final" : "Scanner le badge");
			}
			else if (lecteurBadge.IsInGroup("interactif"))
			{
				lecteurBadge.RemoveFromGroup("interactif");
			}
		}
	}

	private AudioStreamPlayer3D CreerPlayer(AudioStream stream, float volumeDb)
	{
		var p = new AudioStreamPlayer3D();
		p.Stream = stream;
		p.VolumeDb = volumeDb;
		// Bus par défaut "Master". Change ici si tu as un bus "SFX" :
		// p.Bus = "SFX";
		AddChild(p);
		return p;
	}

	private void JouerSon(AudioStreamPlayer3D player)
	{
		if (player == null || player.Stream == null) return;
		// Stop() explicite avant Play() : sans ça, des appels rapides à Play()
		// (cas du REFUS quand on bourre les touches 1/2) peuvent laisser
		// l'AudioStreamPlayer3D dans un état où le restart n'émet pas de son,
		// notamment avec des streams MP3. Coffre/Levier font pareil.
		if (player.Playing) player.Stop();
		player.Play();
	}

	public override void _Process(double delta)
	{
		// Scan RFID. Désormais piloté par le raycast : il faut regarder le
		// lecteur (si configuré) ou la porte directement pour qu'un scan soit
		// pris en compte. Évite qu'un appui sur 1/2 ne déverrouille une porte
		// "à l'aveugle" parce qu'on est dans son rayon.
		//  - Avec lecteur : on exige l'appui 1 ou 2 PENDANT que le joueur le
		//    regarde, et on draine la file pour qu'un appui pré-enregistré
		//    n'ouvre pas la porte tout seul au moment où on tourne la tête.
		//  - Sans lecteur : on consomme la file dès que la porte est regardée.
		bool regardeScan = (lecteurBadge != null)
			? Survol3D.CibleCourante == lecteurBadge
			: Survol3D.CibleCourante == this;
		if (regardeScan && !estDeverrouille && GameState.Instance != null)
		{
			if (lecteurBadge != null)
			{
				bool appuiB1 = Input.IsActionJustPressed(InputBindings.Badge1);
				bool appuiB2 = Input.IsActionJustPressed(InputBindings.Badge2);
				if (appuiB1 || appuiB2)
				{
					int numero = appuiB1 ? 1 : 2;
					bool enPoche = appuiB1 ? GameState.Instance.BadgeRamasse1 : GameState.Instance.BadgeRamasse2;
					if (!enPoche)
					{
						string raison = $"{PorteId} : badge {numero} non récupéré";
						JouerSon(playerRefus);
						GameState.Instance.SignalerScanInvalide(raison);
						EmitSignal(SignalName.ScanRefuse, raison);
					}
					else
					{
						TraiterScan(appuiB1 ? GameState.BADGE_1_UID : GameState.BADGE_2_UID);
					}
					// La touche 1/2 a aussi été poussée dans la file par Node3d.
					// On la draine pour ne pas laisser un scan fantôme qui
					// déclencherait une autre porte plus tard.
					while (GameState.Instance.HasPendingRfid)
						GameState.Instance.ConsumePendingRfid();
				}
			}
			else if (GameState.Instance.HasPendingRfid)
			{
				string uid = GameState.Instance.PendingRfidUid;
				TraiterScan(uid);
				GameState.Instance.ConsumePendingRfid();
			}
		}

		bool boutonActuel = arduino != null && arduino.isInteractPressed;
		bool frontMontant = boutonActuel && !boutonPrecedent;
		boutonPrecedent = boutonActuel;

		// L'interaction est désormais pilotée uniquement par le raycast : il
		// faut regarder la porte (ou le lecteur si verrouillée) pour pouvoir
		// l'ouvrir / la fermer. La portée du Survol3D (2 m) joue le rôle de la
		// zone d'Area3D — plus précis car on doit aussi regarder l'objet.
		bool regardeCible = Survol3D.CibleCourante == this
			|| (lecteurBadge != null && Survol3D.CibleCourante == lecteurBadge);

		if (regardeCible && frontMontant)
		{
			if (estDeverrouille)
			{
				estOuverte = !estOuverte;
				if (estOuverte)
				{
					JouerSon(playerOuverture);
					EmitSignal(SignalName.PorteOuverte);

					// Phase finale de la boussole : ouvrir porte_sortie = on
					// s'engage dans le sas → la boussole bascule sur PorteRonde.
					if (PorteId == "porte_sortie" && GameState.Instance != null)
						GameState.Instance.PorteSortieFranchie = true;
				}
				else
				{
					JouerSon(playerFermeture);
					EmitSignal(SignalName.PorteFermee);
				}
			}
			else if (GameState.Instance != null)
			{
				string raison = $"{PorteId} verrouillée — scanne le badge";
				JouerSon(playerRefus);
				GameState.Instance.SignalerScanInvalide(raison);
				EmitSignal(SignalName.ScanRefuse, raison);
			}
		}

		float angleCible = estOuverte ? Mathf.DegToRad(AngleOuvert) : 0f;
		Vector3 rot = pivot.Rotation;
		rot.Y = Mathf.MoveToward(rot.Y, angleCible, VitesseOuverture * (float)delta);
		pivot.Rotation = rot;
	}

	private void TraiterScan(string uid)
	{
		var gs = GameState.Instance;

		if (EstPorteFinale)
		{
			if (!gs.EstBadge2(uid))
			{
				string raison = gs.EstBadge1(uid)
					? $"{PorteId} : ce badge n'ouvre pas la porte finale"
					: $"{PorteId} : badge inconnu";
				JouerSon(playerRefus);
				gs.SignalerScanInvalide(raison);
				EmitSignal(SignalName.ScanRefuse, raison);
				return;
			}

			if (!gs.PorteFinalePeutEtreDeverrouille())
			{
				string raison = $"{PorteId} : il manque des salles à visiter ({gs.SallesVisitees.Count}/6)";
				JouerSon(playerRefus);
				gs.SignalerScanInvalide(raison);
				EmitSignal(SignalName.ScanRefuse, raison);
				return;
			}

			estDeverrouille = true;
			JouerSon(playerDeverrouillage);
			gs.MarquerPorteDeverrouille(PorteId, estFinale: true);
			EmitSignal(SignalName.PorteDeverrouillee);
			MettreAJourEtatVerrouillage();
			return;
		}

		if (gs.EstBadge1(uid))
		{
			if (!PrerequisRempli)
			{
				string nomSalle = NumeroSallePrerequise > 0
					? $"Salle {NumeroSallePrerequise}"
					: SallePrerequise.Replace("_", " ");
				string raison = $"{PorteId} : termine d'abord la {nomSalle}";
				JouerSon(playerRefus);
				gs.SignalerScanInvalide(raison);
				EmitSignal(SignalName.ScanRefuse, raison);
				return;
			}

			estDeverrouille = true;
			JouerSon(playerDeverrouillage);
			gs.MarquerPorteDeverrouille(PorteId, estFinale: false);
			EmitSignal(SignalName.PorteDeverrouillee);
			MettreAJourEtatVerrouillage();
		}
		else if (gs.EstBadge2(uid))
		{
			string raison = $"{PorteId} : ce badge est réservé à la porte finale";
			JouerSon(playerRefus);
			gs.SignalerScanInvalide(raison);
			EmitSignal(SignalName.ScanRefuse, raison);
		}
		else
		{
			string raison = $"{PorteId} : badge inconnu";
			JouerSon(playerRefus);
			gs.SignalerScanInvalide(raison);
			EmitSignal(SignalName.ScanRefuse, raison);
		}
	}
}
