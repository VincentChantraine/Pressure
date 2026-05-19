using Godot;
 
public partial class Player : CharacterBody3D
{
	[Export] public float Speed = 5.0f;
	[Export] public float SprintMultiplier = 2.0f;
 
	[Export] public float RotationSpeed = 2.5f;        // rad/s à fond
	[Export] public int SliderCenter = ArduinoConfig.AnalogCenter; // à calibrer (cf. moniteur série)
	[Export] public int SliderDeadzone = 100;          // large pour tolérer la dérive
	[Export] public bool InvertSlider = false;

	// Atténuation des inputs partagés avec un puzzle quand le joueur le regarde :
	// le slider sert aussi à actionner le LevierSequence et le joystick à tourner
	// la VanneRotation. Sans atténuation, manipuler le puzzle ferait pivoter /
	// déplacer le joueur en même temps. 0 = mouvement bloqué pendant l'interaction,
	// 1 = pas d'atténuation. Deux facteurs séparés car le joueur a besoin de garder
	// un minimum de mobilité face à la vanne (réajustement) mais peut rester immobile
	// face au levier sans gêne.
	[Export(PropertyHint.Range, "0,1,0.01")] public float FacteurAttenuationLevier = 0.15f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float FacteurAttenuationVanne  = 0.4f;

	[Export] public int LdrMin = 150;
	[Export] public int LdrMax = ArduinoConfig.AnalogMax;
	[Export] public float TorchMinEnergy = 0.0f;
	[Export] public float TorchMaxEnergy = 4.0f;
	[Export] public float TorchMinRange = 2.0f;   // portée mini (capteur éclairé)
	[Export] public float TorchMaxRange = 15.0f;  // portée maxi (capteur couvert)
	[Export] public NodePath TorchLightPath;

	// Son joué quand on commence à "couvrir" le LDR simulé (clic droit pressé).
	// La torche elle-même rampe ensuite progressivement en énergie / portée :
	// le son n'est qu'un "tick" haptique au début de l'appui.
	[Export] public AudioStream FlashlightSound;
	[Export] public float FlashlightSoundVolumeDb = -6f;

	private SpotLight3D torch;
	private AudioStreamPlayer flashlightPlayer;
 
	// --- Lift ultrason ---
	[Export] public int UltraMinCm = 5;       // en-dessous : considéré comme "main collée" → lift max
	[Export] public int UltraMaxCm = 20;      // au-dessus (ou -1) : lift = 0
	[Export] public float LiftMaxHeight = 5.0f;   // hauteur max en mètres
	[Export] public float LiftRiseSpeed = 4.0f;   // m/s en montée
	[Export] public float LiftFallSpeed = 2.0f;   // m/s en descente (plus lent = "progressif")
	[Export] public NodePath LiftTargetPath;       // ce qu'on déplace verticalement (ex: Camera3D ou un pivot)
 
	private Node3D liftTarget;
	private float currentLift = 0f;
	private Vector3 liftTargetBasePos;   // position locale initiale à préserver
 
	[Export] public NodePath ArduinoPath;

	private Node3d arduino;

	// --- Souris / pavé tactile ---
	[Export] public bool MouseLookEnabled = true;
	[Export] public float MouseSensitivity = 0.0025f;   // rad / pixel
	[Export] public bool InvertMouseY = false;
	[Export] public bool InvertMouseX = false;
	[Export] public float PitchMinDeg = -80f;
	[Export] public float PitchMaxDeg = 80f;
	[Export] public NodePath CameraPath;                // si vide → "Camera3D"

	private Camera3D cameraNode;
	private float cameraPitch = 0f;

	// Si vrai, le joueur ne peut plus bouger ni regarder autour.
	// Utilisé pendant la séquence de fin (DepthBomb + fondu noir).
	public bool Frozen { get; set; } = false;

	// --- Screen shake (caméra) ---
	// Décalage local appliqué à la caméra par-dessus sa position de base
	// (laquelle peut être modifiée par le Lift). Stocké séparément pour
	// pouvoir le composer sans corrompre liftTargetBasePos / Position.
	private Vector3 shakeOffset = Vector3.Zero;
	private float shakeAmplitude = 0f;
	private float shakeDureeRestante = 0f;
	private float shakeDureeTotale = 0f;

	/// <summary>
	/// Déclenche un screen shake : la caméra tremble pendant `duree` secondes
	/// avec une amplitude qui décroît linéairement.
	/// </summary>
	public void Trembler(float amplitude, float duree)
	{
		// Si un shake plus fort est en cours, on garde le plus impactant.
		if (duree <= 0f || amplitude <= 0f) return;
		if (amplitude * duree < shakeAmplitude * shakeDureeRestante) return;
		shakeAmplitude = amplitude;
		shakeDureeTotale = duree;
		shakeDureeRestante = duree;
	}

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNode<Node3d>(ArduinoPath);
		if (TorchLightPath != null && !TorchLightPath.IsEmpty)
			torch = GetNode<SpotLight3D>(TorchLightPath);

