using Godot;

// Trigger "porte ronde" style Fallout 4 :
//   - le joueur entre dans la zone
//   - il appuie sur le bouton joystick (Arduino BTN, même touche que les portes / interrupteur / commandes BT-01)
//   - la porte fait une petite animation (léger recul)
//   - fondu au noir, téléportation, fondu retour
public partial class PorteRondeTP : Area3D
{
	[Export] public NodePath ArduinoPath;
	[Export] public NodePath PorteRondePath;   // Node3D de la porte ronde à animer
	[Export] public NodePath CiblePath;        // Marker3D / Node3D point d'arrivée
	[Export] public Vector3 CiblePosition;     // Fallback si CiblePath vide

	// --- Animation porte (rotation type "volant qui tourne") ---
	// Rotation en degrés appliquée en repère MONDE (pas dans le repère local de la porte).
	// Les axes locaux de la porte sont rotés par sa transformation ; pour tourner comme
	// un volant autour de la normale à la face, on utilise les axes monde directement.
	// X monde = perpendiculaire à la face de la porte (l'axe "volant").
	[Export] public Vector3 RotationMondeAnimDegres = new Vector3(25f, 0f, 0f);
	[Export] public float DureeAnimationPorte = 0.6f;

	// Décalage (en coords LOCALES de la porte) entre l'origine du modèle et le centre
	// visible. Laisse à 0 si l'origine est déjà au centre (cas vu dans l'éditeur).
	[Export] public Vector3 PivotOffsetLocal = Vector3.Zero;

	// --- Fondu noir ---
	[Export] public float DureeFondu = 0.5f;     // temps fade in et fade out
	[Export] public float DureeNoirComplet = 0.25f; // temps passé en noir total avant fade out

	// --- Son optionnel ---
	[Export] public AudioStream SonInteraction;
	[Export] public float VolumeDbInteraction = -4f;

	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();
	[Signal] public delegate void TeleportationDeclencheeEventHandler();

	private Node3d arduino;
	private Node3D porteRonde;
	private Transform3D porteRondeTransformBase;
	private AudioStreamPlayer3D playerSon;

	private bool joueurDansZone = false;
	private bool boutonPrecedent = false;
	private bool declenche = false;
	private Player joueurCourant;

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNodeOrNull<Node3d>(ArduinoPath);

		if (PorteRondePath != null && !PorteRondePath.IsEmpty)
		{
			porteRonde = GetNodeOrNull<Node3D>(PorteRondePath);
			if (porteRonde != null)
				porteRondeTransformBase = porteRonde.Transform;
		}

		if (SonInteraction != null)
		{
			playerSon = new AudioStreamPlayer3D();
			playerSon.Stream = SonInteraction;
			playerSon.VolumeDb = VolumeDbInteraction;
			AddChild(playerSon);
		}

		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;

		AddToGroup("interactif");
		SetMeta("libelle_interaction", "Franchir la porte");

