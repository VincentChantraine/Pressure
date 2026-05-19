using Godot;

// Trigger d'objectif pour la salle 1.
//
// Problème : dans cette salle, le joueur "vole" en levant la main (capteur ultrason)
// → c'est la caméra (LiftTarget de Player.cs) qui monte, pas le CharacterBody3D.
// Une Area3D classique avec BodyEntered ne se déclencherait donc jamais.
//
// Deux modes de validation (n'importe lequel déclenche) :
//  1. Altitude : la cible (caméra/LiftTarget) entre dans une zone aérienne.
//  2. Regard : le joueur vise un Node3D (typiquement Label3DSalle1) à
//     ≤ PorteeRegardMax mètres, dans un cône (AngleRegardMaxDeg), sans mur
//     entre les deux. Utile maintenant qu'on a la visée souris : pas besoin
//     de s'envoler, juste regarder le texte en hauteur depuis le sol.
public partial class Salle1AltitudeTrigger : Node3D
{
	[Export] public string SalleId = "salle_1";

	// Cible suivie : la Camera3D du joueur, ou le pivot LiftTarget de Player.cs.
	[Export] public NodePath CiblePath;

	// CollisionShape3D enfant qui définit la zone "en l'air" à atteindre.
	// Supporté : BoxShape3D et SphereShape3D.
	[Export] public NodePath FormePath;
	// Active/désactive le mode altitude. Par défaut off : on s'appuie sur le
	// regard maintenu (cf. CibleRegardPath) depuis qu'on a la visée souris,
	// l'envol n'est plus nécessaire pour valider.
	[Export] public bool ModeAltitudeActif = false;

	// Mode regard : Node3D à viser (ex. Label3DSalle1). Laisser vide pour désactiver.
	[Export] public NodePath CibleRegardPath;
	[Export] public float PorteeRegardMax = 8.0f;
	[Export(PropertyHint.Range, "1,45,1")] public float AngleRegardMaxDeg = 10.0f;
	// Temps minimum (sec) à maintenir le regard sur la cible pour valider :
	// évite que la salle se valide par un simple coup d'œil au plafond.
	[Export] public float DureeRegardMin = 0.25f;
	// Mask d'occlusion. Volontairement à 0 (désactivé) : le texte est placé
	// au-dessus du plafond — c'est l'astuce de la salle. Avec mask=1, le ray
	// caméra→label tape toujours le plafond/rustines, et impossible de valider
	// sans voler à travers un trou. Distance + angle suffisent à empêcher la
	// validation depuis une autre pièce.
	[Export(PropertyHint.Layers3DPhysics)] public uint MaskOcclusion = 0;

	private Node3D cible;
	private CollisionShape3D forme;
	private Node3D cibleRegard;
	private bool valide = false;
	private float tempsRegardAccumule = 0f;

	public override void _Ready()
	{
		if (CiblePath != null && !CiblePath.IsEmpty)
			cible = GetNodeOrNull<Node3D>(CiblePath);
		if (FormePath != null && !FormePath.IsEmpty)
			forme = GetNodeOrNull<CollisionShape3D>(FormePath);
		if (CibleRegardPath != null && !CibleRegardPath.IsEmpty)
			cibleRegard = GetNodeOrNull<Node3D>(CibleRegardPath);

		if (cible == null)
			GD.PushWarning($"[Salle1AltitudeTrigger] {Name} : cible introuvable via CiblePath.");
		if (forme == null || forme.Shape == null)
			GD.PushWarning($"[Salle1AltitudeTrigger] {Name} : forme/shape introuvable via FormePath.");

		// Centre boussole (proximité-only) : la zone aérienne si dispo,
		// sinon le trigger lui-même. JAMAIS cible de flèche — salle_1 n'a
		// pas de porte associée, le joueur explore le hub pour la trouver.
		Node3D centre = forme != null ? (Node3D)forme : this;
		GameState.Instance?.RegistrerCentreSalle(SalleId, centre);
	}

	public override void _Process(double delta)
	{
		if (valide) return;

		// Mode altitude désactivé : on ne valide plus que par le regard maintenu.
		// (Conservé : CibleDansZone() en bas si on veut le ressusciter un jour.)

		// Maintien du regard : il faut viser la cible pendant DureeRegardMin
		// d'affilée pour valider. Si le joueur détourne les yeux, le compteur
		// se vide (pas instantanément pour tolérer un micro-flottement).
		if (RegardSurCible())
		{
			tempsRegardAccumule += (float)delta;
			if (tempsRegardAccumule >= DureeRegardMin)
				Valider();
		}
		else
		{
			tempsRegardAccumule = Mathf.Max(0f, tempsRegardAccumule - (float)delta * 2f);
		}
	}

	private bool CibleDansZone()
	{
		if (cible == null || forme == null || forme.Shape == null) return false;

		Vector3 local = forme.ToLocal(cible.GlobalPosition);

		if (forme.Shape is BoxShape3D box)
		{
			Vector3 demi = box.Size * 0.5f;
			return Mathf.Abs(local.X) <= demi.X
				&& Mathf.Abs(local.Y) <= demi.Y
				&& Mathf.Abs(local.Z) <= demi.Z;
		}
		if (forme.Shape is SphereShape3D sphere)
			return local.Length() <= sphere.Radius;

		return false;
	}

	private bool RegardSurCible()
	{
		if (cibleRegard == null) return false;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return false;

		Vector3 camPos = cam.GlobalPosition;
		Vector3 toCible = cibleRegard.GlobalPosition - camPos;
		float dist = toCible.Length();
		if (dist < 0.001f || dist > PorteeRegardMax) return false;

		// Cône autour du forward de la caméra. Label3D n'a pas de collider donc
		// on ne peut pas tester avec un raycast classique → angle + occlusion.
		Vector3 dir = toCible / dist;
		Vector3 fwd = -cam.GlobalTransform.Basis.Z;
		if (fwd.Dot(dir) < Mathf.Cos(Mathf.DegToRad(AngleRegardMaxDeg))) return false;

		// Occlusion : si MaskOcclusion=0, on saute le test (cas de la salle 1
		// où le label est derrière le plafond par design).
		if (MaskOcclusion != 0)
		{
			var space = cam.GetWorld3D().DirectSpaceState;
			var q = PhysicsRayQueryParameters3D.Create(camPos, cibleRegard.GlobalPosition, MaskOcclusion);
			q.CollideWithAreas = false;
			if (space.IntersectRay(q).Count > 0) return false;
		}
		return true;
	}

	private void Valider()
	{
		valide = true;
		GameState.Instance?.MarquerSalleVisitee(SalleId);
	}
}