		if (FlashlightSound == null)
			FlashlightSound = GD.Load<AudioStream>("res://Son/Flashlight.mp3");
		flashlightPlayer = new AudioStreamPlayer
		{
			Stream = FlashlightSound,
			VolumeDb = FlashlightSoundVolumeDb,
		};
		AddChild(flashlightPlayer);
		if (LiftTargetPath != null && !LiftTargetPath.IsEmpty)
		{
			liftTarget = GetNode<Node3D>(LiftTargetPath);
			liftTargetBasePos = liftTarget.Position;
		}

		cameraNode = (CameraPath != null && !CameraPath.IsEmpty)
			? GetNodeOrNull<Camera3D>(CameraPath)
			: GetNodeOrNull<Camera3D>("Camera3D");
		if (cameraNode != null)
			cameraPitch = cameraNode.Rotation.X;

		// Surcharge les valeurs inspecteur par les paramètres utilisateur sauvegardés.
		AppliquerParametres();
		ParametresJeu.Changes += AppliquerParametres;

		if (MouseLookEnabled)
			Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void AppliquerParametres()
	{
		MouseSensitivity = ParametresJeu.SensibiliteSouris;
		InvertMouseY     = ParametresJeu.InvertY;
		InvertMouseX     = ParametresJeu.InvertX;
		RotationSpeed    = ParametresJeu.VitesseRotation;
		Speed            = ParametresJeu.VitesseMarche;
		SprintMultiplier = ParametresJeu.MultiplicateurSprint;
		if (cameraNode != null)
			cameraNode.Fov = ParametresJeu.FOV;
	}

	public override void _ExitTree()
	{
		// Désabonnement obligatoire : event statique → ParametresJeu garderait
		// sinon une référence sur ce Player après destruction (fuite mémoire).
		ParametresJeu.Changes -= AppliquerParametres;
	}

	public override void _Input(InputEvent @event)
	{
		if (!MouseLookEnabled) return;
		if (Frozen) return;

		if (@event is InputEventMouseMotion motion
			&& Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			// Yaw : on tourne le Player (additif avec le slider Arduino)
			// Signe négatif par défaut pour que "souris à droite" → "vue à droite".
			float dx = -motion.Relative.X * MouseSensitivity;
			if (InvertMouseX) dx = -dx;
			RotateY(dx);

			// Pitch : on tourne la caméra seule, clampé
			if (cameraNode != null)
			{
				float dy = motion.Relative.Y * MouseSensitivity;
				if (InvertMouseY) dy = -dy;
				cameraPitch = Mathf.Clamp(
					cameraPitch - dy,
					Mathf.DegToRad(PitchMinDeg),
					Mathf.DegToRad(PitchMaxDeg));
				Vector3 r = cameraNode.Rotation;
				r.X = cameraPitch;
				cameraNode.Rotation = r;
			}
		}

		// Échap est désormais géré par PartieManager qui ouvre le menu pause
		// (lequel libère la souris). On garde uniquement la recapture au clic.

		// Clic dans la fenêtre quand la souris est libre → on recapture
		if (@event is InputEventMouseButton mb && mb.Pressed
			&& Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}

		// Action "lampe_torche" (F par défaut) → "tick" au début ET à la fin
		// de la couverture du LDR simulé : on entend un clic à l'appui et au relâchement,
		// comme un vrai interrupteur de torche. La torche elle-même monte / descend en
		// douceur via la rampe LDR de Node3d.cs entre les deux ticks.
		// Joué seulement quand la souris est capturée (en jeu, pas en menu).
		bool torchePressEvent = @event.IsActionPressed(InputBindings.LampeTorche)
			|| @event.IsActionReleased(InputBindings.LampeTorche);
		if (torchePressEvent
			&& Input.MouseMode == Input.MouseModeEnum.Captured
			&& flashlightPlayer != null)
		{
			flashlightPlayer.Play();
		}
	}
 
