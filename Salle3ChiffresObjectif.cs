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
	}

	public override void _Process(double delta)
	{
		if (valide) return;

		foreach (var c in chiffres)
		{
			if (c.Visible) dejaVus.Add(c);
		}

		if (dejaVus.Count >= chiffres.Count && chiffres.Count > 0)
		{
			valide = true;
			GameState.Instance?.MarquerSalleVisitee(SalleId);
			GD.Print($"[Salle3ChiffresObjectif] {SalleId} validée (tous les chiffres révélés).");
		}
	}
}
