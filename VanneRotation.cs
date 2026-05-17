using Godot;

// Vanne à fermer par rotation du joystick (ou Q/D/Z/S au clavier via Node3d).
//
// Principe :
// - Le joystick a une position (X, Y) lue via Node3d (0..1023, centre 512).
// - On convertit en angle. Si le stick est suffisamment poussé hors du centre
//   (hors "deadzone"), on enregistre le quadrant courant (N, E, S, O).
// - Quand les quadrants défilent dans le bon ordre (horaire : N → E → S → O → N),
//   on compte +1 quart de tour. 4 quarts = 1 tour complet.
// - À ToursRequis tours, la vanne est considérée comme fermée.
//
// Tolérance :
// - Sens antihoraire = IGNORÉ (pas de reset punitif).
// - Stick relâché au centre = ne casse rien, on garde les quarts déjà accumulés.
//
// Feedback audio :
// - Pendant la rotation, le son de fuite baisse progressivement.
// - À la fin : son de fuite coupé, son d'activation joué une fois.
//
// Effet sur le chrono :
// - Si ServoGameTimerPath est renseigné, la fermeture appelle AjouterTemps(BonusTempsSecondes)
//   sur le ServoGameTimer (recule elapsed de N secondes, clamp à 0).

public partial class VanneRotation : Node3D
{
	[Export] public string VanneId = "vanne_salle2";

	// Si renseigné, valide cette salle dans GameState quand la vanne se ferme.
	[Export] public string SalleIdAValider = "";
	[Export] public NodePath ArduinoPath;
	[Export] public NodePath ZoneInteractionPath;
	[Export] public NodePath SonFuitePath;
	[Export] public NodePath SonActivationPath;
	// Son joué en boucle pendant que le joueur tourne la vanne (stick hors deadzone).
	// AudioStreamPlayer3D avec un AudioStream en mode loop.
	[Export] public NodePath SonRotationPath;
	// Délai (s) sans mouvement avant de couper le son de rotation.
	[Export] public float DelaiCoupureRotation = 0.25f;

	// Lien vers le ServoGameTimer pour donner du temps à la fermeture.
	// Laisser vide si tu ne veux pas cet effet.
	[Export] public NodePath ServoGameTimerPath;

	// Bonus de temps (en secondes) ajouté au chrono à la fermeture de la vanne.
	// 0 = aucun effet. 20 = recule elapsed de 20s.
	[Export] public float BonusTempsSecondes = 20f;

	// Combien de tours complets requis. 1 tour = 4 quarts.
	[Export] public int ToursRequis = 3;

	// Deadzone autour du centre 512 (en unités brutes 0..1023).
	[Export] public int DeadzoneRaw = 200;

	// Volume (dB) du son de fuite quand la vanne est totalement ouverte.
	[Export] public float VolumeDbOuvert = 0f;

	// Volume (dB) du son de fuite quand la vanne est totalement fermée.
	[Export] public float VolumeDbFerme = -40f;

	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();
	[Signal] public delegate void QuartValideEventHandler(int quartsTotal, int quartsRequis);
	[Signal] public delegate void VanneFermeeEventHandler(string vanneId);

	private Node3d arduino;
	private Area3D zone;
	private AudioStreamPlayer3D sonFuite;
	private AudioStreamPlayer3D sonActivation;
	private AudioStreamPlayer3D sonRotation;
	private float tempsSansMouvement = 0f;
	private ServoGameTimer servoTimer;

	private bool joueurDansZone = false;
	private bool fermee = false;

	// Quadrants : 0=N (Y haut), 1=E (X droite), 2=S (Y bas), 3=O (X gauche).
	private int quadrantPrecedent = -1;
	private int quartsAccumules = 0;

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNodeOrNull<Node3d>(ArduinoPath);

		if (ServoGameTimerPath != null && !ServoGameTimerPath.IsEmpty)
			servoTimer = GetNodeOrNull<ServoGameTimer>(ServoGameTimerPath);

		if (ZoneInteractionPath != null && !ZoneInteractionPath.IsEmpty)
		{
			zone = GetNode<Area3D>(ZoneInteractionPath);
			zone.BodyEntered += (Node3D body) =>
			{
				if (body is Player)
				{
					joueurDansZone = true;
					EmitSignal(SignalName.JoueurEntreZone);
				}
			};
			zone.BodyExited += (Node3D body) =>
			{
				if (body is Player)
				{
					joueurDansZone = false;
					quadrantPrecedent = -1;
					EmitSignal(SignalName.JoueurSortZone);
				}
			};
		}
		else
		{
			joueurDansZone = true;
		}