	public override void _PhysicsProcess(double delta)
	{
		// Annule le shake de la frame précédente avant d'appliquer la nouvelle logique
		// (lift + recalcul du shake) : sinon le shake s'accumulerait à la position de base.
		if (cameraNode != null && shakeOffset != Vector3.Zero)
		{
			cameraNode.Position -= shakeOffset;
			shakeOffset = Vector3.Zero;
		}

		if (Frozen)
		{
			Vector3 v = Velocity;
			v.X = 0;
			v.Z = 0;
			if (!IsOnFloor()) v.Y -= 9.8f * (float)delta;
			else v.Y = 0;
			Velocity = v;
			MoveAndSlide();
			return;
		}

		Vector3 velocity = Velocity;

		if (!IsOnFloor())
			velocity.Y -= 9.8f * (float)delta;
 
		if (arduino != null)
		{
			// Quand le joueur regarde un puzzle qui réutilise les mêmes inputs,
			// on atténue fortement l'effet correspondant côté player pour éviter
			// qu'il pivote (Levier ↔ slider) ou se déplace (Vanne ↔ joystick)
			// pendant qu'il manipule le puzzle.
			float attenuationSlider   = Survol3D.CibleCourante is LevierSequence ? FacteurAttenuationLevier : 1f;
			float attenuationJoystick = Survol3D.CibleCourante is VanneRotation  ? FacteurAttenuationVanne  : 1f;

			// --- ROTATION (slider → vitesse angulaire sur Y) ---
			float rawSlider = arduino.sliderValue - SliderCenter;
			if (Mathf.Abs(rawSlider) < SliderDeadzone) rawSlider = 0;

			float sliderNorm = rawSlider / (float)(ArduinoConfig.AnalogCenter - SliderDeadzone);
			sliderNorm = Mathf.Clamp(sliderNorm, -1f, 1f);
			if (InvertSlider) sliderNorm = -sliderNorm;

			RotateY(-sliderNorm * RotationSpeed * (float)delta * attenuationSlider);

			// --- DÉPLACEMENT (joystick → vitesse en coords LOCALES du Player) ---
			float rawX = arduino.joystickX - ArduinoConfig.AnalogCenter;
			float rawY = arduino.joystickY - ArduinoConfig.AnalogCenter;
			float deadzone = 100;

			if (Mathf.Abs(rawX) < deadzone) rawX = 0;
			if (Mathf.Abs(rawY) < deadzone) rawY = 0;

			float inputX = (rawX / (float)ArduinoConfig.AnalogCenter) * attenuationJoystick;
			float inputZ = (rawY / (float)ArduinoConfig.AnalogCenter) * attenuationJoystick;
 
			float currentSpeed = Speed;
			if (arduino.isButtonPressed)
				currentSpeed *= SprintMultiplier;
 
			if (torch != null)
			{
				float t = Mathf.InverseLerp(LdrMin, LdrMax, arduino.lightValue);
				t = Mathf.Clamp(t, 0f, 1f);
				torch.SpotRange   = Mathf.Lerp(TorchMaxRange,  TorchMinRange,  t);
				torch.LightEnergy = Mathf.Lerp(TorchMaxEnergy, TorchMinEnergy, t);
			}
 
			Vector3 localDir = new Vector3(inputX, 0, inputZ);
			Vector3 worldDir = Transform.Basis * localDir;
			velocity.X = worldDir.X * currentSpeed;
			velocity.Z = worldDir.Z * currentSpeed;
		}
 
		// --- Lift ultrason : main détectée = on monte, sinon on redescend ---
		if (arduino != null && liftTarget != null)
		{
			int d = arduino.ultraDistCm;
			bool mainDetectee = (d > 0 && d <= UltraMaxCm);
 
			float liftCible = mainDetectee ? LiftMaxHeight : 0f;
 
			float vitesse = (liftCible > currentLift) ? LiftRiseSpeed : LiftFallSpeed;
			currentLift = Mathf.MoveToward(currentLift, liftCible, vitesse * (float)delta);
 
			Vector3 p = liftTargetBasePos;
			p.Y += currentLift;
			liftTarget.Position = p;
		}
 
		Velocity = velocity;
		MoveAndSlide();

		// Met à jour le shake et applique l'offset à la caméra. À faire APRÈS
		// MoveAndSlide pour rester au-dessus du lift / de la rotation de pitch.
		AppliquerShake((float)delta);
	}

	private void AppliquerShake(float delta)
	{
		if (cameraNode == null) return;
		if (shakeDureeRestante <= 0f) return;

		shakeDureeRestante -= delta;
		if (shakeDureeRestante <= 0f)
		{
			shakeDureeRestante = 0f;
			shakeAmplitude = 0f;
			return;
		}

		// Décroissance linéaire de l'amplitude jusqu'à 0.
		float t = shakeDureeRestante / shakeDureeTotale;
		float amp = shakeAmplitude * t;

		// Vecteur aléatoire dans une sphère unitaire, projeté sur le plan local
		// de la caméra (X/Y) — pas de Z pour éviter le "near plane" qui clipperait.
		shakeOffset = new Vector3(
			(float)GD.RandRange(-1.0, 1.0) * amp,
			(float)GD.RandRange(-1.0, 1.0) * amp,
			0f);
		cameraNode.Position += shakeOffset;
	}

	public void TeleporterA(Vector3 cible)
	{
		// Reset Velocity + lift : sinon la vélocité résiduelle ferait glisser le joueur
		// au point d'arrivée, et le lift ultrason resterait actif (joueur "en l'air").
		// Pour un TP "fluide" (fondu noir), le caller doit en plus désactiver _PhysicsProcess
		// autour de l'appel — cf. PorteRondeTP.LancerSequence().
		GlobalPosition = cible;
		Velocity = Vector3.Zero;

		if (liftTarget != null)
		{
			currentLift = 0f;
			liftTarget.Position = liftTargetBasePos;
		}
	}
}
