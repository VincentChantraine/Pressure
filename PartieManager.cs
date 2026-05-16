using Godot;

// À attacher à un Node dans la scène de jeu principale.
// Rôle : écouter la fin de partie (temps écoulé OU porte finale ouverte)
// et déclencher la transition vers l'écran de fin.
public partial class PartieManager : Node
{
	[Export] public NodePath ServoGameTimerPath;
	[Export] public NodePath PlayerPath;

	// Son de radio à couper quand le joueur reprend les commandes (victoire).
	[Export] public NodePath SonRadioInterferencePath;

	// Durée de la séquence de fin (son DepthBomb + fondu noir) avant l'écran de fin.
	[Export] public float DureeSequenceVictoire = 5.0f;

	private ServoGameTimer servoTimer;
	private Player joueur;
	private bool partieTerminee = false;
	private float tempsAuTP = -1f;

	public override void _Ready()
	{
		servoTimer = GetNodeOrNull<ServoGameTimer>(ServoGameTimerPath);

		if (PlayerPath != null && !PlayerPath.IsEmpty)
			joueur = GetNodeOrNull<Player>(PlayerPath);
		if (joueur == null)
			joueur = GetTree().Root.FindChild("Player", true, false) as Player;

		if (servoTimer != null)
			servoTimer.TempsEcoule += OnTempsEcoule;
		else
			GD.PrintErr("[PartieManager] ServoGameTimer introuvable !");

		if (GameState.Instance != null)
		{
			GameState.Instance.PorteFinaleOuverte  += OnVictoire;
			GameState.Instance.JoueurEntreeBT01    += OnJoueurEntreeBT01;
		}
	}

	private void OnJoueurEntreeBT01()
	{
		if (servoTimer == null) return;
		tempsAuTP = servoTimer.GetElapsed();
		servoTimer.Pause();
		servoTimer.ActiverAlarmePermanente();
		GD.Print($"[PartieManager] TP BT-01 — chrono figé à {tempsAuTP:F1}s.");
	}

	private void OnTempsEcoule()
	{
		if (partieTerminee) return;
		partieTerminee = true;

		GD.Print("[PartieManager] Défaite — temps écoulé.");
		float temps = servoTimer != null ? servoTimer.DureeTotale : 300f;
		GameState.Instance?.TerminerPartie(GameState.ResultatPartie.Defaite, temps);
	}

	private async void OnVictoire()
	{
		if (partieTerminee) return;
		partieTerminee = true;

		GD.Print("[PartieManager] Victoire ! Lancement séquence DepthBomb.");
		float temps = tempsAuTP >= 0f ? tempsAuTP : (servoTimer != null ? servoTimer.GetElapsed() : 0f);
		servoTimer?.Pause();

		// Gèle le joueur : plus de déplacement ni de regard.
		if (joueur != null) joueur.Frozen = true;

		// Coupe la radio d'interférence du BT-01 : on a repris les commandes.
		if (SonRadioInterferencePath != null && !SonRadioInterferencePath.IsEmpty)
		{
			var radio = GetNodeOrNull<AudioStreamPlayer3D>(SonRadioInterferencePath);
			if (radio is LoopingAudio loop)
				loop.StopDefinitivement();
			else
				radio?.Stop();
		}
		else
		{
			GD.PrintErr("[PartieManager] SonRadioInterferencePath non assigné !");
		}

		// Joue DepthBomb.mp3
		var audio = new AudioStreamPlayer();
		AddChild(audio);
		audio.Stream = GD.Load<AudioStream>("res://Son/DepthBomb.mp3");
		audio.Play();

		// Fondu noir plein écran sur DureeSequenceVictoire secondes.
		var canvas = new CanvasLayer { Layer = 100 };
		AddChild(canvas);

		var rect = new ColorRect
		{
			Color = new Color(0, 0, 0, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		};
		canvas.AddChild(rect);

		var tween = CreateTween();
		tween.TweenProperty(rect, "color:a", 1.0f, DureeSequenceVictoire);

		await ToSignal(GetTree().CreateTimer(DureeSequenceVictoire),
			SceneTreeTimer.SignalName.Timeout);

		GameState.Instance?.TerminerPartie(GameState.ResultatPartie.Victoire, temps);
	}

	public override void _ExitTree()
	{
		if (servoTimer != null)
			servoTimer.TempsEcoule -= OnTempsEcoule;
		if (GameState.Instance != null)
		{
			GameState.Instance.PorteFinaleOuverte -= OnVictoire;
			GameState.Instance.JoueurEntreeBT01   -= OnJoueurEntreeBT01;
		}
	}
}
