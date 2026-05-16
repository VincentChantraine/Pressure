using System;
using Godot;

// Chrono principal de la partie (5 min).
// - Démarre TOUJOURS au _Ready(), avec ou sans Arduino.
// - Si Arduino connecté : envoie les commandes servo (vitesse dégressive).
// - Si pas d'Arduino : le chrono tourne quand même pour le HUD et le game over.
// - Émet "TempsEcoule" quand les 5 min sont atteintes.
//
// Système de bonus de temps :
// - Plusieurs sources (leviers, vannes...) peuvent appeler AjouterTemps(secondes)
//   pour faire reculer le chrono d'un montant fixe.
// - Le recul est plafonné par le temps déjà écoulé (clamp à 0).
// - Émet "TempsAjoute" pour permettre un feedback HUD éventuel.
public partial class ServoGameTimer : Node
{
	[Export] public NodePath ArduinoPath;
	[Export] public float DureeTotale = 300.0f;   // 5 min
	[Export] public int VitesseInitiale = 60;     // ∈ [0, 90]
	[Export] public int Sens = 1;                 // +1 ou -1
	[Export] public float IntervalleEnvoi = 0.2f; // 200 ms entre commandes série
	[Export] public float CourbeExposant = 1.5f;  // 1 = linéaire, >1 = ralentit vite tôt

	// Tic-tac qui accélère avec le temps écoulé.
	// AudioStreamPlayer (ou AudioStreamPlayer3D) avec un AudioStream en mode loop.
	[Export] public NodePath SonTicTacPath;
	// PitchScale au début (elapsed = 0).
	[Export] public float PitchTicTacInitial = 1.0f;
	// PitchScale à la fin (elapsed = DureeTotale).
	[Export] public float PitchTicTacFinal = 2.5f;
	// Courbe d'accélération (1 = linéaire, >1 = accélère vite à la fin).
	[Export] public float CourbeTicTac = 1.5f;

	// Lumières d'alarme : changent de couleur sans toucher au LightSwitch.
	[Export] public OmniLight3D[] LumieresAlarme = Array.Empty<OmniLight3D>();
	// Fraction du temps écoulé à partir de laquelle l'alarme s'active (0.7 = 30% restant).
	[Export] public float SeuilAlarme = 0.7f;
	// Couleur de l'alarme (rouge vif par défaut).
	[Export] public Color CouleurAlarme = new Color(1f, 0.1f, 0f);

	// Signal émis quand les 5 min sont écoulées
	[Signal] public delegate void TempsEcouleEventHandler();

	// Signal émis quand un bonus de temps est appliqué (feedback HUD).
	// secondesEffectives = combien de secondes ont réellement été retirées
	// (peut être < secondesDemandees si elapsed était trop bas).
	[Signal] public delegate void TempsAjouteEventHandler(float secondesEffectives, float nouveauElapsed);

	private Node3d arduino;
	private float elapsed = 0f;
	private float sendAccum = 0f;
	private bool fini = false;
	private int derniereVitesseEnvoyee = int.MinValue;
	// Accepte AudioStreamPlayer ou AudioStreamPlayer3D — on accède via les propriétés dynamiques.
	private Node sonTicTac;
	private bool alarmeActive = false;
	private Color[] couleursOriginales;
	private float[] energiesOriginales;

	private bool enPause = false;
	private bool alarmeForcee = false;
	private float alarmeTemps = 0f;

	public float GetElapsed() => elapsed;
	public bool EstDemarre()  => !enPause;
	public bool EstFini()     => fini;

	public void ActiverAlarmePermanente()
	{
		alarmeForcee = true;
		alarmeActive = true;
	}

	public void Pause()   { enPause = true; }
	public void Reprend() { enPause = false; }

	public void Reset()
	{
		elapsed = 0f;
		sendAccum = 0f;
		fini = false;
		derniereVitesseEnvoyee = int.MinValue;
		enPause = false;
	}

	/// <summary>
	/// Recule le chrono d'un montant fixe en secondes (clamp à 0).
	/// Utilisé par les leviers, vannes, etc. pour donner un bonus de temps.
	/// </summary>
	public void AjouterTemps(float secondes)
	{
		if (fini || secondes <= 0f) return;

		float avant = elapsed;
		elapsed = Mathf.Max(0f, elapsed - secondes);
		float effectif = avant - elapsed;

		EmitSignal(SignalName.TempsAjoute, effectif, elapsed);
		GD.Print($"[ServoGameTimer] +{effectif:F1}s offerts → elapsed = {elapsed:F1}s / {DureeTotale:F0}s");
	}

