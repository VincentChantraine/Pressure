using Godot;

public partial class LightSwitch : Node3D
{
	[Export] public NodePath LumierePath;
	[Export] public NodePath ZonePath;
	[Export] public NodePath ArduinoPath;

	[Export] public NodePath SpriteEtatPath;
	[Export] public Texture2D TextureOn;
	[Export] public Texture2D TextureOff;

	[Export] public bool AllumeAuDemarrage = true;

	[Export] public NodePath SonOnPath;
	[Export] public NodePath SonOffPath;

	[Signal] public delegate void LumiereChangeeEventHandler(bool allumee);
	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();

	public bool EstAllumee => allumee;
	public bool JoueurDansZone => joueurDansZone;

	private Node3d arduino;
	private OmniLight3D lumiere;
	private Area3D zone;
	private Sprite3D spriteEtat;
	private AudioStreamPlayer3D playerOn;
	private AudioStreamPlayer3D playerOff;

	private bool joueurDansZone = false;
	private bool boutonPrecedent = false;
	private bool allumee = true;

	public override void _Ready()
	{
		// Arduino : via export, sinon en remontant l'arbre.
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNodeOrNull<Node3d>(ArduinoPath);
		if (arduino == null)
		{
			Node n = GetParent();
			while (n != null)
			{
				if (n is Node3d nd) { arduino = nd; break; }
				n = n.GetParent();
			}
		}
		if (arduino == null)
			GD.PrintErr($"[LightSwitch] {Name} : Node3d (Arduino) introuvable.");

		// Lumière : NodePath obligatoire.
		if (LumierePath != null && !LumierePath.IsEmpty)
			lumiere = GetNodeOrNull<OmniLight3D>(LumierePath);
		if (lumiere == null)
			GD.PrintErr($"[LightSwitch] {Name} : OmniLight3D introuvable via LumierePath.");

		// Zone : NodePath obligatoire.
		if (ZonePath != null && !ZonePath.IsEmpty)
			zone = GetNodeOrNull<Area3D>(ZonePath);
		if (zone == null)
		{
			GD.PrintErr($"[LightSwitch] {Name} : Area3D introuvable via ZonePath.");
		}
		else
		{
			zone.BodyEntered += OnBodyEntered;
			zone.BodyExited += OnBodyExited;
		}

		// Sprite3D d'état (optionnel).
		if (SpriteEtatPath != null && !SpriteEtatPath.IsEmpty)
			spriteEtat = GetNodeOrNull<Sprite3D>(SpriteEtatPath);

		// Players audio (NodePaths vers des AudioStreamPlayer3D placés dans la scène).
		if (SonOnPath != null && !SonOnPath.IsEmpty)
			playerOn = GetNodeOrNull<AudioStreamPlayer3D>(SonOnPath);
		if (SonOffPath != null && !SonOffPath.IsEmpty)
			playerOff = GetNodeOrNull<AudioStreamPlayer3D>(SonOffPath);

		// État initial.
		allumee = AllumeAuDemarrage;
		AppliquerEtat();

		AddToGroup("interactif");
		SetMeta("libelle_interaction", "Interrupteur");
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is Player)
		{
			joueurDansZone = true;
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
		if (arduino == null) return;

		bool boutonActuel = arduino.isInteractPressed;
		bool frontMontant = boutonActuel && !boutonPrecedent;
		boutonPrecedent = boutonActuel;

		if (joueurDansZone && frontMontant)
		{
			allumee = !allumee;
			AppliquerEtat();
			AudioStreamPlayer3D player = allumee ? playerOn : playerOff;
			if (player != null && player.Stream != null)
				player.Play();
			EmitSignal(SignalName.LumiereChangee, allumee);
		}
	}

	private void AppliquerEtat()
	{
		if (lumiere != null) lumiere.Visible = allumee;
		if (spriteEtat != null)
			spriteEtat.Texture = allumee ? TextureOn : TextureOff;
	}
}
