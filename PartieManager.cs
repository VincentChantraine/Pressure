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

	// Amplitude (en mètres) du screen shake à la validation d'une salle,
	// et durée associée. Exportés pour pouvoir affiner sans recompiler.
	[Export] public float ShakeAmplitudeSalleValidee = 0.12f;
	[Export] public float ShakeDureeSalleValidee = 0.35f;

	private ServoGameTimer servoTimer;
	private Player joueur;
	private bool partieTerminee = false;
	private float tempsAuTP = -1f;
	private BonusTempsHud bonusTempsHud;
	private EffetsValidation effetsValidation;

	// Menu pause en cours (null si la partie tourne normalement).
	// CanvasLayer dédié pour s'assurer d'être au-dessus du HUD et du monde 3D.
	private MenuPause menuPauseCourant;
	private CanvasLayer canvasMenuPause;

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
			GameState.Instance.SalleValidee        += OnSalleValidee;
			// Permet à MarquerSalleVisitee de capter l'elapsed sans coupler les
			// puzzles au timer.
			GameState.Instance.Timer = servoTimer;
		}

		// HUD de feedback bonus de temps : créé dynamiquement pour rester
		// auto-installant (pas besoin de l'ajouter à node_3d.tscn).
		if (servoTimer != null)
		{
			bonusTempsHud = new BonusTempsHud();
			AddChild(bonusTempsHud);
			bonusTempsHud.Connecter(servoTimer);
		}

		// Flash blanc plein écran à chaque salle validée (CanvasLayer dédié
		// pour passer au-dessus du HUD progression mais sous le menu pause).
		var canvasEffets = new CanvasLayer { Layer = 40 };
		AddChild(canvasEffets);
		effetsValidation = new EffetsValidation();
		canvasEffets.AddChild(effetsValidation);

		// Boussole : flèche directionnelle vers la salle non-validée la plus proche.
		// Toggle via Options (ParametresJeu.AfficherBoussole) — toujours instanciée,
		// elle se masque toute seule si le paramètre est désactivé.
		var canvasBoussole = new CanvasLayer { Layer = 35 };
		AddChild(canvasBoussole);
		canvasBoussole.AddChild(new BoussoleHud());
	}

	private void OnSalleValidee(string salleId)
	{
		effetsValidation?.Flasher();
		joueur?.Trembler(ShakeAmplitudeSalleValidee, ShakeDureeSalleValidee);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (partieTerminee) return;
		if (menuPauseCourant != null) return; // le menu gère lui-même Échap pour se refermer
		if (@event.IsActionPressed(InputBindings.Pause))
		{
			OuvrirMenuPause();
			GetViewport().SetInputAsHandled();
		}
	}

	// Pause auto quand la fenêtre perd le focus (alt-tab, clic ailleurs…).
	// Évite que le chrono continue à tourner pendant qu'on n'est pas devant l'écran.
	public override void _Notification(int what)
	{
		if (what != NotificationApplicationFocusOut) return;
		if (partieTerminee || menuPauseCourant != null) return;
		OuvrirMenuPause();
	}

	private void OuvrirMenuPause()
	{
		if (menuPauseCourant != null) return;

		servoTimer?.Pause();
		if (joueur != null) joueur.Frozen = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		canvasMenuPause = new CanvasLayer { Layer = 50 };
		AddChild(canvasMenuPause);

		menuPauseCourant = new MenuPause();
		menuPauseCourant.Reprendre     += FermerMenuPause;
		menuPauseCourant.MenuPrincipal += SurMenuPrincipal;
		canvasMenuPause.AddChild(menuPauseCourant);
	}

	private void FermerMenuPause()
	{
		if (menuPauseCourant == null) return;

		menuPauseCourant.QueueFree();
		menuPauseCourant = null;
		canvasMenuPause?.QueueFree();
		canvasMenuPause = null;

		servoTimer?.Reprend();
		if (joueur != null) joueur.Frozen = false;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void SurMenuPrincipal()
	{
		// On retire la pause / freeze avant de changer de scène pour ne pas laisser
		// l'état figé sur le prochain Player instancié.
		servoTimer?.Reprend();
		if (joueur != null) joueur.Frozen = false;
		GameState.Instance?.ChargerMenu();
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
			GameState.Instance.SalleValidee       -= OnSalleValidee;
		}
	}
}
