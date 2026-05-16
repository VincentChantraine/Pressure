using Godot;
using System;
using System.IO.Ports;

public partial class Node3d : Node3D
{
	// =========================
	// FALLBACK CLAVIER
	// =========================
	[Export] public bool UseKeyboardFallback = true;

	// Amplitude simulée pour les axes analogiques (0..1023, centre 512)
	// 400 = on reste un peu sous la saturation pour éviter les edge cases de deadzone
	private const int KB_AXIS_AMPLITUDE = 400;

	// Encodeur clavier : cooldown entre deux crans pour que l'appui maintenu
	// ne spamme pas 60 crans/seconde
	[Export] public float KeyboardEncoderCooldown = 0.08f;
	private float kbEncoderTimer = 0f;

	// Encodeur : accumulation des crans clavier à consommer
	private int kbEncoderPending = 0;

	// État précédent des touches à front montant (bouton encodeur)
	private bool kbEncoderBtnPrev = false;
	private bool kbEncoderBtnJustPressed = false;

	// RFID clavier : front montant sur 1 et 2
	private bool kbRfid1Prev = false;
	private bool kbRfid2Prev = false;

	// =========================
	// ÉTAT CAPTEURS (inchangé)
	// =========================
	private SerialPort serialPort;

	public int joystickX = 512;
	public int joystickY = 512;
	public int sliderValue = 512;
	public bool isButtonPressed = false;
	// Bouton "interaction" : isButtonPressed (Shift / bouton Arduino) OU clic gauche souris.
	// Volontairement séparé pour que le clic gauche ne déclenche PAS le sprint.
	public bool isInteractPressed = false;
	public int lightValue = 512;
	public int ultraDistCm = -1;
	public bool arduinoReady = false;

	// Valeurs brutes reçues de l'Arduino avant override clavier
	// (utile si tu veux un jour logguer ou afficher les deux)
	private int arduinoJoystickX = 512;
	private int arduinoJoystickY = 512;
	private int arduinoSliderValue = 512;
	private bool arduinoButtonPressed = false;
	private int arduinoLightValue = 512;
	private int arduinoUltraDistCm = -1;

	// =========================
	// ENCODEUR (nouveau : exposé au Coffre)
	// =========================
	private int encoderDeltaAccum = 0;      // crans reçus de l'Arduino depuis la dernière consommation
	private bool encoderBtnPrevArduino = false;
	private bool encoderBtnJustPressedArduino = false;

	/// <summary>
	/// Consomme et retourne le delta de crans accumulé depuis le dernier appel.
	/// Combine crans Arduino + crans clavier.
	/// </summary>
	public int ConsumeEncoderDelta()
	{
		int total = encoderDeltaAccum + kbEncoderPending;
		encoderDeltaAccum = 0;
		kbEncoderPending = 0;
		return total;
	}

	/// <summary>
	/// True pendant UNE frame quand le bouton encodeur vient d'être pressé
	/// (Arduino OU clavier).
	/// </summary>
	public bool EncoderButtonJustPressed => encoderBtnJustPressedArduino || kbEncoderBtnJustPressed;

	public override void _Ready()
	{
		string[] ports = SerialPort.GetPortNames();
		GD.Print("Ports détectés : " + string.Join(", ", ports));

		try
		{
			serialPort = new SerialPort("COM5", 115200)
			{
				ReadTimeout = 500,
				WriteTimeout = 200,
				DtrEnable = true,
				RtsEnable = true
			};
			serialPort.Open();
			GD.Print("Port série ouvert ! (attente handshake READY...)");
		}
		catch (Exception e)
		{
			GD.PrintErr("Erreur ouverture port : " + e.Message);
			GD.Print("(Fallback clavier actif : " + UseKeyboardFallback + ")");
		}
	}

	float timer = 0;

