using Godot;

// À attacher à un CanvasLayer, UN par interrupteur.
// Config attendue dans la SceneTree :
//   - 2 TextureRect enfants : un avec l'image ON, un avec l'image OFF
//     (positionnées et texturées dans l'éditeur)
// Comportement : caché par défaut. Quand l'état change (on↔off),
// affiche brièvement l'icône puis la fait disparaître en fondu.
public partial class LightSwitchHud : CanvasLayer
{
	[Export] public NodePath LightSwitchPath;
	[Export] public NodePath TextureRectOnPath;
	[Export] public NodePath TextureRectOffPath;

	// Durée totale d'affichage à chaque changement d'état (secondes).
	[Export] public float DureeAffichage = 2.0f;

	// Durée du fondu de sortie, à la fin (secondes). Doit être <= DureeAffichage.
	[Export] public float DureeFadeOut = 0.8f;

	private LightSwitch lightSwitch;
	private TextureRect rectOn;
	private TextureRect rectOff;
	private TextureRect rectActuel;

	private float timerAffichage = 0f;

	public override void _Ready()
	{
		if (LightSwitchPath != null && !LightSwitchPath.IsEmpty)
			lightSwitch = GetNodeOrNull<LightSwitch>(LightSwitchPath);

		if (TextureRectOnPath != null && !TextureRectOnPath.IsEmpty)
			rectOn = GetNodeOrNull<TextureRect>(TextureRectOnPath);
		if (TextureRectOffPath != null && !TextureRectOffPath.IsEmpty)
			rectOff = GetNodeOrNull<TextureRect>(TextureRectOffPath);

		if (rectOn == null) GD.PrintErr($"[LightSwitchHud] {Name} : TextureRect ON introuvable.");
		if (rectOff == null) GD.PrintErr($"[LightSwitchHud] {Name} : TextureRect OFF introuvable.");

		if (lightSwitch != null)
			lightSwitch.LumiereChangee += OnLumiereChangee;
		else
			GD.PrintErr($"[LightSwitchHud] {Name} : LightSwitch introuvable via LightSwitchPath.");

		// Caché au démarrage.
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (timerAffichage <= 0f) return;

		timerAffichage -= (float)delta;

		// Fondu de sortie sur les DureeFadeOut dernières secondes.
		if (rectActuel != null && DureeFadeOut > 0f && timerAffichage < DureeFadeOut)
		{
			float alpha = Mathf.Clamp(timerAffichage / DureeFadeOut, 0f, 1f);
			Color c = rectActuel.Modulate;
			c.A = alpha;
			rectActuel.Modulate = c;
		}

		if (timerAffichage <= 0f)
			Visible = false;
	}

	private void OnLumiereChangee(bool allumee)
	{
		if (rectOn != null) rectOn.Visible = allumee;
		if (rectOff != null) rectOff.Visible = !allumee;

		rectActuel = allumee ? rectOn : rectOff;

		// Reset alpha au cas où on relance pendant un fondu.
		if (rectActuel != null)
		{
			Color c = rectActuel.Modulate;
			c.A = 1f;
			rectActuel.Modulate = c;
		}

		Visible = true;
		timerAffichage = DureeAffichage;
	}
}
