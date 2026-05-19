using Godot;

// Épreuve labyrinthe — salle 6.
// Quand le joueur s'approche (entre dans ZoneActivation) :
//   - bascule sur la caméra du dessus
//   - gèle le déplacement du joueur
//   - les flèches du clavier contrôlent la bille
// Quand le joueur sort (ou que l'épreuve est réussie) : retour caméra joueur, déplacement réactivé.
public partial class LabyrintheBille : Node3D
{
	[Export] public NodePath BillePath;
	[Export] public NodePath PointDepartPath;
	[Export] public NodePath ZoneSortiePath;          // Area3D à la sortie du labyrinthe
	[Export] public NodePath ZoneActivationPath;      // Area3D autour de la table — détecte l'approche du joueur
	[Export] public NodePath CameraDessusPath;        // Camera3D placée au-dessus du labyrinthe (vue top-down)
	[Export] public NodePath PlayerPath;              // Le Player (CharacterBody3D) — pour figer son déplacement

	[Export] public float Vitesse = 1.5f;             // Vitesse de déplacement de la bille

	// Touche (clavier) pour réinitialiser la bille au point de départ (bille tombée/coincée).
	[Export] public Key ToucheResetManuel = Key.R;

	[Export] public NodePath ChiffreRevelePath;       // Optionnel : Label/Control 2D (ex: dans un CanvasLayer) ou Node3D à afficher à la réussite
	[Export] public string SalleIdAValider = "salle_6";

	[Export] public NodePath LampeTorchePath;         // SpotLight3D — éteint pendant l'épreuve
	[Export] public NodePath FlashLightVisuelPath;    // Mesh/Node3D de la lampe — masqué pendant l'épreuve

	// Son de roulement de la bille (AudioStreamPlayer3D en mode loop).
	// Le volume et le pitch sont modulés par la vitesse de la bille.
	[Export] public NodePath SonRoulementPath;
	// Vitesse linéaire (m/s) à partir de laquelle on considère la bille "en mouvement".
	[Export] public float VitesseMinSonore = 0.02f;
	// Vitesse linéaire de référence pour pitch/volume max.
	// Doit correspondre à peu près à la valeur de `Vitesse` (par défaut 1.5).
	[Export] public float VitesseMaxSonore = 1.5f;
	[Export] public float VolumeDbMin = -6f;
	[Export] public float VolumeDbMax = 6f;
	[Export] public float PitchMin = 0.85f;
	[Export] public float PitchMax = 1.3f;

	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();
	[Signal] public delegate void EpreuveReussieEventHandler();
	[Signal] public delegate void BilleResetEventHandler();

	private RigidBody3D bille;
	private Node3D pointDepart;
	private Area3D zoneSortie;
	private Area3D zoneActivation;
	private Camera3D cameraDessus;
	private Player player;
	private CanvasItem chiffre;

	private SpotLight3D lampeTorche;
	private Node3D flashLightVisuel;
	// Accepte AudioStreamPlayer (non-positionnel, recommandé pour la vue top-down)
	// ou AudioStreamPlayer3D — on accède via les propriétés dynamiques.
	private Node sonRoulement;

	private Camera3D cameraPrecedente;
	private bool epreuveReussie = false;
	private bool joueurDansZone = false;
	private bool resetKeyPrecedent = false;

	public bool EstReussie => epreuveReussie;
	public bool JoueurDansZone => joueurDansZone;

