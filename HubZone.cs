using Godot;

// Zone "hub" centrale — quand le joueur est dedans, la boussole s'affiche.
// Quand il en sort (= il est entré dans une salle), la boussole se masque
// pour ne pas spoiler la position des puzzles.
//
// Usage : ajouter une HubZone (Area3D) au .tscn englobant la pièce centrale,
// avec un CollisionShape3D enfant. Le compteur statique gère le cas multi-hubs.
// S'il n'y a aucune HubZone dans la scène, la boussole reste visible partout
// (fallback : pas régressif si l'utilisateur n'a pas encore configuré la zone).
public partial class HubZone : Area3D
{
	// Nombre de HubZone instanciées dans la scène courante.
	// > 0 + JoueursDedans == 0 → joueur hors hub → boussole masquée.
	// == 0 → pas de hub configuré → fallback "toujours visible".
	private static int ZonesInstanciees = 0;
	private static int JoueursDedans = 0;

	/// <summary>
	/// True si le joueur doit voir la boussole : soit aucune HubZone n'est
	/// configurée (fallback), soit il est actuellement dans une.
	/// </summary>
	public static bool JoueurDansHub =>
		ZonesInstanciees == 0 || JoueursDedans > 0;

	public override void _Ready()
	{
		ZonesInstanciees++;
		BodyEntered += OnBodyEntered;
		BodyExited  += OnBodyExited;
	}

	public override void _ExitTree()
	{
		ZonesInstanciees = Mathf.Max(0, ZonesInstanciees - 1);
		// Reset défensif en cas de désinstanciation de toutes les zones.
		if (ZonesInstanciees == 0) JoueursDedans = 0;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is Player) JoueursDedans++;
	}

	private void OnBodyExited(Node3D body)
	{
		if (body is Player) JoueursDedans = Mathf.Max(0, JoueursDedans - 1);
	}
}
