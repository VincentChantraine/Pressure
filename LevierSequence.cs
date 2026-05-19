using Godot;

// Levier à actionner par séquence de butées du slider (ou A/E au clavier via Node3d).
//
// Principe :
// - Le joueur entre dans la zone : le levier s'arme.
// - Il doit actionner le slider selon une séquence de butées.
//   Ex: [+1, +1, -1, +1] = →→←→
// - Entre deux butées, le slider DOIT repasser par le neutre.
// - Bonne direction attendue → on avance. Mauvaise → reset total.
// - Dernière étape validée → levier verrouillé, signal émis, bonus de temps appliqué.
//
// Effet :
// - Si ServoGameTimerPath est renseigné, l'activation appelle AjouterTemps(BonusTempsSecondes)
//   sur le ServoGameTimer (recule elapsed de N secondes, clamp à 0).

public partial class LevierSequence : Node3D
{
	[Export] public string LevierId = "levier_salle3";

	// Si renseigné, valide cette salle dans GameState quand le levier s'active.
	[Export] public string SalleIdAValider = "";
	[Export] public NodePath ArduinoPath;

	// Son joué à chaque étape validée (AudioStreamPlayer3D).
	[Export] public NodePath SonEtapePath;
	// Son joué à l'activation finale du levier (optionnel).
	[Export] public NodePath SonActivePath;
	// Son joué quand le joueur rate la séquence. Fallback Rater.mp3 chargé
	// automatiquement si non assigné.
	[Export] public AudioStream SonRate;
	[Export] public float VolumeDbRate = -8f;

	// Sons d'ambiance (AudioStreamPlayer3D) à couper quand le levier est activé.
	// Ex: arcs électriques sur les PowerVaults qui cessent une fois le courant rétabli.
	[Export] public NodePath SonAmbiant1Path;
	[Export] public NodePath SonAmbiant2Path;

	// Lien vers le ServoGameTimer pour donner du temps à l'activation.
	[Export] public NodePath ServoGameTimerPath;

	// Bonus de temps (en secondes) ajouté au chrono à l'activation du levier.
	// 0 = aucun effet. 20 = recule elapsed de 20s.
	[Export] public float BonusTempsSecondes = 20f;

	// Séquence attendue. +1 = butée droite, -1 = butée gauche.
	[Export] public int[] Sequence = new int[] { +1, -1, +1 };

	// Si le slider est inversé physiquement (gauche = valeur haute), mettre true.
	[Export] public bool InvertSlider = false;

	// Seuils de détection (sur 0–1023, centre 512).
	[Export] public int SeuilButeeHaute = 900;
	[Export] public int SeuilButeeBasse = 100;
	[Export] public int SeuilNeutreHaut = 600;
	[Export] public int SeuilNeutreBas = 400;

	[Signal] public delegate void EtapeValideeEventHandler(int indexEtape);
	[Signal] public delegate void SequenceRateeEventHandler(string raison);
	[Signal] public delegate void LevierActiveEventHandler(string levierId);
	[Signal] public delegate void ProgressionChangeeEventHandler(int indexEtape, int sensAttendu);

	private Node3d arduino;
	private ServoGameTimer servoTimer;
	private AudioStreamPlayer3D sonEtape;
	private AudioStreamPlayer3D sonActive;
	private AudioStreamPlayer3D sonRatePlayer;
	private AudioStreamPlayer3D sonAmbiant1;
	private AudioStreamPlayer3D sonAmbiant2;

	private bool active = false;
	private int indexCourant = 0;

	// Machine à états du slider.
	// 0 = neutre (prêt à détecter une butée)
	// +1 = en butée droite (attendre retour au neutre)
	// -1 = en butée gauche (attendre retour au neutre)
	private int etatSlider = 0;

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNodeOrNull<Node3d>(ArduinoPath);

		if (ServoGameTimerPath != null && !ServoGameTimerPath.IsEmpty)
			servoTimer = GetNodeOrNull<ServoGameTimer>(ServoGameTimerPath);

		if (SonEtapePath != null && !SonEtapePath.IsEmpty)
			sonEtape = GetNodeOrNull<AudioStreamPlayer3D>(SonEtapePath);
		if (SonActivePath != null && !SonActivePath.IsEmpty)
			sonActive = GetNodeOrNull<AudioStreamPlayer3D>(SonActivePath);