	public override void _Ready()
	{
		bille          = GetNodeOrNull<RigidBody3D>(BillePath);
		pointDepart    = GetNodeOrNull<Node3D>(PointDepartPath);
		zoneSortie     = GetNodeOrNull<Area3D>(ZoneSortiePath);
		zoneActivation = GetNodeOrNull<Area3D>(ZoneActivationPath);
		cameraDessus   = GetNodeOrNull<Camera3D>(CameraDessusPath);
		player         = GetNodeOrNull<Player>(PlayerPath);
		chiffre        = GetNodeOrNull<CanvasItem>(ChiffreRevelePath);
		lampeTorche    = GetNodeOrNull<SpotLight3D>(LampeTorchePath);
		flashLightVisuel = GetNodeOrNull<Node3D>(FlashLightVisuelPath);
		sonRoulement   = GetNodeOrNull<Node>(SonRoulementPath);
		if (SonRoulementPath != null && !SonRoulementPath.IsEmpty)
		{
			if (sonRoulement == null)
			{
				GD.PrintErr($"[LabyrintheBille] SonRoulementPath introuvable : {SonRoulementPath}");
			}
			else if (sonRoulement is AudioStreamPlayer asp)
			{
				if (asp.Stream == null)
					GD.PrintErr("[LabyrintheBille] ⚠ Aucun Stream assigné à l'AudioStreamPlayer ! Glisse Roulement Bille.wav dans le champ Stream.");
			}
			else if (sonRoulement is AudioStreamPlayer3D asp3)
			{
				if (asp3.Stream == null)
					GD.PrintErr("[LabyrintheBille] ⚠ Aucun Stream assigné !");
			}
			else
			{
				GD.PrintErr($"[LabyrintheBille] Le nœud son n'est ni AudioStreamPlayer ni AudioStreamPlayer3D : {sonRoulement.GetType().Name}");
				sonRoulement = null;
			}
		}

		if (bille == null)          GD.PrintErr("[LabyrintheBille] Bille introuvable.");
		if (pointDepart == null)    GD.PrintErr("[LabyrintheBille] PointDepart introuvable.");
		if (zoneSortie == null)     GD.PrintErr("[LabyrintheBille] ZoneSortie introuvable.");
		if (zoneActivation == null) GD.PrintErr("[LabyrintheBille] ZoneActivation introuvable.");
		if (cameraDessus == null)   GD.PrintErr("[LabyrintheBille] CameraDessus introuvable.");

		if (chiffre != null)
		{
			chiffre.Visible = false;
			chiffre.Hide();
			chiffre.CallDeferred(CanvasItem.MethodName.Hide);
		}

		if (zoneSortie != null)     zoneSortie.BodyEntered     += OnSortieAtteinte;
		if (zoneActivation != null)
		{
			zoneActivation.BodyEntered += OnJoueurEntre;
			zoneActivation.BodyExited  += OnJoueurSort;
		}

		// Centre boussole (proximité-only, jamais cible de flèche) : la zone
		// d'activation marque le cœur de la salle, sinon le labyrinthe lui-même.
		Node3D centre = zoneActivation != null ? (Node3D)zoneActivation : this;
		GameState.Instance?.RegistrerCentreSalle(SalleIdAValider, centre);

		ResetBille();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (bille == null || epreuveReussie || !joueurDansZone)
		{
			StopperSonRoulement();
			return;
		}

		// Reset manuel par touche (bille tombée ou coincée).
		bool resetKey = Input.IsPhysicalKeyPressed(ToucheResetManuel);
		if (resetKey && !resetKeyPrecedent)
		{
			GD.Print("[LabyrintheBille] Reset manuel demandé.");
			ResetBille();
		}
		resetKeyPrecedent = resetKey;

		Vector2 input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
		// Caméra tournée 90° → on mappe input.Y sur X monde et -input.X sur Z monde,
		// pour que les flèches correspondent visuellement à l'écran.
		Vector3 force = new Vector3(input.Y, 0f, -input.X) * Vitesse;
		bille.LinearVelocity = new Vector3(force.X, bille.LinearVelocity.Y, force.Z);

		MettreAJourSonRoulement();
	}

