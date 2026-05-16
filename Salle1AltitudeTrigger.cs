using Godot;

// Trigger d'objectif pour la salle 1.
//
// Problème : dans cette salle, le joueur "vole" en levant la main (capteur ultrason)
// → c'est la caméra (LiftTarget de Player.cs) qui monte, pas le CharacterBody3D.
// Une Area3D classique avec BodyEntered ne se déclencherait donc jamais.
//
// Solution : on surveille à chaque frame la GlobalPosition d'un Node3D cible
// (typiquement la Camera3D du joueur ou le LiftTarget) et on vérifie si elle
// se trouve à l'intérieur d'un CollisionShape3D (BoxShape3D ou SphereShape3D)
// placé en l'air dans la salle. Dès que oui, la salle est validée une fois pour toutes.
public partial class Salle1AltitudeTrigger : Node3D
{
	[Export] public string SalleId = "salle_1";

	// Cible suivie : la Camera3D du joueur, ou le pivot LiftTarget de Player.cs.
	[Export] public NodePath CiblePath;

	// CollisionShape3D enfant qui définit la zone "en l'air" à atteindre.
	// Supporté : BoxShape3D et SphereShape3D.
	[Export] public NodePath FormePath;

	private Node3D cible;
	private CollisionShape3D forme;
	private bool valide = false;

	public override void _Ready()
	{
		if (CiblePath != null && !CiblePath.IsEmpty)
			cible = GetNodeOrNull<Node3D>(CiblePath);
		if (FormePath != null && !FormePath.IsEmpty)
			forme = GetNodeOrNull<CollisionShape3D>(FormePath);

		if (cible == null)
			GD.PushWarning($"[Salle1AltitudeTrigger] {Name} : cible introuvable via CiblePath.");
		if (forme == null || forme.Shape == null)
			GD.PushWarning($"[Salle1AltitudeTrigger] {Name} : forme/shape introuvable via FormePath.");
	}

	public override void _Process(double delta)
	{
		if (valide || cible == null || forme == null || forme.Shape == null) return;

		Vector3 local = forme.ToLocal(cible.GlobalPosition);
		bool dedans = false;

		if (forme.Shape is BoxShape3D box)
		{
			Vector3 demi = box.Size * 0.5f;
			dedans = Mathf.Abs(local.X) <= demi.X
				  && Mathf.Abs(local.Y) <= demi.Y
				  && Mathf.Abs(local.Z) <= demi.Z;
		}
		else if (forme.Shape is SphereShape3D sphere)
		{
			dedans = local.Length() <= sphere.Radius;
		}

		if (dedans)
		{
			valide = true;
			GameState.Instance?.MarquerSalleVisitee(SalleId);
			GD.Print($"[Salle1AltitudeTrigger] {SalleId} validée (cible dans la zone aérienne).");
		}
	}
}
