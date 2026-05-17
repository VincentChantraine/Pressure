using Godot;

// Feedback visuel + sonore lorsqu'un bonus de temps est obtenu (levier, vanne...).
// S'abonne au signal TempsAjoute du ServoGameTimer et fait flasher "+Xs" au centre
// de l'écran avec un tween (apparition rapide, fondu lent) + un son court.
//
// Instancié et configuré par PartieManager — aucun ajout requis dans le .tscn.
// La classe se construit elle-même (Label + AudioStreamPlayer) pour rester
// totalement autonome.
public partial class BonusTempsHud : CanvasLayer
{
	// Son joué à chaque bonus. Optionnel : si null, seul le visuel est affiché.
	[Export] public AudioStream SonBonus;
	[Export] public float VolumeDb = -4f;

	// Couleur du flash. Vert vif par défaut pour matcher l'idée "gain".
	[Export] public Color Couleur = new Color(0.45f, 1f, 0.55f);

	// Durée totale de l'animation (apparition + fondu).
	[Export] public float Duree = 1.8f;

	// Taille de police du flash.
	[Export] public int TailleFont = 96;

	private Label label;
	private AudioStreamPlayer audio;

	public override void _Ready()
	{
		Layer = 60; // au-dessus du HUD de puzzle (BasePuzzleHud n'a pas de layer fixe).

		// Container plein écran pour positionner le label au centre haut.
		var ancre = new Control
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		};
		AddChild(ancre);

		label = new Label
		{
			Text = "",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			AnchorTop = 0.18f,
			AnchorBottom = 0.18f,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
			OffsetLeft = -240,
			OffsetRight = 240,
			OffsetTop = -60,
			OffsetBottom = 60,
			Modulate = new Color(1, 1, 1, 0),
		};
		label.AddThemeFontSizeOverride("font_size", TailleFont);
		label.AddThemeColorOverride("font_color", Couleur);
		label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
		label.AddThemeConstantOverride("outline_size", 8);
		ancre.AddChild(label);

		if (SonBonus == null)
			SonBonus = GD.Load<AudioStream>("res://Son/Success.mp3");

		audio = new AudioStreamPlayer { Stream = SonBonus, VolumeDb = VolumeDb };
		AddChild(audio);
	}

	/// <summary>
	/// Branche ce HUD sur le timer : à chaque bonus, le flash sera joué.
	/// </summary>
	public void Connecter(ServoGameTimer timer)
	{
		if (timer == null) return;
		timer.TempsAjoute += OnTempsAjoute;
	}

	private void OnTempsAjoute(float secondesEffectives, float nouveauElapsed)
	{
		if (secondesEffectives <= 0f) return;
		Afficher(secondesEffectives);
	}

	public void Afficher(float secondes)
	{
		if (label == null) return;

		label.Text = $"+{Mathf.RoundToInt(secondes)}s";
		// Reset transformation : si un précédent tween était en cours, on repart de zéro.
		label.Scale = new Vector2(0.6f, 0.6f);
		label.Modulate = new Color(1, 1, 1, 0);
		label.PivotOffset = label.Size * 0.5f;

		var tween = CreateTween();
		tween.SetParallel(true);
		// Pop d'apparition : scale + alpha en 0.15s.
		tween.TweenProperty(label, "scale", Vector2.One * 1.15f, 0.15f)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(label, "modulate:a", 1f, 0.15f);
		// Légère retombée + maintien.
		tween.Chain().TweenProperty(label, "scale", Vector2.One, 0.15f);
		// Fondu sortant.
		tween.Chain().TweenProperty(label, "modulate:a", 0f, Duree - 0.3f)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);

		if (audio != null && audio.Stream != null)
			audio.Play();
	}
}