	private void MettreAJourSonRoulement()
	{
		if (sonRoulement == null || bille == null) return;

		Vector3 v = bille.LinearVelocity;
		float vitesse = new Vector2(v.X, v.Z).Length();

		if (vitesse < VitesseMinSonore)
		{
			StopperSonRoulement();
			return;
		}

		float t = Mathf.Clamp((vitesse - VitesseMinSonore) / (VitesseMaxSonore - VitesseMinSonore), 0f, 1f);
		float volume = Mathf.Lerp(VolumeDbMin, VolumeDbMax, t);
		float pitch = Mathf.Lerp(PitchMin, PitchMax, t);

		if (sonRoulement is AudioStreamPlayer asp)
		{
			asp.VolumeDb = volume;
			asp.PitchScale = pitch;
			if (!asp.Playing) asp.Play();
		}
		else if (sonRoulement is AudioStreamPlayer3D asp3)
		{
			asp3.VolumeDb = volume;
			asp3.PitchScale = pitch;
			if (!asp3.Playing) asp3.Play();
		}
	}

	private void StopperSonRoulement()
	{
		if (sonRoulement is AudioStreamPlayer asp && asp.Playing) asp.Stop();
		else if (sonRoulement is AudioStreamPlayer3D asp3 && asp3.Playing) asp3.Stop();
	}

	private void OnJoueurEntre(Node3D body)
	{
		if (body != player) return;

		// Si l'épreuve est déjà finie : on n'active plus la vue du dessus,
		// mais on réaffiche le chiffre tant que le joueur reste dans la zone.
		if (epreuveReussie)
		{
			if (chiffre != null) chiffre.Visible = true;
			EmitSignal(SignalName.JoueurEntreZone);
			return;
		}

		joueurDansZone = true;
		EmitSignal(SignalName.JoueurEntreZone);

		if (cameraDessus != null)
		{
			cameraPrecedente = GetViewport().GetCamera3D();
			cameraDessus.MakeCurrent();
		}

		// Le joueur garde le contrôle de son perso (joystick Arduino) pour pouvoir reculer
		// et quitter la vue du dessus à tout moment. La bille répond aux flèches du clavier.

		BasculerLampeTorche(false);

		GD.Print("[LabyrintheBille] Joueur en mode labyrinthe (vue du dessus).");
	}

	private void OnJoueurSort(Node3D body)
	{
		if (body != player) return;

		// On masque le chiffre dès que le joueur quitte la zone (qu'il l'ait résolu ou non).
		if (chiffre != null) chiffre.Visible = false;

		QuitterModeLabyrinthe();
		EmitSignal(SignalName.JoueurSortZone);
	}

	private void QuitterModeLabyrinthe()
	{
		joueurDansZone = false;

		if (cameraPrecedente != null)
			cameraPrecedente.MakeCurrent();

		if (bille != null)
		{
			bille.LinearVelocity = Vector3.Zero;
			bille.AngularVelocity = Vector3.Zero;
		}

		StopperSonRoulement();
		BasculerLampeTorche(true);

		GD.Print("[LabyrintheBille] Retour vue joueur.");
	}

	private void BasculerLampeTorche(bool actif)
	{
		if (lampeTorche != null) lampeTorche.Visible = actif;
		if (flashLightVisuel != null) flashLightVisuel.Visible = actif;
	}

	private void OnSortieAtteinte(Node3D body)
	{
		if (epreuveReussie || body != bille) return;

		epreuveReussie = true;
		GD.Print("[LabyrintheBille] Sortie atteinte — épreuve réussie.");

		if (chiffre != null) chiffre.Visible = true;

		if (GameState.Instance != null && !string.IsNullOrEmpty(SalleIdAValider))
			GameState.Instance.MarquerSalleVisitee(SalleIdAValider);

		EmitSignal(SignalName.EpreuveReussie);
		QuitterModeLabyrinthe();
	}

	public void ResetBille()
	{
		if (bille == null || pointDepart == null) return;

		bille.LinearVelocity = Vector3.Zero;
		bille.AngularVelocity = Vector3.Zero;
		bille.GlobalTransform = new Transform3D(Basis.Identity, pointDepart.GlobalPosition);

		EmitSignal(SignalName.BilleReset);
	}

	public override void _ExitTree()
	{
		if (zoneSortie != null) zoneSortie.BodyEntered -= OnSortieAtteinte;
		if (zoneActivation != null)
		{
			zoneActivation.BodyEntered -= OnJoueurEntre;
			zoneActivation.BodyExited  -= OnJoueurSort;
		}
	}
}