	public override void _Ready()
	{
		couleursOriginales = new Color[LumieresAlarme.Length];
		energiesOriginales = new float[LumieresAlarme.Length];
		for (int i = 0; i < LumieresAlarme.Length; i++)
			if (LumieresAlarme[i] != null)
			{
				couleursOriginales[i] = LumieresAlarme[i].LightColor;
				energiesOriginales[i] = LumieresAlarme[i].LightEnergy;
			}

		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNodeOrNull<Node3d>(ArduinoPath);

		if (arduino == null)
			GD.Print("[ServoGameTimer] Pas d'Arduino lié — chrono logiciel seul.");
		else
			GD.Print("[ServoGameTimer] Arduino lié — chrono + servo.");

		if (SonTicTacPath != null && !SonTicTacPath.IsEmpty)
		{
			sonTicTac = GetNodeOrNull<Node>(SonTicTacPath);
			if (sonTicTac == null)
			{
				GD.PrintErr($"[ServoGameTimer] SonTicTacPath introuvable : {SonTicTacPath}");
			}
			else if (sonTicTac is AudioStreamPlayer asp)
			{
				asp.PitchScale = PitchTicTacInitial;
				if (!asp.Playing) asp.Play();
				GD.Print("[ServoGameTimer] Tic-Tac démarré (AudioStreamPlayer).");
			}
			else if (sonTicTac is AudioStreamPlayer3D asp3)
			{
				asp3.PitchScale = PitchTicTacInitial;
				if (!asp3.Playing) asp3.Play();
				GD.Print("[ServoGameTimer] Tic-Tac démarré (AudioStreamPlayer3D).");
			}
			else
			{
				GD.PrintErr($"[ServoGameTimer] Le nœud Tic-Tac n'est ni AudioStreamPlayer ni AudioStreamPlayer3D : {sonTicTac.GetType().Name}");
				sonTicTac = null;
			}
		}
	}

	public override void _Process(double delta)
	{
		if (alarmeForcee)
		{
			alarmeTemps += (float)delta;
			float pulse = (Mathf.Sin(alarmeTemps * 0.6f * Mathf.Pi * 2f) + 1f) * 0.5f;
			for (int i = 0; i < LumieresAlarme.Length; i++)
				if (LumieresAlarme[i] != null)
				{
					LumieresAlarme[i].LightColor = CouleurAlarme;
					LumieresAlarme[i].LightEnergy = Mathf.Lerp(0.1f, energiesOriginales[i], pulse);
				}
			return;
		}

		if (fini || enPause) return;

		elapsed += (float)delta;
		sendAccum += (float)delta;

		float t = Mathf.Clamp(elapsed / DureeTotale, 0f, 1f);

		// Mise à jour pitch du tic-tac : pitch monte avec une courbe exponentielle.
		if (sonTicTac != null)
		{
			float tt = Mathf.Pow(t, CourbeTicTac);
			float pitch = Mathf.Lerp(PitchTicTacInitial, PitchTicTacFinal, tt);
			if (sonTicTac is AudioStreamPlayer asp) asp.PitchScale = pitch;
			else if (sonTicTac is AudioStreamPlayer3D asp3) asp3.PitchScale = pitch;
		}

		// Envoi servo uniquement si Arduino branché et prêt
		if (arduino != null && arduino.arduinoReady)
		{
			float fac = Mathf.Pow(1f - t, CourbeExposant);
			int vitesse = Mathf.RoundToInt(VitesseInitiale * fac) * Sens;

			if (sendAccum >= IntervalleEnvoi)
			{
				sendAccum = 0f;
				if (vitesse != derniereVitesseEnvoyee)
				{
					arduino.SendServoSpeed(vitesse);
					derniereVitesseEnvoyee = vitesse;
				}
			}
		}

		// Lumières d'alarme : changent de couleur, sans toucher à l'énergie (LightSwitch intact).
		if (LumieresAlarme.Length > 0)
		{
			if (t >= SeuilAlarme)
			{
				alarmeActive = true;
				float progression = (t - SeuilAlarme) / (1f - SeuilAlarme);
				float hz = Mathf.Lerp(0.15f, 0.6f, progression);
				// Sinus entre 0 et 1 → pulse douce (jamais coupée net).
				float pulse = (Mathf.Sin(elapsed * hz * Mathf.Pi * 2f) + 1f) * 0.5f;
				for (int i = 0; i < LumieresAlarme.Length; i++)
					if (LumieresAlarme[i] != null)
					{
						LumieresAlarme[i].LightColor = CouleurAlarme;
						LumieresAlarme[i].LightEnergy = Mathf.Lerp(0.1f, energiesOriginales[i], pulse);
					}
			}
			else if (alarmeActive)
			{
				// Bonus de temps : repasse sous le seuil → restaurer couleur et énergie d'origine.
				for (int i = 0; i < LumieresAlarme.Length; i++)
					if (LumieresAlarme[i] != null)
					{
						LumieresAlarme[i].LightColor = couleursOriginales[i];
						LumieresAlarme[i].LightEnergy = energiesOriginales[i];
					}
				alarmeActive = false;
			}
		}

		// Fin de partie
		if (t >= 1f && !fini)
		{
			if (arduino != null && arduino.arduinoReady)
				arduino.SendServoSpeed(0);
			if (sonTicTac is AudioStreamPlayer aspStop && aspStop.Playing) aspStop.Stop();
			else if (sonTicTac is AudioStreamPlayer3D asp3Stop && asp3Stop.Playing) asp3Stop.Stop();
			for (int i = 0; i < LumieresAlarme.Length; i++)
				if (LumieresAlarme[i] != null)
				{
					LumieresAlarme[i].LightColor = couleursOriginales[i];
					LumieresAlarme[i].LightEnergy = energiesOriginales[i];
				}
			fini = true;
			GD.Print("[ServoGameTimer] Temps écoulé — émission TempsEcoule.");
			EmitSignal(SignalName.TempsEcoule);
		}
	}
}