	public override void _Process(double delta)
	{
		timer += (float)delta;

		if (timer > 1.0f)
		{
			timer = 0;
			//SendServoSpeed(30);
		}

		// --- Reset des flags à front montant en début de frame ---
		encoderBtnJustPressedArduino = false;
		kbEncoderBtnJustPressed = false;

		// --- 1. Lecture Arduino (inchangée dans la logique, mais écrit dans les champs "arduinoXxx") ---
		LireArduino();

		// --- 2. Application des valeurs Arduino aux champs publics ---
		joystickX = arduinoJoystickX;
		joystickY = arduinoJoystickY;
		sliderValue = arduinoSliderValue;
		isButtonPressed = arduinoButtonPressed;
		lightValue = arduinoLightValue;
		ultraDistCm = arduinoUltraDistCm;

		// --- 3. Override clavier par-dessus ---
		if (UseKeyboardFallback)
			AppliquerOverrideClavier((float)delta);

		// --- 4. Interaction : bouton Arduino OU clic gauche souris ---
		// NB : on lit arduinoButtonPressed (et PAS isButtonPressed) pour exclure le Shift clavier,
		// qui doit continuer à servir au sprint sans déclencher d'interaction.
		// Pour réactiver Shift comme bouton d'interaction, décommente la ligne ci-dessous.
		isInteractPressed = arduinoButtonPressed
			// || Input.IsKeyPressed(Key.Shift)
			|| Input.IsMouseButtonPressed(MouseButton.Left);
	}

	private void LireArduino()
	{
		if (serialPort == null || !serialPort.IsOpen) return;

		try
		{
			while (serialPort.BytesToRead > 0)
			{
				string message = serialPort.ReadLine().Trim();
				GD.Print("Reçu : " + message);

				if (message.StartsWith("RFID:"))
				{
					string uid = message.Substring(5).Trim();
					if (!string.IsNullOrEmpty(uid) && GameState.Instance != null)
						GameState.Instance.NotifyRfidScan(uid);
					continue;
				}

			if (message.Contains("READY"))
			{
				if (!arduinoReady)
				{
					arduinoReady = true;
					GD.Print("[Arduino] Handshake reçu.");
				}
				// On ne continue PAS : la ligne peut contenir des données valides avant/après READY
				// On laisse le parseur ci-dessous tenter d'extraire ce qu'il peut
			}

				string[] parts = message.Split(
					new[] { ' ', '\t' },
					StringSplitOptions.RemoveEmptyEntries
				);

				bool encBtnNow = encoderBtnPrevArduino; // conserver l'état si pas de ENCBTN dans la ligne

				foreach (string part in parts)
				{
					if (part.StartsWith("X:") && int.TryParse(part.Substring(2), out int x))
						arduinoJoystickX = x;
					else if (part.StartsWith("Y:") && int.TryParse(part.Substring(2), out int y))
						arduinoJoystickY = y;
					else if (part.StartsWith("SLD:") && int.TryParse(part.Substring(4), out int s))
						arduinoSliderValue = s;
					else if (part.StartsWith("BTN:") && int.TryParse(part.Substring(4), out int b))
						arduinoButtonPressed = (b == 1);
					else if (part.StartsWith("LDR:") && int.TryParse(part.Substring(4), out int l))
						arduinoLightValue = l;
					else if (part.StartsWith("DIST:") && int.TryParse(part.Substring(5), out int d))
						arduinoUltraDistCm = d;
					else if (part.StartsWith("ENC:") && int.TryParse(part.Substring(4), out int encDelta))
						encoderDeltaAccum += encDelta;
					else if (part.StartsWith("ENCBTN:") && int.TryParse(part.Substring(7), out int eb))
						encBtnNow = (eb == 1);
				}

				// Front montant du bouton encodeur (Arduino)
				if (encBtnNow && !encoderBtnPrevArduino)
					encoderBtnJustPressedArduino = true;
				encoderBtnPrevArduino = encBtnNow;
			}
		}
		catch (TimeoutException) { }
		catch (Exception e)
		{
			GD.PrintErr("Erreur lecture : " + e.Message);
		}
	}

