using Godot;
using System;
using System.IO.Ports;

public partial class Node3d : Node3D
{
	// =========================
	// CONNEXION SÉRIE
	// =========================
	// Port à essayer en premier. Éditable depuis l'inspecteur Godot,
	// ce qui évite de recompiler quand l'Arduino n'est pas sur COM5.
	[Export] public string ComPort = "COM5";
	[Export] public int BaudRate = 115200;
	// Si l'ouverture du port configuré échoue, on tente automatiquement
	// les autres ports détectés par le système. Le handshake "READY" du firmware
	// fait office de vérification — un port qui n'est pas notre Arduino
	// ne renverra jamais "READY" donc on peut le laisser ouvert sans dégât
	// (le fallback clavier prend le relais).
	[Export] public bool AutoDetectPortIfMissing = true;
	// Active le log à chaque ligne série reçue (très verbeux, ~60 Hz).
	// Laisse à false en production pour ne pas spammer la console.
	[Export] public bool DebugSerialLog = false;

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

	// Lampe torche clavier : durée pour "couvrir" puis "découvrir" le LDR simulé.
	// Plus on tient le bouton, plus le capteur est progressivement masqué → la
	// portée et l'énergie de la torche montent en douceur (cf. Player.cs).
	// TorchDischargeTime < TorchChargeTime donne un effet "redécouverte" rapide,
	// l'inverse simulerait un œil qui s'adapte plus vite que la main ne couvre.
	[Export] public float TorchChargeTime = 1.2f;
	[Export] public float TorchDischargeTime = 0.6f;
	private float kbTorchCharge = 0f; // 0 = capteur éclairé (torche off), 1 = capteur couvert (torche max)

	// Bouton encodeur clavier : levé à true pendant 1 frame quand "valider" est juste pressé,
	// consommé via EncoderButtonJustPressed.
	private bool kbEncoderBtnJustPressed = false;

	// =========================
	// ÉTAT CAPTEURS (inchangé)
	// =========================
	private SerialPort serialPort;

	// Buffer d'accumulation des octets reçus du port série.
	// On lit avec ReadExisting() (non-bloquant) plutôt que ReadLine() (peut bloquer
	// jusqu'à ReadTimeout=500ms si la ligne courante n'a pas encore son \n).
	// On découpe nous-mêmes par \n dans ce buffer, en conservant la dernière ligne
	// partielle pour la frame suivante.
	private string serialReadBuffer = "";

	// Garde-fou contre un buffer qui grossit indéfiniment si le parser ne suit pas
	// (très improbable, mais évite une fuite mémoire en cas de bug).
	private const int SERIAL_BUFFER_MAX = 16 * 1024;

	public int joystickX = ArduinoConfig.AnalogCenter;
	public int joystickY = ArduinoConfig.AnalogCenter;
	public int sliderValue = ArduinoConfig.AnalogCenter;

	// Valeur slider à utiliser pour le LEVIER uniquement : sliderValue de l'Arduino,
	// overridé par Q/E en mode clavier. Volontairement décorrélé de sliderValue (lu
	// par Player.cs pour la rotation) pour que les touches Q/E n'aient AUCUN effet
	// sur la tête du joueur — seulement sur le levier.
	public int LeverSliderValue
	{
		get
		{
			int v = sliderValue;
			if (UseKeyboardFallback)
			{
				if (Input.IsActionPressed(InputBindings.RotationGauche)) v = ArduinoConfig.AnalogMin;
				else if (Input.IsActionPressed(InputBindings.RotationDroite)) v = ArduinoConfig.AnalogMax;
			}
			return v;
		}
	}
	public bool isButtonPressed = false;
	// Bouton "interaction" : isButtonPressed (Shift / bouton Arduino) OU clic gauche souris.
	// Volontairement séparé pour que le clic gauche ne déclenche PAS le sprint.
	public bool isInteractPressed = false;
	public int lightValue = ArduinoConfig.AnalogCenter;
	public int ultraDistCm = -1;
	public bool arduinoReady = false;

	// Valeurs brutes reçues de l'Arduino avant override clavier
	// (utile si tu veux un jour logguer ou afficher les deux)
	private int arduinoJoystickX = ArduinoConfig.AnalogCenter;
	private int arduinoJoystickY = ArduinoConfig.AnalogCenter;
	private int arduinoSliderValue = ArduinoConfig.AnalogCenter;
	private bool arduinoButtonPressed = false;
	private int arduinoLightValue = ArduinoConfig.AnalogCenter;
	private int arduinoUltraDistCm = -1;