		// Cible de boussole pour la phase finale (après franchissement porte_sortie).
		GameState.Instance?.RegistrerAncreSalle("exit_teleport", this);
	}

	private void OnBodyEntered(Node3D body)
	{
		if (declenche) return;
		if (body is Player p)
		{
			joueurDansZone = true;
			joueurCourant = p;
			// Évite un faux déclenchement si le bouton est déjà tenu (ex: Shift maintenu pour sprinter)
			boutonPrecedent = arduino?.isInteractPressed ?? false;
			EmitSignal(SignalName.JoueurEntreZone);

			// Approximation simple : entrer dans la zone du sas final = avoir
			// franchi porte_sortie. La boussole bascule alors sur PorteRonde.
			if (GameState.Instance != null)
				GameState.Instance.PorteSortieFranchie = true;
		}
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is Player)
		{
			joueurDansZone = false;
			joueurCourant = null;
			EmitSignal(SignalName.JoueurSortZone);
		}
	}

	public override void _Process(double delta)
	{
		if (declenche || !joueurDansZone || arduino == null) return;

		bool boutonActuel = arduino.isInteractPressed;
		bool frontMontant = boutonActuel && !boutonPrecedent;
		boutonPrecedent = boutonActuel;

		if (frontMontant)
		{
			declenche = true;
			EmitSignal(SignalName.TeleportationDeclenchee);
			_ = LancerSequence();
		}
	}

	private async System.Threading.Tasks.Task LancerSequence()
	{
		// Capture locale : le joueur va sortir de la zone au moment du TP (BodyExited ->
		// joueurCourant remis à null), il faut donc garder une référence stable ici.
		Player joueur = joueurCourant;

		// Coupe la physique du joueur le temps de la séquence pour éviter qu'il bouge pendant le fondu
		if (joueur != null)
			joueur.SetPhysicsProcess(false);

		if (playerSon != null && playerSon.Stream != null)
			playerSon.Play();

		// Animation "volant qui tourne" : rotation autour des axes MONDE (pré-multiplication).
		// rWorld * baseBasis = rotation appliquée DANS le repère monde après transformation locale,
		// ce qui correspond à "tourner autour de l'axe X monde" pour X=25° (axe normale à la porte).
		if (porteRonde != null)
		{
			Basis rWorld = Basis.FromEuler(new Vector3(
				Mathf.DegToRad(RotationMondeAnimDegres.X),
				Mathf.DegToRad(RotationMondeAnimDegres.Y),
				Mathf.DegToRad(RotationMondeAnimDegres.Z)));

			Basis baseBasis = porteRondeTransformBase.Basis;
			Vector3 basePos = porteRondeTransformBase.Origin;
			Basis targetBasis = rWorld * baseBasis;
			// Garde le pivot (basePos + offset local en monde) fixe pendant la rotation.
			Vector3 pivotOffsetMonde = baseBasis * PivotOffsetLocal;
			Vector3 targetPos = basePos + pivotOffsetMonde - rWorld * pivotOffsetMonde;
			Transform3D transformCible = new Transform3D(targetBasis, targetPos);

			var tween = CreateTween();
			tween.TweenProperty(porteRonde, "transform", transformCible, DureeAnimationPorte * 0.5f)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(porteRonde, "transform", porteRondeTransformBase, DureeAnimationPorte * 0.5f)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		}

		// Création du fondu (CanvasLayer + ColorRect plein écran) en runtime
		var canvas = new CanvasLayer();
		canvas.Layer = 100;
		var rect = new ColorRect();
		rect.Color = new Color(0f, 0f, 0f, 0f);
		rect.AnchorRight = 1f;
		rect.AnchorBottom = 1f;
		rect.MouseFilter = Control.MouseFilterEnum.Ignore;
		canvas.AddChild(rect);
		GetTree().Root.AddChild(canvas);

		// Fade in vers noir
		await FaireFondu(rect, 0f, 1f, DureeFondu);

		// Téléportation pendant le noir
		Vector3 cible = CiblePosition;
		if (CiblePath != null && !CiblePath.IsEmpty && HasNode(CiblePath))
		{
			var n = GetNode<Node3D>(CiblePath);
			cible = n.GlobalPosition;
		}
		if (joueur != null)
			joueur.TeleporterA(cible);

		GameState.Instance?.NotifierEntreeBT01();

		// Petit temps en noir complet
		await ToSignal(GetTree().CreateTimer(DureeNoirComplet), SceneTreeTimer.SignalName.Timeout);

		// Fade out
		await FaireFondu(rect, 1f, 0f, DureeFondu);

		canvas.QueueFree();

		// Réactive le joueur (la ref locale tient même si le joueur a quitté la zone via le TP)
		if (joueur != null)
			joueur.SetPhysicsProcess(true);
	}

	private async System.Threading.Tasks.Task FaireFondu(ColorRect rect, float depuis, float vers, float duree)
	{
		var tween = CreateTween();
		var couleurDepart = rect.Color;
		couleurDepart.A = depuis;
		rect.Color = couleurDepart;

		var couleurFin = rect.Color;
		couleurFin.A = vers;

		tween.TweenProperty(rect, "color", couleurFin, duree);
		await ToSignal(tween, Tween.SignalName.Finished);
	}
}
