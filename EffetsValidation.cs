using Godot;

// HUD plein écran qui joue un flash blanc bref à chaque salle validée.
// Instancié dynamiquement par PartieManager et placé sur un CanvasLayer
// élevé pour passer par-dessus le HUD de progression.
// Le screen shake est délégué au Player.Trembler — ce nœud ne gère que le visuel 2D.
public partial class EffetsValidation : Control
{
	// Intensité initiale du flash (0..1) puis fade out en `DureeFlash` secondes.
	[Export] public float OpaciteInitiale = 0.55f;
	[Export] public float DureeFlash = 0.35f;

	private ColorRect rect;
	private Tween tweenCourant;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		rect = new ColorRect
		{
			Color = new Color(1f, 1f, 1f, 0f),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		rect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(rect);
	}

	/// <summary>
	/// Déclenche un flash blanc : opacité passe de OpaciteInitiale à 0
	/// en DureeFlash secondes (ease out).
	/// </summary>
	public void Flasher()
	{
		if (rect == null) return;

		// Coupe tween en cours pour pouvoir relancer un flash propre.
		tweenCourant?.Kill();

		rect.Color = new Color(1f, 1f, 1f, OpaciteInitiale);
		tweenCourant = CreateTween();
		tweenCourant.TweenProperty(rect, "color:a", 0f, DureeFlash)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
	}
}
