using Godot;
using System.Collections.Generic;

// Objectif de la salle 3 : tous les ChiffreRevelable listés doivent avoir été
// "illuminés" (rendus Visible par la torche) au moins une fois.
// On surveille leur propriété Visible à chaque frame ; dès qu'un chiffre est
// vu pour la première fois, on l'enregistre. Quand tous ont été vus → la salle
// est validée dans GameState.
public partial class Salle3ChiffresObjectif : Node
{
	[Export] public string SalleId = "salle_3";

	// Liste des Node3D ChiffreRevelable à surveiller.
	[Export] public NodePath[] ChiffresPaths = new NodePath[0];

	// Visée précise pour compter un chiffre comme "vu". Le chiffre doit être
	// visible (révélé par la torche) ET le joueur doit le regarder dans ce
	// cône resserré centré sur la caméra. Empêche qu'un balayage rapide du
	// faisceau de torche valide d'un coup tous les chiffres en périphérie.
	// Mettre à 0 pour désactiver le check (ancien comportement : visible = vu).
	[Export(PropertyHint.Range, "0,45,0.5")] public float AngleRegardMaxDeg = 6.0f;

	private readonly List<Node3D> chiffres = new List<Node3D>();
	private readonly HashSet<Node3D> dejaVus = new HashSet<Node3D>();
	private bool valide = false;

	public override void _Ready()
	{
		foreach (var path in ChiffresPaths)
		{
			if (path == null || path.IsEmpty) continue;
			var n = GetNodeOrNull<Node3D>(path);
			if (n != null) chiffres.Add(n);
			else GD.PushWarning($"[Salle3ChiffresObjectif] Chiffre introuvable : {path}");
		}

		if (chiffres.Count == 0)
			GD.PushWarning("[Salle3ChiffresObjectif] Aucun chiffre référencé — l'objectif ne pourra jamais valider.");
		else
			// Ce nœud est un Node (pas un Node3D) ; on utilise le premier chiffre
			// comme centre boussole (proximité-only) — il marque la zone de la salle.
			GameState.Instance?.RegistrerCentreSalle(SalleId, chiffres[0]);
	}

	public override void _Process(double delta)
	{
		if (valide) return;

		var cam = AngleRegardMaxDeg > 0f ? GetViewport()?.GetCamera3D() : null;
		float cosMax = Mathf.Cos(Mathf.DegToRad(AngleRegardMaxDeg));

		foreach (var c in chiffres)
		{
			if (!c.Visible) continue;
			if (cam == null) { dejaVus.Add(c); continue; }

			// Cône caméra → chiffre : le crosshair doit être sur le chiffre.
			Vector3 to = c.GlobalPosition - cam.GlobalPosition;
			float d = to.Length();
			if (d < 0.001f) { dejaVus.Add(c); continue; }
			Vector3 dir = to / d;
			Vector3 fwd = -cam.GlobalTransform.Basis.Z;
			if (fwd.Dot(dir) >= cosMax)
				dejaVus.Add(c);
		}

		if (dejaVus.Count >= chiffres.Count && chiffres.Count > 0)
		{
			valide = true;
			GameState.Instance?.MarquerSalleVisitee(SalleId);
		}
	}
}
