using Godot;

// HUD plein écran qui dessine une flèche pointant vers la salle non-validée
// la plus proche du joueur. Activable/désactivable via ParametresJeu.AfficherBoussole.
//
// Instancié dynamiquement par PartieManager dans un CanvasLayer.
// Lit GameState.AncresSalles (rempli par chaque puzzle dans _Ready) et
// GameState.SallesVisitees pour filtrer les salles déjà résolues.
public partial class BoussoleHud : Control
{
	// Couleur et taille de la flèche. Couleur alignée sur l'accent du menu
	// (cyan clair) pour rester cohérent avec le reste du HUD.
	[Export] public Color CouleurFleche = new Color(0.55f, 0.93f, 1f, 0.85f);
	[Export] public float TailleFleche = 22f;
	// Distance du centre de l'écran à laquelle la flèche flotte (rayon).
	[Export] public float RayonAffichage = 110f;
	// Si l'ancre est dans le champ de vision et à moins de cette distance (m),
	// on masque la flèche — inutile de désigner un objet déjà visible et proche.
	[Export] public float DistanceMasquage = 5f;

	private Camera3D camera;
	private Label labelDistance;

	public override void _Ready()
	{
		// Couvre tout l'écran, intercepte rien.
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		labelDistance = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		labelDistance.AddThemeColorOverride("font_color", CouleurFleche);
		labelDistance.AddThemeFontSizeOverride("font_size", 14);
		AddChild(labelDistance);
	}

	public override void _Process(double delta)
	{
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (!ParametresJeu.AfficherBoussole) { labelDistance.Visible = false; return; }
		if (GameState.Instance == null)      { labelDistance.Visible = false; return; }

		// Récupère la caméra active (peut changer : labyrinthe top-down, fin de partie).
		var viewport = GetViewport();
		camera = viewport?.GetCamera3D();
		if (camera == null) { labelDistance.Visible = false; return; }

		Node3D ancre = TrouverAncrePlusProche(camera.GlobalPosition);
		if (ancre == null) { labelDistance.Visible = false; return; }

		Vector3 vers = ancre.GlobalPosition - camera.GlobalPosition;
		float distance = vers.Length();

		// Test visibilité : si l'ancre est devant la caméra ET proche, on masque.
		Vector3 versNorm = distance > 0.001f ? vers / distance : Vector3.Forward;
		Vector3 forward = -camera.GlobalTransform.Basis.Z;
		float cosAngle = forward.Dot(versNorm);
		if (cosAngle > 0.85f && distance < DistanceMasquage)
		{
			labelDistance.Visible = false;
			return;
		}

		// Projection en espace caméra pour calculer l'angle de la flèche.
		// On utilise les axes X (droite) et Z (avant inverse) du repère caméra.
		Vector3 right = camera.GlobalTransform.Basis.X;
		float dotRight   = right.Dot(versNorm);
		float dotForward = forward.Dot(versNorm);

		// atan2(droite, avant) → angle 0 = devant, π/2 = à droite, -π/2 = à gauche, π = derrière.
		float angle = Mathf.Atan2(dotRight, dotForward);

		Vector2 centre = Size * 0.5f;
		Vector2 pointe = centre + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * RayonAffichage;

		DessinerFleche(centre, pointe, angle);

		// Étiquette distance, posée 24 px sous la pointe de la flèche.
		labelDistance.Visible = true;
		labelDistance.Text = $"{Mathf.RoundToInt(distance)} m";
		var size = labelDistance.Size;
		labelDistance.Position = pointe + new Vector2(-size.X * 0.5f, TailleFleche * 0.6f);
	}

	private Node3D TrouverAncrePlusProche(Vector3 depuis)
	{
		Node3D meilleur = null;
		float meilleurD2 = float.MaxValue;
		foreach (var (id, ancre) in GameState.Instance.AncresSalles)
		{
			if (ancre == null || !IsInstanceValid(ancre)) continue;
			if (GameState.Instance.SallesVisitees.Contains(id)) continue;
			float d2 = depuis.DistanceSquaredTo(ancre.GlobalPosition);
			if (d2 < meilleurD2)
			{
				meilleurD2 = d2;
				meilleur = ancre;
			}
		}
		return meilleur;
	}

	private void DessinerFleche(Vector2 centre, Vector2 pointe, float angle)
	{
		// Triangle pointant dans la direction de l'ancre.
		Vector2 dir = (pointe - centre).Normalized();
		Vector2 perp = new Vector2(-dir.Y, dir.X);

		Vector2 a = pointe;
		Vector2 b = pointe - dir * TailleFleche + perp * (TailleFleche * 0.5f);
		Vector2 c = pointe - dir * TailleFleche - perp * (TailleFleche * 0.5f);

		Vector2[] triangle = { a, b, c };
		Color[] couleurs = { CouleurFleche, CouleurFleche, CouleurFleche };
		DrawPolygon(triangle, couleurs);

		// Petit cercle au centre pour ancrer visuellement la boussole.
		DrawArc(centre, 4f, 0f, Mathf.Tau, 16, CouleurFleche, 1.5f);
	}
}
