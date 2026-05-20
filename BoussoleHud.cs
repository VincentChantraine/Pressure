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

	// Si le joueur est dans un rayon de RayonSalle mètres autour du centre
	// d'une salle (= la position du puzzle), il est considéré "dans la salle"
	// et la boussole se masque entièrement. Évite de pointer vers le puzzle
	// quand on y est déjà (= éviter de donner la solution).
	// À ajuster selon la taille moyenne d'une salle dans node_3d.tscn.
	// 18m = couvre largement une salle moyenne ; baisser si ça déborde
	// trop sur le hub, monter si la boussole reste visible une fois dedans.
	[Export] public float RayonSalle = 18f;

	// Ordre IMPOSÉ des salles : la boussole pointe vers la première non-validée
	// dans cette liste (et non la plus proche). Reflète le parcours scénarisé
	// (les prérequis SallePrerequise des portes imposent : 1 → 3 → 4 → 5 → 2 → 6).
	// Modifiable dans l'inspecteur si l'ordre éditorial change.
	[Export] public string[] OrdreSalles = new[]
	{
		"salle_1", "salle_3", "salle_4",
		"salle_5", "salle_2", "salle_6",
	};

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

		// Auto-masquée quand le joueur est dans une salle (= près du centre d'un
		// puzzle), pour ne pas montrer une flèche vers la salle suivante alors
		// qu'on est encore en train de résoudre celle-ci.
		if (JoueurDansUneSalle(camera.GlobalPosition)) { labelDistance.Visible = false; return; }

		Node3D ancre = TrouverProchaineAncre();
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

	private Node3D TrouverProchaineAncre() => TrouverProchaineAncreStatique(OrdreSalles);

	private bool JoueurDansUneSalle(Vector3 posJoueur)
	{
		float r2 = RayonSalle * RayonSalle;
		foreach (var (id, centre) in GameState.Instance.CentresSalles)
		{
			// salle_1 = hub d'exploration (Salle1AltitudeTrigger enregistré au
			// nœud du déclencheur, souvent près de l'origine). Si on l'incluait,
			// la boussole serait masquée en permanence dans le hub.
			if (id == "salle_1") continue;
			if (centre == null || !IsInstanceValid(centre)) continue;
			if (posJoueur.DistanceSquaredTo(centre.GlobalPosition) <= r2)
				return true;
		}
		return false;
	}

	// Logique partagée — utilisée aussi par MiniMapHud pour sa flèche off-screen.
	public static Node3D TrouverProchaineAncreStatique(string[] ordreSalles)
	{
		var gs = GameState.Instance;
		if (gs == null) return null;

		// Phase 0 — Badge 1 pas encore ramassé.
		if (!gs.BadgeRamasse1 && gs.BadgeCible != null && GodotObject.IsInstanceValid(gs.BadgeCible))
			return gs.BadgeCible;

		// Phase 1 — salles non-validées, dans l'ordre scénarisé.
		if (gs.SallesVisitees.Count < 6)
		{
			foreach (var id in ordreSalles)
			{
				if (gs.SallesVisitees.Contains(id)) continue;
				if (!gs.AncresSalles.TryGetValue(id, out var ancre)) continue;
				if (ancre == null || !GodotObject.IsInstanceValid(ancre)) continue;
				return ancre;
			}
			return null;
		}

		// Phase 2a — Badge 2 pas encore ramassé.
		if (!gs.BadgeRamasse2 && gs.BadgeCible != null && GodotObject.IsInstanceValid(gs.BadgeCible))
			return gs.BadgeCible;

		// Phase 2b — vers la porte de sortie.
		if (!gs.PorteSortieFranchie
			&& gs.AncresSalles.TryGetValue("exit_door", out var porteSortie)
			&& porteSortie != null && GodotObject.IsInstanceValid(porteSortie))
		{
			return porteSortie;
		}

		// Phase 3 — vers la PorteRonde (sas final).
		if (!gs.SortieFinaleAtteinte
			&& gs.AncresSalles.TryGetValue("exit_teleport", out var porteRonde)
			&& porteRonde != null && GodotObject.IsInstanceValid(porteRonde))
		{
			return porteRonde;
		}

		return null;
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