		if (SonFuitePath != null && !SonFuitePath.IsEmpty)
		{
			sonFuite = GetNodeOrNull<AudioStreamPlayer3D>(SonFuitePath);
			if (sonFuite != null)
			{
				sonFuite.VolumeDb = VolumeDbOuvert;
				if (!sonFuite.Playing) sonFuite.Play();
			}
		}

		if (SonActivationPath != null && !SonActivationPath.IsEmpty)
			sonActivation = GetNodeOrNull<AudioStreamPlayer3D>(SonActivationPath);

		if (SonRotationPath != null && !SonRotationPath.IsEmpty)
			sonRotation = GetNodeOrNull<AudioStreamPlayer3D>(SonRotationPath);
	}

	public override void _Process(double delta)
	{
		if (fermee || arduino == null || !joueurDansZone) return;

		int q = QuadrantCourant(arduino.joystickX, arduino.joystickY);

		// Feedback audio "rotation" : le stick est poussé hors deadzone → la vanne bouge.
		GererSonRotation(q != -1, (float)delta);

		if (q == -1 || q == quadrantPrecedent) return;

		if (quadrantPrecedent == -1)
		{
			quadrantPrecedent = q;
			return;
		}

		int suivantHoraire = (quadrantPrecedent + 1) % 4;
		if (q == suivantHoraire)
		{
			quartsAccumules++;
			quadrantPrecedent = q;
			OnQuartValide();
			return;
		}

		int suivantAntihoraire = (quadrantPrecedent + 3) % 4;
		if (q == suivantAntihoraire)
		{
			quadrantPrecedent = q;
			return;
		}

		quadrantPrecedent = q;
	}

	private void GererSonRotation(bool stickActif, float delta)
	{
		if (sonRotation == null) return;

		if (stickActif)
		{
			tempsSansMouvement = 0f;
			if (!sonRotation.Playing) sonRotation.Play();
		}
		else
		{
			tempsSansMouvement += delta;
			if (sonRotation.Playing && tempsSansMouvement >= DelaiCoupureRotation)
				sonRotation.Stop();
		}
	}

	private int QuadrantCourant(int x, int y)
	{
		int dx = x - ArduinoConfig.AnalogCenter;
		int dy = y - ArduinoConfig.AnalogCenter;

		if (Mathf.Abs(dx) < DeadzoneRaw && Mathf.Abs(dy) < DeadzoneRaw)
			return -1;

		if (Mathf.Abs(dx) > Mathf.Abs(dy))
			return (dx > 0) ? 1 : 3;
		else
			return (dy < 0) ? 0 : 2;
	}

	private void OnQuartValide()
	{
		int quartsRequis = ToursRequis * 4;
		int q = Mathf.Clamp(quartsAccumules, 0, quartsRequis);

		EmitSignal(SignalName.QuartValide, q, quartsRequis);

		if (sonFuite != null)
		{
			float t = (float)q / quartsRequis;
			sonFuite.VolumeDb = Mathf.Lerp(VolumeDbOuvert, VolumeDbFerme, t);
		}

		if (quartsAccumules >= quartsRequis)
			Fermer();
	}

	private void Fermer()
	{
		if (fermee) return;
		fermee = true;

		if (sonFuite != null && sonFuite.Playing) sonFuite.Stop();
		if (sonRotation != null && sonRotation.Playing) sonRotation.Stop();
		if (sonActivation != null) sonActivation.Play();

		// Donne un bonus de temps fixe au chrono global.
		if (servoTimer != null && BonusTempsSecondes > 0f)
			servoTimer.AjouterTemps(BonusTempsSecondes);

		EmitSignal(SignalName.VanneFermee, VanneId);
		GD.Print($"[VanneRotation] {VanneId} fermée ({ToursRequis} tours validés, +{BonusTempsSecondes:F0}s).");

		if (!string.IsNullOrEmpty(SalleIdAValider))
			GameState.Instance?.MarquerSalleVisitee(SalleIdAValider);
	}

	public bool EstFermee => fermee;
	public int QuartsAccumules => quartsAccumules;
	public int QuartsRequis => ToursRequis * 4;
}
