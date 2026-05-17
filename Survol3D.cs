using Godot;
using System.Collections.Generic;

// Service de "hover" 3D : raycast depuis la caméra active vers l'avant,
// détecte les nœuds du groupe "interactif" et applique un material_overlay
// cyan le temps que le joueur regarde la cible. Expose l'état via les
// propriétés statiques pour que SurvolHud puisse afficher un prompt.
//
// Instancié par PartieManager. Aucune dépendance sur le Player — utilise
// la caméra active du viewport (top-down du labyrinthe inclus).
//
// Pour qu'un objet soit "interactable", il doit :
//   - être ajouté au groupe "interactif" (AddToGroup) ;
//   - exposer (via SetMeta) la clé "libelle_interaction" (string).
public partial class Survol3D : Node
{
	[Export] public float PorteeMax = 4.0f;
	[Export] public uint MaskCollision = uint.MaxValue;
	[Export] public Color CouleurOverlay = new Color(0.55f, 0.93f, 1f, 1f);
	[Export] public float IntensiteEmission = 1.8f;

	// État courant lu par SurvolHud. Statique pour éviter de tirer une référence.
	public static Node3D CibleCourante { get; private set; }
	public static string LibelleCourant { get; private set; } = "";

	private Node3D cibleDerniere;
	private StandardMaterial3D overlayPartage;
	private readonly Dictionary<MeshInstance3D, Material> overlayPrecedents = new();

	public override void _Ready()
	{
		overlayPartage = new StandardMaterial3D
		{
			AlbedoColor = new Color(CouleurOverlay.R, CouleurOverlay.G, CouleurOverlay.B, 0.0f),
			EmissionEnabled = true,
			Emission = CouleurOverlay,
			EmissionEnergyMultiplier = IntensiteEmission,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			NoDepthTest = false,
			CullMode = BaseMaterial3D.CullModeEnum.Back,
		};
	}

	public override void _PhysicsProcess(double delta)
	{
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) { ChangerCible(null); return; }

		Vector3 from = camera.GlobalPosition;
		Vector3 to = from + (-camera.GlobalTransform.Basis.Z) * PorteeMax;

		var space = camera.GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to, MaskCollision);
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;
		var hit = space.IntersectRay(query);

		Node3D cible = null;
		if (hit.Count > 0 && hit.TryGetValue("collider", out var coll) && coll.Obj is Node nHit)
			cible = TrouverParentInteractif(nHit);

		ChangerCible(cible);
	}

	private void ChangerCible(Node3D nouvelle)
	{
		if (nouvelle == cibleDerniere) return;

		// Restaure les material_overlay précédents.
		foreach (var (mesh, mat) in overlayPrecedents)
		{
			if (IsInstanceValid(mesh)) mesh.MaterialOverlay = mat;
		}
		overlayPrecedents.Clear();

		cibleDerniere = nouvelle;
		CibleCourante = nouvelle;

		if (nouvelle == null)
		{
			LibelleCourant = "";
			return;
		}

		LibelleCourant = nouvelle.HasMeta("libelle_interaction")
			? (string)nouvelle.GetMeta("libelle_interaction")
			: "Interagir";

		// Applique overlay à tous les MeshInstance3D descendants.
		AppliquerOverlayRec(nouvelle);
	}

	private void AppliquerOverlayRec(Node node)
	{
		if (node is MeshInstance3D m)
		{
			overlayPrecedents[m] = m.MaterialOverlay;
			m.MaterialOverlay = overlayPartage;
		}
		foreach (var child in node.GetChildren())
			AppliquerOverlayRec(child);
	}

	private static Node3D TrouverParentInteractif(Node depart)
	{
		Node n = depart;
		while (n != null)
		{
			if (n.IsInGroup("interactif") && n is Node3D n3)
				return n3;
			n = n.GetParent();
		}
		return null;
	}
}
