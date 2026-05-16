using Godot;

// À attacher à une Area3D placée en haut des escaliers, derrière la porte finale.
// Quand le joueur entre dans la zone, il est téléporté vers le point cible
// (à placer à l'intérieur de l'asset BT-01).
public partial class FinPartieTrigger : Area3D
{
	[Export] public NodePath CiblePath;       // Marker3D / Node3D à l'intérieur du BT-01
	[Export] public Vector3 CiblePosition;    // Fallback si CiblePath n'est pas défini

	private bool declenche = false;

	public override void _Ready()
	{
		BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (declenche) return;
		if (body is Player player)
		{
			declenche = true;

			Vector3 cible = CiblePosition;
			if (CiblePath != null && !CiblePath.IsEmpty && HasNode(CiblePath))
			{
				var n = GetNode<Node3D>(CiblePath);
				cible = n.GlobalPosition;
			}

			player.TeleporterA(cible);
		}
	}
}