	// =========================
	// BUFFER D'INTERACTION (pavé tactile / clics très brefs)
	// =========================
	// Sur un pavé tactile, un "tap" peut presser + relâcher dans la même
	// frame Godot. IsActionPressed retourne alors false en _Process car
	// l'action est déjà inactive. Les consommateurs (Porte, PorteRondeTP…)
	// détectent un front montant via boutonPrecedent → ratent le tap.
	// Solution : on capte IsActionJustPressed (qui voit le front même si
	// release est dans la même frame) et on maintient isInteractPressed
	// à true pendant quelques frames pour que le front montant soit vu.
	private const int INTERACT_LATCH_FRAMES = 3;
	private int interactLatchFrames = 0;

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

		if (!TryOpenPort(ComPort) && AutoDetectPortIfMissing)
		{
			foreach (string p in ports)
			{
				if (p == ComPort) continue; // déjà essayé
				GD.Print($"[Arduino] Tentative auto sur {p}...");
				if (TryOpenPort(p))
				{
					GD.Print($"[Arduino] Connecté sur {p} (fallback auto).");
					break;
				}
			}
		}

		if (serialPort == null || !serialPort.IsOpen)
			GD.Print("(Aucun port série ouvert — Fallback clavier actif : " + UseKeyboardFallback + ")");
	}

	private bool TryOpenPort(string portName)
	{
		if (string.IsNullOrEmpty(portName)) return false;
		try
		{
			var sp = new SerialPort(portName, BaudRate)
			{
				ReadTimeout = 500,
				WriteTimeout = 200,
				DtrEnable = true,
				RtsEnable = true
			};
			sp.Open();
			serialPort = sp;
			GD.Print($"Port série {portName} ouvert ! (attente handshake READY...)");
			return true;
		}
		catch (Exception e)
		{
			GD.PrintErr($"Erreur ouverture {portName} : {e.Message}");
			return false;
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

		// --- 4. Interaction : bouton Arduino OU action "interagir" (clic gauche par défaut) ---
		// NB : on lit arduinoButtonPressed (et PAS isButtonPressed) pour exclure le Sprint clavier,
		// qui doit continuer à servir au sprint sans déclencher d'interaction.
		bool interactNow = arduinoButtonPressed
			|| Input.IsActionPressed(InputBindings.Interagir);

		// Latch trackpad : un tap très bref alimente le buffer pour que
		// le front montant soit vu côté puzzles.
		if (Input.IsActionJustPressed(InputBindings.Interagir))
			interactLatchFrames = INTERACT_LATCH_FRAMES;
		else if (interactLatchFrames > 0 && !interactNow)
			interactLatchFrames--;

		isInteractPressed = interactNow || interactLatchFrames > 0;
	}

	private void LireArduino()
	{
		if (serialPort == null || !serialPort.IsOpen) return;

		try
		{
			// 1) Lecture NON-BLOQUANTE de tout ce qui est dans le buffer du port.
			// ReadExisting() retourne immédiatement (vs ReadLine qui peut bloquer
			// jusqu'à 500ms si la ligne n'a pas encore son \n — cause possible de
			// micro-stutters à 60Hz si l'Arduino redémarre / le câble bouge).
			if (serialPort.BytesToRead > 0)
			{
				serialReadBuffer += serialPort.ReadExisting();

				// Garde-fou : si le buffer dépasse la limite (parser bloqué, bug...),
				// on tronque proprement à la dernière ligne complète pour ne pas
				// fuir de mémoire indéfiniment.
				if (serialReadBuffer.Length > SERIAL_BUFFER_MAX)
				{
					int lastNl = serialReadBuffer.LastIndexOf('\n');
					serialReadBuffer = lastNl >= 0
						? serialReadBuffer.Substring(lastNl + 1)
						: "";
					GD.PrintErr("[Arduino] Buffer série débordé — vidé.");
				}
			}

			// 2) Découpe en lignes complètes ; la dernière partielle reste dans le buffer.
			int newlineIdx;
			while ((newlineIdx = serialReadBuffer.IndexOf('\n')) >= 0)
			{
				string message = serialReadBuffer.Substring(0, newlineIdx).Trim();
				serialReadBuffer = serialReadBuffer.Substring(newlineIdx + 1);

				if (string.IsNullOrEmpty(message)) continue;

				if (DebugSerialLog)
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
		// --- Joystick X (gauche / droite) ---
		bool gauche = Input.IsActionPressed(InputBindings.Gauche);
		bool droite = Input.IsActionPressed(InputBindings.Droite);
		if (gauche && !droite) joystickX = ArduinoConfig.AnalogCenter - KB_AXIS_AMPLITUDE;
		else if (droite && !gauche) joystickX = ArduinoConfig.AnalogCenter + KB_AXIS_AMPLITUDE;
		// sinon : on garde la valeur Arduino déjà copiée

		// --- Joystick Y (avancer / reculer) ---
		// Convention : avancer = Y bas (car le code fait inputZ = rawY/512 et dir.Z négatif = avant en Godot)
		bool avancer = Input.IsActionPressed(InputBindings.Avancer);
		bool reculer = Input.IsActionPressed(InputBindings.Reculer);
		if (avancer && !reculer) joystickY = ArduinoConfig.AnalogCenter - KB_AXIS_AMPLITUDE;
		else if (reculer && !avancer) joystickY = ArduinoConfig.AnalogCenter + KB_AXIS_AMPLITUDE;

		// Q/E ne touche PLUS sliderValue : la rotation caméra est gérée par la souris,
		// et on ne veut aucun effet parasite (même atténué) sur la tête du joueur.
		// L'override Q/E pour le levier est calculé dans LeverSliderValue, lu par
		// LevierSequence uniquement.

		// --- Sprint (Shift) = bouton joystick Arduino côté clavier ---
		if (Input.IsActionPressed(InputBindings.Sprint))
			isButtonPressed = true;

		// --- Lampe torche (F MAINTENU) : capteur LDR simulé progressif ---
		// Au lieu d'un toggle binaire, on rampe kbTorchCharge entre 0 (capteur
		// éclairé, torche off) et 1 (capteur entièrement couvert, torche max).
		// Tenir le bouton "couvre" le LDR sur TorchChargeTime secondes ; le
		// relâcher le "redécouvre" sur TorchDischargeTime. La conversion en
		// lightValue passe par les bornes AnalogMax (LDR à fond éclairé) et 100
		// (sous LdrMin=150 → InverseLerp clampé à 0 dans Player.cs → torche max).
		bool torcheTenue = Input.IsActionPressed(InputBindings.LampeTorche);
		float duree = torcheTenue ? TorchChargeTime : TorchDischargeTime;
		float pas = duree > 0f ? delta / duree : 1f;
		kbTorchCharge = Mathf.MoveToward(kbTorchCharge, torcheTenue ? 1f : 0f, pas);
		lightValue = (int)Mathf.Lerp(ArduinoConfig.AnalogMax, 100f, kbTorchCharge);

		// --- Lift (R MAINTENU) : main au-dessus du capteur ultrason ---
		if (Input.IsActionPressed(InputBindings.Lift))
			ultraDistCm = 10; // valeur arbitraire > 0, dans la plage utile

		// --- Encodeur (← / →) avec cooldown pour éviter le spam ---
		kbEncoderTimer -= delta;
		if (kbEncoderTimer <= 0f)
		{
			if (Input.IsActionPressed(InputBindings.EncodeurGauche))
			{
				kbEncoderPending -= 1;
				kbEncoderTimer = KeyboardEncoderCooldown;
			}
			else if (Input.IsActionPressed(InputBindings.EncodeurDroite))
			{
				kbEncoderPending += 1;
				kbEncoderTimer = KeyboardEncoderCooldown;
			}
		}

		// --- Bouton encodeur (Entrée) — front montant géré par Godot ---
		if (Input.IsActionJustPressed(InputBindings.Valider))
			kbEncoderBtnJustPressed = true;

		// --- RFID (1 et 2) — front montant ---
		// On envoie les VRAIS UIDs hardcodés dans GameState pour que
		// EstBadge1()/EstBadge2() matchent en mode clavier.
		if (Input.IsActionJustPressed(InputBindings.Badge1) && GameState.Instance != null)
			GameState.Instance.NotifyRfidScan(GameState.BADGE_1_UID);

		if (Input.IsActionJustPressed(InputBindings.Badge2) && GameState.Instance != null)
			GameState.Instance.NotifyRfidScan(GameState.BADGE_2_UID);
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
