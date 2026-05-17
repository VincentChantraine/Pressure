using Godot;

// À attacher à une Area3D placée au niveau des commandes du BT-01.
// Le joueur doit ENTRER dans la zone puis APPUYER sur le bouton joystick
// (Arduino BTN, fallback clavier Shift — même touche que les portes et l'interrupteur)
// pour déclencher la victoire.
public partial class VictoireCommandeTrigger : Area3D
{
	[Export] public NodePath ArduinoPath;

	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();
	[Signal] public delegate void VictoireDeclencheeEventHandler();

	private Node3d arduino;
	private bool joueurDansZone = false;
	private bool boutonPrecedent = false;
	private bool declenche = false;

	public bool JoueurDansZone => joueurDansZone;

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNodeOrNull<Node3d>(ArduinoPath);

		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;

		AddToGroup("interactif");
		SetMeta("libelle_interaction", "Reprendre les commandes");
	}

	private void OnBodyEntered(Node3D body)
	{
		if (declenche) return;
		if (body is Player)
		{
			joueurDansZone = true;
			// Capture l'état courant pour ne pas déclencher la victoire
			// si le bouton est déjà maintenu (ex: Shift tenu pour sprinter).
			boutonPrecedent = arduino?.isInteractPressed ?? false;
			EmitSignal(SignalName.JoueurEntreZone);
		}
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is Player)
		{
			joueurDansZone = false;
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
			GD.Print("[VictoireCommandeTrigger] Joueur a repris les commandes — VICTOIRE.");
			EmitSignal(SignalName.VictoireDeclenchee);
			GameState.Instance?.DeclencherVictoire();
		}
	}
}
