using Godot;

public partial class LightSwitch : Node3D
{
	[Export] public NodePath LumierePath;
	[Export] public NodePath ArduinoPath;

	[Export] public bool AllumeAuDemarrage = true;

	[Export] public NodePath SonOnPath;
	[Export] public NodePath SonOffPath;

	[Signal] public delegate void LumiereChangeeEventHandler(bool allumee);

	public bool EstAllumee => allumee;

	private Node3d arduino;
	private OmniLight3D lumiere;
	private AudioStreamPlayer3D playerOn;
	private AudioStreamPlayer3D playerOff;

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

	public override void _Process(double delta)
	{
		if (arduino == null) return;

		bool boutonActuel = arduino.isInteractPressed;
		bool frontMontant = boutonActuel && !boutonPrecedent;
		boutonPrecedent = boutonActuel;

		// Interaction pilotée par le raycast Survol3D : il faut regarder
		// l'interrupteur. La portée de 2 m remplace la zone d'Area3D.
		if (Survol3D.CibleCourante == this && frontMontant)
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
	}
}