	private void AppliquerOverrideClavier(float delta)
	{
		// --- Joystick X (Q/D) ---
		bool q = Input.IsKeyPressed(Key.Q);
		bool d = Input.IsKeyPressed(Key.D);
		if (q && !d) joystickX = 512 - KB_AXIS_AMPLITUDE;
		else if (d && !q) joystickX = 512 + KB_AXIS_AMPLITUDE;
		// sinon : on garde la valeur Arduino déjà copiée

		// --- Joystick Y (Z/S) ---
		// Convention : Z = avant = Y bas (car le code fait inputZ = rawY/512 et dir.Z négatif = avant en Godot)
		// Dans ton Player.cs, inputZ positif = avancer ? À vérifier. On suit la convention du joystick physique :
		// joystickY > 512 = on pousse dans un sens, < 512 = l'autre. Tu ajusteras si c'est inversé avec `InvertSlider`-style.
		bool z = Input.IsKeyPressed(Key.Z);
		bool s = Input.IsKeyPressed(Key.S);
		if (z && !s) joystickY = 512 - KB_AXIS_AMPLITUDE;
		else if (s && !z) joystickY = 512 + KB_AXIS_AMPLITUDE;

		// --- Slider rotation caméra (A/E) ---
		//bool a = Input.IsKeyPressed(Key.A);
		//bool e = Input.IsKeyPressed(Key.E);
		//if (a && !e) sliderValue = 512 - KB_AXIS_AMPLITUDE;
		//else if (e && !a) sliderValue = 512 + KB_AXIS_AMPLITUDE;

		// --- Slider (A/E) ---
		bool a = Input.IsKeyPressed(Key.A);
		bool e = Input.IsKeyPressed(Key.E);
		if (a && !e) sliderValue = 0;       // butée gauche franche
		else if (e && !a) sliderValue = 1023; // butée droite franche

		// --- Bouton joystick (Shift) = sprint / interaction porte ---
		// OR logique : si Shift OU bouton Arduino pressé
		if (Input.IsKeyPressed(Key.Shift))
			isButtonPressed = true;

		// --- LDR : capteur couvert = lampe max ---
		// Ancienne touche (L) désactivée temporairement, décommenter pour réactiver :
		// if (Input.IsKeyPressed(Key.L))
		//     lightValue = 100;
		if (Input.IsMouseButtonPressed(MouseButton.Right))
			lightValue = 100; // sous LdrMin (150) → InverseLerp clampé à 0 → lampe max

		// --- Ultrason (Espace) : main au-dessus du capteur ---
		if (Input.IsKeyPressed(Key.Space))
			ultraDistCm = 10; // valeur arbitraire > 0, dans la plage utile

		// --- Encodeur (flèches ← →) ---
		kbEncoderTimer -= delta;
		if (kbEncoderTimer <= 0f)
		{
			if (Input.IsKeyPressed(Key.Left))
			{
				kbEncoderPending -= 1;
				kbEncoderTimer = KeyboardEncoderCooldown;
			}
			else if (Input.IsKeyPressed(Key.Right))
			{
				kbEncoderPending += 1;
				kbEncoderTimer = KeyboardEncoderCooldown;
			}
		}

		// --- Bouton encodeur (Entrée) — front montant clavier ---
		bool enterNow = Input.IsKeyPressed(Key.Enter);
		if (enterNow && !kbEncoderBtnPrev)
			kbEncoderBtnJustPressed = true;
		kbEncoderBtnPrev = enterNow;

		// --- RFID (1 et 2) — front montant uniquement ---
		// On envoie les VRAIS UIDs hardcodés dans GameState pour que
		// EstBadge1()/EstBadge2() matchent en mode clavier.
		bool k1Now = Input.IsKeyPressed(Key.Key1);
		if (k1Now && !kbRfid1Prev && GameState.Instance != null)
			GameState.Instance.NotifyRfidScan(GameState.BADGE_1_UID);
		kbRfid1Prev = k1Now;

		bool k2Now = Input.IsKeyPressed(Key.Key2);
		if (k2Now && !kbRfid2Prev && GameState.Instance != null)
			GameState.Instance.NotifyRfidScan(GameState.BADGE_2_UID);
		kbRfid2Prev = k2Now;
	}

	public void SendServoSpeed(int speedSigned)
	{
		if (!arduinoReady) return;
		if (serialPort == null || !serialPort.IsOpen) return;

		int angle = Mathf.Clamp(90 + speedSigned, 0, 180);
		try
		{
			serialPort.WriteLine($"SERVO:{angle}");
		}
		catch (TimeoutException)
		{
			GD.PrintErr("Timeout écriture servo (buffer saturé ?)");
		}
		catch (Exception ex)
		{
			GD.PrintErr("Erreur écriture servo : " + ex.Message);
		}
	}

	public override void _ExitTree()
	{
		if (serialPort != null && serialPort.IsOpen)
		{
			try { serialPort.WriteLine("SERVO:90"); } catch { }
			serialPort.Close();
		}
	}
}