		if (SonRate == null)
			SonRate = GD.Load<AudioStream>("res://Son/Rater.mp3");
		if (SonRate != null)
		{
			sonRatePlayer = new AudioStreamPlayer3D
			{
				Stream = SonRate,
				VolumeDb = VolumeDbRate,
			};
			AddChild(sonRatePlayer);
		}

		if (SonAmbiant1Path != null && !SonAmbiant1Path.IsEmpty)
			sonAmbiant1 = GetNodeOrNull<AudioStreamPlayer3D>(SonAmbiant1Path);
		if (SonAmbiant2Path != null && !SonAmbiant2Path.IsEmpty)
			sonAmbiant2 = GetNodeOrNull<AudioStreamPlayer3D>(SonAmbiant2Path);

		GameState.Instance?.RegistrerCentreSalle(SalleIdAValider, this);

		AddToGroup("interactif");
		SetMeta("libelle_interaction", "Actionner le levier (A / E)");
	}

	public override void _Process(double delta)
	{
		// Interaction pilotée uniquement par le raycast : il faut regarder le
		// levier pour que les butées du slider comptent. La portée Survol3D
		// (2 m) remplace l'Area3D côté distance.
		if (active || arduino == null || Survol3D.CibleCourante != this) return;

		int v = arduino.LeverSliderValue;
		if (InvertSlider) v = ArduinoConfig.AnalogMax - v;

		switch (etatSlider)
		{
			case 0: // Neutre : on guette une entrée en butée
				if (v > SeuilButeeHaute)
				{
					etatSlider = +1;
					TraiterButee(+1);
				}
				else if (v < SeuilButeeBasse)
				{
					etatSlider = -1;
					TraiterButee(-1);
				}
				break;

			case +1: // En butée droite : attendre retour au neutre
				if (v < SeuilNeutreHaut)
					etatSlider = 0;
				break;

			case -1: // En butée gauche : attendre retour au neutre
				if (v > SeuilNeutreBas)
					etatSlider = 0;
				break;
		}
	}

	private void TraiterButee(int sens)
	{
		int attendu = Sequence[indexCourant];

		if (sens != attendu)
		{
			string dirFaite = (sens > 0 ? "droite" : "gauche");
			EmitSignal(SignalName.SequenceRatee, $"Mauvaise direction à l'étape {indexCourant + 1} ({dirFaite})");
			if (sonRatePlayer != null)
			{
				if (sonRatePlayer.Playing) sonRatePlayer.Stop();
				sonRatePlayer.Play();
			}
			Reset();
			return;
		}

		EmitSignal(SignalName.EtapeValidee, indexCourant);
		if (sonEtape != null)
		{
			if (sonEtape.Playing) sonEtape.Stop();
			sonEtape.Play();
		}
		indexCourant++;

		if (indexCourant >= Sequence.Length)
		{
			Activer();
		}
		else
		{
			EmettreProgression();
		}
	}

	private void Activer()
	{
		if (active) return;
		active = true;

		if (servoTimer != null && BonusTempsSecondes > 0f)
			servoTimer.AjouterTemps(BonusTempsSecondes);

		if (sonActive != null) sonActive.Play();

		// Coupe les sons d'ambiance liés au puzzle (ex: arcs électriques).
		if (sonAmbiant1 is LoopingAudio l1) l1.StopDefinitivement();
		else if (sonAmbiant1 != null && sonAmbiant1.Playing) sonAmbiant1.Stop();
		if (sonAmbiant2 is LoopingAudio l2) l2.StopDefinitivement();
		else if (sonAmbiant2 != null && sonAmbiant2.Playing) sonAmbiant2.Stop();

		EmitSignal(SignalName.LevierActive, LevierId);
		GD.Print($"[LevierSequence] {LevierId} activé (+{BonusTempsSecondes:F0}s).");

		if (!string.IsNullOrEmpty(SalleIdAValider))
			GameState.Instance?.MarquerSalleVisitee(SalleIdAValider);
	}

	private void Reset()
	{
		indexCourant = 0;
		// On laisse etatSlider tel quel : le joueur doit repasser par le neutre
		// avant de recommencer, ce qui évite un reset en boucle s'il garde le
		// slider à fond.
		EmettreProgression();
	}

	private void EmettreProgression()
	{
		if (indexCourant < Sequence.Length)
			EmitSignal(SignalName.ProgressionChangee, indexCourant, Sequence[indexCourant]);
	}

	public bool EstActive => active;
	public int[] GetSequence() => (int[])Sequence.Clone();
	public int IndexCourant => indexCourant;
}
