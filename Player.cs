using Godot;
 
public partial class Player : CharacterBody3D
{
	[Export] public float Speed = 5.0f;
	[Export] public float SprintMultiplier = 2.0f;
 
	[Export] public float RotationSpeed = 2.5f;        // rad/s à fond
	[Export] public int SliderCenter = 512;            // à calibrer (cf. moniteur série)
	[Export] public int SliderDeadzone = 100;          // large pour tolérer la dérive
	[Export] public bool InvertSlider = false;
 
	[Export] public int LdrMin = 150;
	[Export] public int LdrMax = 1023;
	[Export] public float TorchMinEnergy = 0.0f;
	[Export] public float TorchMaxEnergy = 4.0f;
	[Export] public float TorchMinRange = 2.0f;   // portée mini (capteur éclairé)
	[Export] public float TorchMaxRange = 15.0f;  // portée maxi (capteur couvert)
	[Export] public NodePath TorchLightPath;
 
	private SpotLight3D torch;
 
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
	[Export] public float PitchMinDeg = -80f;
	[Export] public float PitchMaxDeg = 80f;
	[Export] public NodePath CameraPath;                // si vide → "Camera3D"

	private Camera3D cameraNode;
	private float cameraPitch = 0f;

	// Si vrai, le joueur ne peut plus bouger ni regarder autour.
	// Utilisé pendant la séquence de fin (DepthBomb + fondu noir).
	public bool Frozen { get; set; } = false;

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNode<Node3d>(ArduinoPath);
		if (TorchLightPath != null && !TorchLightPath.IsEmpty)
			torch = GetNode<SpotLight3D>(TorchLightPath);
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

		if (MouseLookEnabled)
			Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (!MouseLookEnabled) return;
		if (Frozen) return;

		if (@event is InputEventMouseMotion motion
			&& Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			// Yaw : on tourne le Player (additif avec le slider Arduino)
			RotateY(-motion.Relative.X * MouseSensitivity);

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

		// Esc : libérer / re-capturer la souris (utile pour alt-tab)
		if (@event is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
		{
			Input.MouseMode = (Input.MouseMode == Input.MouseModeEnum.Captured)
				? Input.MouseModeEnum.Visible
				: Input.MouseModeEnum.Captured;
		}

		// Clic dans la fenêtre quand la souris est libre → on recapture
		if (@event is InputEventMouseButton mb && mb.Pressed
			&& Input.MouseMode != Input.MouseModeEnum.Captured)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
	}
 
	public override void _PhysicsProcess(double delta)
	{
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
			// --- ROTATION (slider → vitesse angulaire sur Y) ---
			float rawSlider = arduino.sliderValue - SliderCenter;
			if (Mathf.Abs(rawSlider) < SliderDeadzone) rawSlider = 0;
 
			float sliderNorm = rawSlider / (512f - SliderDeadzone);
			sliderNorm = Mathf.Clamp(sliderNorm, -1f, 1f);
			if (InvertSlider) sliderNorm = -sliderNorm;
 
			RotateY(-sliderNorm * RotationSpeed * (float)delta);
 
			// --- DÉPLACEMENT (joystick → vitesse en coords LOCALES du Player) ---
			float rawX = arduino.joystickX - 512;
			float rawY = arduino.joystickY - 512;
			float deadzone = 100;
 
			if (Mathf.Abs(rawX) < deadzone) rawX = 0;
			if (Mathf.Abs(rawY) < deadzone) rawY = 0;
 
			float inputX = rawX / 512f;
			float inputZ = rawY / 512f;
 
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
	}
 
	public void TeleporterA(Vector3 cible)
	{
		// TODO : implémenter la téléportation (désactiver la physique le temps du snap, etc.)
		GlobalPosition = cible;
	}
}
