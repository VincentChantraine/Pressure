using Godot;

public partial class Porte : Node3D
{
	[Export] public NodePath PivotPath;
	[Export] public NodePath ZonePath;
	[Export] public NodePath ArduinoPath;

	// --- Sons (à assigner dans l'inspecteur) ---
	[Export] public AudioStream SonOuverture;
	[Export] public AudioStream SonFermeture;
	[Export] public AudioStream SonDeverrouillage;
	[Export] public AudioStream SonRefus;

	// --- Volumes (en dB) ---
	[Export] public float VolumeDbOuverture = -32f;
	[Export] public float VolumeDbFermeture = -32f;
	[Export] public float VolumeDbDeverrouillage = -28f;
	[Export] public float VolumeDbRefus = -25f;

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

	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();
	[Signal] public delegate void PorteDeverrouilleeEventHandler();
	[Signal] public delegate void PorteOuverteEventHandler();
	[Signal] public delegate void PorteFermeeEventHandler();
	[Signal] public delegate void ScanRefuseEventHandler(string raison);

	private Node3D pivot;
	private Area3D zone;
	private Node3d arduino;  // ⚠️ vérifie que ta classe s'appelle bien Node3d (pas Node3D)

	// Players audio créés en code, un par son pour permettre le chevauchement
	private AudioStreamPlayer3D playerOuverture;
	private AudioStreamPlayer3D playerFermeture;
	private AudioStreamPlayer3D playerDeverrouillage;
	private AudioStreamPlayer3D playerRefus;

	private bool joueurDansZone = false;
	private bool estOuverte = false;
	private bool estDeverrouille = false;
	private bool boutonPrecedent = false;

	public bool JoueurDansZone => joueurDansZone;
	public bool EstOuverte => estOuverte;
	public bool EstDeverrouille => estDeverrouille;

	public bool PrerequisRempli =>
		string.IsNullOrEmpty(SallePrerequise)
		|| (GameState.Instance != null && GameState.Instance.SallesVisitees.Contains(SallePrerequise));

	public override void _Ready()
	{
		pivot = GetNode<Node3D>(PivotPath);
		zone = GetNode<Area3D>(ZonePath);
		arduino = GetNode<Node3d>(ArduinoPath);

		zone.BodyEntered += OnBodyEntered;
		zone.BodyExited += OnBodyExited;

		// Création des players audio
		playerOuverture = CreerPlayer(SonOuverture, VolumeDbOuverture);
		playerFermeture = CreerPlayer(SonFermeture, VolumeDbFermeture);
		playerDeverrouillage = CreerPlayer(SonDeverrouillage, VolumeDbDeverrouillage);
		playerRefus = CreerPlayer(SonRefus, VolumeDbRefus);

		if (!NecessiteBadge1 && !EstPorteFinale)
			estDeverrouille = true;

		// Registre la porte comme ancre boussole pour la salle qu'elle protège.
		// Écrase l'ancre puzzle (placée par le puzzle dans son _Ready) — c'est voulu :
		// pointer vers la porte est moins spoilant que pointer vers l'objet à manipuler.
		// Fallback : si SalleQueProtege n'est pas configuré dans l'inspecteur, on
		// déduit la salle depuis PorteId (convention "porte_N" → "salle_N").
		// Évite d'avoir à éditer chaque .tscn pour les 6 portes existantes.
		string salleCible = SalleQueProtege;
		if (string.IsNullOrEmpty(salleCible)
			&& !string.IsNullOrEmpty(PorteId)
			&& PorteId.StartsWith("porte_", System.StringComparison.OrdinalIgnoreCase))
		{
			string suffixe = PorteId.Substring("porte_".Length);
			if (int.TryParse(suffixe, out _))
				salleCible = "salle_" + suffixe;
		}
		// Cas spécial : la porte de sortie n'a pas de salle_N associée, elle est
		// la cible de la phase "toutes les salles validées" de la boussole.
		// Id réservé "exit_door" — cf. BoussoleHud.
		if (string.IsNullOrEmpty(salleCible)
			&& PorteId == "porte_sortie")
		{
			salleCible = "exit_door";
		}
		if (!string.IsNullOrEmpty(salleCible))
		{
			GameState.Instance?.RegistrerAncreSalle(salleCible, this);
			GD.Print($"[Porte] {PorteId} → boussole pour {salleCible}.");
		}
		else
		{
			GD.Print($"[Porte] {PorteId} : pas d'ancre boussole (PorteId hors convention 'porte_N').");
		}

		// Survol : ce nœud est "regardable" — le raycast caméra l'affichera comme cible.
		AddToGroup("interactif");
		SetMeta("libelle_interaction",
			EstPorteFinale ? "Scanner le badge final" : "Ouvrir / Fermer");
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
		if (player != null && player.Stream != null)
			player.Play();
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is Player)
		{
			joueurDansZone = true;
			EmitSignal(SignalName.JoueurEntreZone);
		}
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is Player)
		{
			joueurDansZone = false;
			EmitSignal(SignalName.JoueurSortZone);
		}
	}

	public override void _Process(double delta)
	{
		if (joueurDansZone && !estDeverrouille && GameState.Instance != null && GameState.Instance.HasPendingRfid)
		{
			string uid = GameState.Instance.PendingRfidUid;
			TraiterScan(uid);
			GameState.Instance.ConsumePendingRfid();
		}

		bool boutonActuel = arduino != null && arduino.isInteractPressed;
		bool frontMontant = boutonActuel && !boutonPrecedent;
		boutonPrecedent = boutonActuel;

		if (joueurDansZone && frontMontant)
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
