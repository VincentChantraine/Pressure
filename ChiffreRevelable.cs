using Godot;

// À attacher à chacun des chiffres (4, 3, 6, 8) qui doivent n'apparaître
// que quand la torche est à la fois ORIENTÉE dessus ET PUISSANTE (LDR couvert).
public partial class ChiffreRevelable : Node3D
{
	[Export] public NodePath TorchPath;

	// Seuil d'énergie de la torche en-dessous duquel le chiffre reste caché.
	// Avec TorchMaxEnergy=4.0 dans Player.cs, 2.0 = il faut couvrir au moins ~50% le LDR.
	[Export] public float EnergySeuil = 2.0f;

	// Marge (en degrés) ajoutée au SpotAngle pour tolérer les imprécisions.
	[Export] public float AngleMarginDeg = 0.0f;

	// Layers physiques à tester pour l'occlusion (typiquement : layer des murs).
	[Export(PropertyHint.Layers3DPhysics)] public uint OcclusionMask = 1;

	// Distance sous laquelle on considère que le raycast a "touché le chiffre lui-même"
	// plutôt qu'un mur. À augmenter si tes chiffres sont collés très près du mur
	// et que le raycast tape le mur juste derrière.
	[Export] public float ToleranceOcclusion = 0.3f;

	private SpotLight3D torch;

	public override void _Ready()
	{
		if (TorchPath != null && !TorchPath.IsEmpty)
			torch = GetNodeOrNull<SpotLight3D>(TorchPath);

		if (torch == null)
			GD.PushWarning($"[ChiffreRevelable] {Name} : torche non trouvée via TorchPath.");

		Visible = false;
	}

	public override void _Process(double delta)
	{
		Visible = EstEclaire();
	}

	private bool EstEclaire()
	{
		if (torch == null) return false;

		// 1) Torche assez puissante ? (LDR couvert)
		if (torch.LightEnergy < EnergySeuil) return false;

		Vector3 torchPos = torch.GlobalPosition;
		Vector3 torchFwd = -torch.GlobalTransform.Basis.Z; // SpotLight3D éclaire vers -Z local
		Vector3 toChiffre = GlobalPosition - torchPos;
		float dist = toChiffre.Length();

		// 2) Dans la portée ?
		if (dist > torch.SpotRange || dist < 0.001f) return false;

		// 3) Dans le cône ?
		Vector3 dir = toChiffre / dist;
		float cosAngle = torchFwd.Dot(dir);
		float halfAngleRad = Mathf.DegToRad(torch.SpotAngle + AngleMarginDeg);
		if (cosAngle < Mathf.Cos(halfAngleRad)) return false;

		// 4) Aucun mur entre la torche et le chiffre ?
		var space = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(torchPos, GlobalPosition, OcclusionMask);
		query.CollideWithAreas = false;
		var hit = space.IntersectRay(query);

		if (hit.Count > 0)
		{
			Vector3 hitPos = (Vector3)hit["position"];
			// Si le point de collision est loin du chiffre → c'est un mur qui bloque.
			if (hitPos.DistanceTo(GlobalPosition) > ToleranceOcclusion)
				return false;
		}

		return true;
	}
}
