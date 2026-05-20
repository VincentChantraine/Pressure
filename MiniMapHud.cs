using Godot;
using System.Collections.Generic;

// Mini-map en bas à droite : SubViewport rendant une vue top-down orthographique
// centrée sur le joueur, qui tourne avec son orientation.
//
// Deux modes auto-bascules selon la hauteur du joueur :
//  - "carte" (Y < SeuilYBt01) : plafonds + BT-01 masqués, le reste visible
//  - "bt01"  (Y ≥ SeuilYBt01) : seul le BT-01 visible
//
// Marqueurs en monde 3D sur le calque dédié `CalqueMarqueurs` (vu uniquement par
// la caméra minimap, exclu de la caméra principale) :
//  - Portes (toujours visibles, couleur selon état)
//  - Puzzles (révélés par proximité — pas de spoil au début)
//  - Badge actif (depuis GameState.BadgeCible)
//
// Overlays UI : compas N (rotation selon yaw), flèche off-screen vers la cible
// courante quand elle sort du cadre, halo sous la flèche joueur.
//
// Toggle visibilité : touche M.
public partial class MiniMapHud : CanvasLayer
{
	[Export] public NodePath PlayerPath;
	[Export] public float HauteurCamera = 40.0f;
	[Export] public float TailleOrtho = 38.0f;
	[Export] public float TailleOrthoBt01 = 12.0f;
	// Inclinaison de la caméra par rapport au top-down strict (en degrés).
	// 0 = pure top-down (murs invisibles), 15-20° = vue 3/4 qui montre le côté
	// des murs sans perdre la lisibilité top-down.
	[Export] public float InclinaisonDeg = 18.0f;
	[Export] public Vector2I TaillePixels = new Vector2I(280, 280);
	[Export] public int MargeBord = 16;
	[Export] public Key ToucheToggle = Key.M;

	[Export] public float LuminositeAmbiante = 2.5f;
	[Export] public Color CouleurAmbiante = new Color(1f, 1f, 1f, 1f);

	[Export] public int CalquePlafond = 20;
	[Export] public int CalqueBt01 = 19;
	[Export] public int CalqueMarqueurs = 18;

	[Export] public float SeuilYBt01 = 10.0f;
	[Export] public float DistanceRevelationPuzzle = 5.0f;

	// Motifs (Contains, insensible à la casse) classés comme "plafond" → masqués
	// de la minimap. Ajoute ici les noms si certains plafonds manquent.
	[Export] public string[] MotifsPlafond = new string[] { "Plafond" };

	// Ordre des salles pour la flèche off-screen — doit refléter OrdreSalles du
	// BoussoleHud. La logique elle-même est dans BoussoleHud.TrouverProchaineAncreStatique.
	[Export] public string[] OrdreSalles = new[]
	{
		"salle_1", "salle_3", "salle_4",
		"salle_5", "salle_2", "salle_6",
	};
	[Export] public float TailleMarqueur = 1.4f;
	[Export] public float TailleMarqueurBadge = 1.9f;

	private Node3D _joueur;
	private Camera3D _camPrincipale;
	private SubViewport _viewport;
	private Camera3D _camMini;
	private Control _conteneur;
	private TextureRect _fleche;
	private TextureRect _flecheHalo;
	private TextureRect _flecheObjectif;
	private Label _labelN;
	private bool _dansBt01 = false;

	// Conteneur des markers : Node3D enfant direct de la scène, sans scale ni
	// visibilité héritée → contourne le scale 7 du Coffre et le visible=false du Lever.
	private Node3D _marqueursParent;

	private readonly List<Marqueur> _marqueurs = new();
	private static ImageTexture _texturePastille;
	private static ImageTexture _textureFleche;

	private enum TypeMarqueur { Porte, Puzzle, Badge }

	private class Marqueur
	{
		public Node3D Cible;
		public MeshInstance3D Mesh;
		public StandardMaterial3D Mat;
		public TypeMarqueur Type;
		public bool Decouvert;
	}

	public override void _Ready()
	{
		_joueur = GetNodeOrNull<Node3D>(PlayerPath);
		if (_joueur == null)
		{
			_joueur = GetTree().Root.FindChild("Player", true, false) as Node3D;
		}
		if (_joueur != null)
		{
			_camPrincipale = _joueur.GetNodeOrNull<Camera3D>("Camera3D");
		}

		// Exclure le calque des marqueurs ET la lumière directionnelle dédiée
		// minimap de TOUTES les caméras de la scène (principale, labyrinthe...).
		// Sinon ces caméras "voient" la lumière minimap → sur-éclairage parasite.
		uint bitMarqueursMask = ~(1u << (CalqueMarqueurs - 1));
		ExclureCalqueDeToutesCameras(GetTree().CurrentScene ?? GetTree().Root, bitMarqueursMask);

		ConstruireUi();
		ClassifierEtAppliquerCalques();
		MettreAJourModeCamera();

		Layer = 5;

		// Différé : la scène est encore en cours de _ready au moment où nous y
		// sommes ; AddChild sur sa racine déclenche "Parent node is busy".
		CallDeferred(MethodName.CreerMarqueursMonde);
	}

	// =========================================================================
	// UI
	// =========================================================================
	private void ConstruireUi()
	{
		_conteneur = new Control();
		_conteneur.Name = "MiniMapConteneur";
		_conteneur.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_conteneur.Size = TaillePixels;
		_conteneur.Position = new Vector2(-TaillePixels.X - MargeBord, -TaillePixels.Y - MargeBord);
		_conteneur.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_conteneur);

		var panneau = new Panel();
		panneau.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		panneau.MouseFilter = Control.MouseFilterEnum.Ignore;
		var styleFond = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.55f),
			BorderColor = new Color(0.85f, 0.85f, 0.9f, 0.9f),
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6,
		};
		styleFond.SetBorderWidthAll(2);
		panneau.AddThemeStyleboxOverride("panel", styleFond);
		_conteneur.AddChild(panneau);

		var subContainer = new SubViewportContainer();
		subContainer.Stretch = true;
		subContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		subContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
		_conteneur.AddChild(subContainer);

		_viewport = new SubViewport();
		_viewport.Size = TaillePixels;
		_viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_viewport.TransparentBg = false;
		_viewport.OwnWorld3D = false;
		_viewport.World3D = GetTree().Root.World3D;
		subContainer.AddChild(_viewport);

		_camMini = new Camera3D();
		_camMini.Projection = Camera3D.ProjectionType.Orthogonal;
		_camMini.Size = TailleOrtho;
		_camMini.Near = 0.1f;
		_camMini.Far = HauteurCamera * 2.0f;
		_camMini.Environment = ConstruireEnvironnementMinimap();
		_viewport.AddChild(_camMini);
		_camMini.Current = true;

		// Lumière directionnelle dédiée à la minimap : son calque (Layers) est
		// uniquement le bit marqueurs, donc seule une caméra dont le cull_mask
		// inclut ce bit (= la cam minimap) en rend la contribution. La caméra
		// principale, qui exclut ce bit, n'est PAS affectée → pas de pollution
		// de l'éclairage du jeu. Donne du relief aux murs sur la minimap.
		var lumDir = new DirectionalLight3D();
		lumDir.RotationDegrees = new Vector3(-55f, 35f, 0f);
		lumDir.LightEnergy = 1.4f;
		lumDir.LightColor = new Color(1f, 0.97f, 0.9f, 1f);
		lumDir.ShadowEnabled = false;
		lumDir.Layers = 1u << (CalqueMarqueurs - 1);
		_viewport.AddChild(lumDir);

		// Halo sombre derrière la flèche joueur pour la rendre lisible sur fond clair.
		var tailleHalo = new Vector2(28, 28);
		_flecheHalo = new TextureRect();
		_flecheHalo.Texture = ConstruireDisqueTexture(28, new Color(0f, 0f, 0f, 0.55f), 1.5f, new Color(0f, 0f, 0f, 0f));
		_flecheHalo.CustomMinimumSize = tailleHalo;
		_flecheHalo.Size = tailleHalo;
		_flecheHalo.Position = new Vector2((TaillePixels.X - tailleHalo.X) / 2f, (TaillePixels.Y - tailleHalo.Y) / 2f);
		_flecheHalo.MouseFilter = Control.MouseFilterEnum.Ignore;
		_conteneur.AddChild(_flecheHalo);

		var tailleFleche = new Vector2(18, 18);
		_fleche = new TextureRect();
		_fleche.Texture = ConstruireFlecheTexture();
		_fleche.CustomMinimumSize = tailleFleche;
		_fleche.Size = tailleFleche;
		_fleche.Position = new Vector2((TaillePixels.X - tailleFleche.X) / 2f, (TaillePixels.Y - tailleFleche.Y) / 2f);
		_fleche.PivotOffset = tailleFleche / 2f;
		_fleche.MouseFilter = Control.MouseFilterEnum.Ignore;
		_conteneur.AddChild(_fleche);

		// Indicateur N (cardinal Nord) — tourne avec la rotation de la carte.
		_labelN = new Label();
		_labelN.Text = "N";
		_labelN.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.3f, 1f));
		_labelN.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 1f));
		_labelN.AddThemeConstantOverride("outline_size", 4);
		_labelN.AddThemeFontSizeOverride("font_size", 14);
		_labelN.CustomMinimumSize = new Vector2(16, 16);
		_labelN.Size = new Vector2(16, 16);
		_labelN.PivotOffset = new Vector2(8, 8);
		_labelN.HorizontalAlignment = HorizontalAlignment.Center;
		_labelN.VerticalAlignment = VerticalAlignment.Center;
		_labelN.MouseFilter = Control.MouseFilterEnum.Ignore;
		_conteneur.AddChild(_labelN);

		// Flèche d'objectif "off-screen" : visible quand BadgeCible existe et est
		// hors champ de la minimap.
		_flecheObjectif = new TextureRect();
		_flecheObjectif.Texture = ConstruireFlecheTexture(24, new Color(0.4f, 1f, 0.4f, 1f));
		_flecheObjectif.CustomMinimumSize = new Vector2(24, 24);
		_flecheObjectif.Size = new Vector2(24, 24);
		_flecheObjectif.PivotOffset = new Vector2(12, 12);
		_flecheObjectif.MouseFilter = Control.MouseFilterEnum.Ignore;
		_flecheObjectif.Visible = false;
		_conteneur.AddChild(_flecheObjectif);
	}

	private Godot.Environment ConstruireEnvironnementMinimap()
	{
		var env = new Godot.Environment();
		env.BackgroundMode = Godot.Environment.BGMode.Color;
		env.BackgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = CouleurAmbiante;
		env.AmbientLightEnergy = LuminositeAmbiante;
		env.AmbientLightSkyContribution = 0f;
		env.TonemapMode = Godot.Environment.ToneMapper.Linear;
		env.FogEnabled = false;
		env.GlowEnabled = false;
		env.SsrEnabled = false;
		env.SdfgiEnabled = false;

		// SSAO : assombrit les angles concaves (sol/mur, recoins) → contours des
		// pièces nettement plus lisibles sur une vue top-down.
		env.SsaoEnabled = true;
		env.SsaoRadius = 1.5f;
		env.SsaoIntensity = 4.0f;
		env.SsaoPower = 1.5f;
		env.SsaoDetail = 0.5f;

		// Post-process contrast/brightness pour démarquer les volumes.
		env.AdjustmentEnabled = true;
		env.AdjustmentBrightness = 1.05f;
		env.AdjustmentContrast = 1.35f;
		env.AdjustmentSaturation = 1.1f;
		return env;
	}

	private static ImageTexture ConstruireFlecheTexture(int taille = 18, Color? remplissage = null)
	{
		// Cyan par défaut : même teinte que le réticule / l'overlay Survol3D.
		var couleur = remplissage ?? new Color(0.55f, 0.93f, 1f, 1f);
		var contour = new Color(0f, 0f, 0f, 1f);
		var img = Image.CreateEmpty(taille, taille, false, Image.Format.Rgba8);
		img.Fill(new Color(0, 0, 0, 0));
		int cx = taille / 2;
		for (int y = 0; y < taille; y++)
		{
			int demi = y / 2;
			for (int x = cx - demi; x <= cx + demi; x++)
			{
				if (x >= 0 && x < taille) img.SetPixel(x, y, couleur);
			}
			if (cx - demi - 1 >= 0) img.SetPixel(cx - demi - 1, y, contour);
			if (cx + demi + 1 < taille) img.SetPixel(cx + demi + 1, y, contour);
		}
		// Cache la version par défaut (18 / jaune) pour réutilisation.
		if (taille == 18 && !remplissage.HasValue) _textureFleche = ImageTexture.CreateFromImage(img);
		return ImageTexture.CreateFromImage(img);
	}

	private static ImageTexture ConstruireDisqueTexture(int taille, Color interieur, float epaisseurContour, Color contour)
	{
		var img = Image.CreateEmpty(taille, taille, false, Image.Format.Rgba8);
		img.Fill(new Color(0, 0, 0, 0));
		float cx = (taille - 1) / 2f;
		float rExt = taille / 2f - 0.5f;
		float rInt = rExt - epaisseurContour;
		for (int y = 0; y < taille; y++)
		{
			for (int x = 0; x < taille; x++)
			{
				float dx = x - cx;
				float dy = y - cx;
				float d = Mathf.Sqrt(dx * dx + dy * dy);
				if (d <= rInt) img.SetPixel(x, y, interieur);
				else if (d <= rExt) img.SetPixel(x, y, contour);
			}
		}
		return ImageTexture.CreateFromImage(img);
	}

	private static ImageTexture ObtenirTexturePastille()
	{
		if (_texturePastille != null) return _texturePastille;
		// Disque plein avec centre semi-transparent et contour opaque. Le centre
		// laisse voir l'objet en-dessous, le contour reste bien lisible même
		// quand le marker est petit à l'écran.
		int taille = 32;
		var img = Image.CreateEmpty(taille, taille, false, Image.Format.Rgba8);
		img.Fill(new Color(0, 0, 0, 0));
		float cx = (taille - 1) / 2f;
		float rExt = taille / 2f - 0.5f;
		float rBordExt = rExt - 1.0f;
		float rBordInt = rBordExt - 4.5f;
		for (int y = 0; y < taille; y++)
		{
			for (int x = 0; x < taille; x++)
			{
				float dx = x - cx;
				float dy = y - cx;
				float d = Mathf.Sqrt(dx * dx + dy * dy);
				if (d > rExt) continue;
				if (d > rBordExt) img.SetPixel(x, y, new Color(0, 0, 0, 1));         // liseré noir
				else if (d > rBordInt) img.SetPixel(x, y, new Color(1, 1, 1, 1));    // bandeau de couleur (teinté via Albedo)
				else img.SetPixel(x, y, new Color(1, 1, 1, 0.45f));                  // centre semi-transparent
			}
		}
		_texturePastille = ImageTexture.CreateFromImage(img);
		return _texturePastille;
	}

	// =========================================================================
	// Classification des calques (plafonds / BT-01 / autres)
	// =========================================================================
	private void ClassifierEtAppliquerCalques()
	{
		var racine = GetTree().CurrentScene ?? GetTree().Root;
		uint bitPlafond = 1u << (CalquePlafond - 1);
		uint bitBt01 = 1u << (CalqueBt01 - 1);
		ParcourirEtAffecter(racine, false, false, bitPlafond, bitBt01);
	}

	private void ParcourirEtAffecter(Node n, bool dansBt01, bool dansPlafond, uint bitPlafond, uint bitBt01)
	{
		if (n == null) return;
		string nom = n.Name.ToString();
		bool plafondIci = dansPlafond || NomCorrespondMotifs(nom, MotifsPlafond);
		bool bt01Ici = dansBt01 || nom.Contains("BT-01", System.StringComparison.OrdinalIgnoreCase);

		if (n is MeshInstance3D mi)
		{
			if (plafondIci) mi.Layers = bitPlafond;
			else if (bt01Ici) mi.Layers = bitBt01;
		}

		foreach (var enfant in n.GetChildren())
		{
			ParcourirEtAffecter(enfant, bt01Ici, plafondIci, bitPlafond, bitBt01);
		}
	}

	private void ExclureCalqueDeToutesCameras(Node n, uint masque)
	{
		if (n is Camera3D cam && cam != _camMini)
		{
			cam.CullMask &= masque;
		}
		foreach (var enfant in n.GetChildren()) ExclureCalqueDeToutesCameras(enfant, masque);
	}

	private static bool NomCorrespondMotifs(string nom, string[] motifs)
	{
		if (motifs == null) return false;
		foreach (var m in motifs)
		{
			if (!string.IsNullOrEmpty(m) && nom.Contains(m, System.StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	private void MettreAJourModeCamera()
	{
		if (_camMini == null) return;
		uint bitPlafond = 1u << (CalquePlafond - 1);
		uint bitBt01 = 1u << (CalqueBt01 - 1);
		uint bitMarqueurs = 1u << (CalqueMarqueurs - 1);
		if (_dansBt01)
		{
			// Voir UNIQUEMENT le sous-arbre BT-01 (plus les marqueurs n'ont pas
			// d'utilité ici — l'action se passe à l'extérieur).
			_camMini.CullMask = bitBt01;
			_camMini.Size = TailleOrthoBt01;
		}
		else
		{
			_camMini.CullMask = (0xFFFFFu & ~(bitPlafond | bitBt01)) | bitMarqueurs;
			_camMini.Size = TailleOrtho;
		}
	}

	// =========================================================================
	// Création des marqueurs en monde 3D
	// =========================================================================
	private void CreerMarqueursMonde()
	{
		var racine = GetTree().CurrentScene ?? GetTree().Root;
		_marqueursParent = new Node3D { Name = "MinimapMarqueurs" };
		racine.AddChild(_marqueursParent);
		CollecterEtCreerMarqueurs(racine);
	}

	private void CollecterEtCreerMarqueurs(Node n)
	{
		switch (n)
		{
			case Porte:
				CreerMarqueurPour(n as Node3D, TypeMarqueur.Porte, TailleMarqueur, new Color(1f, 0.4f, 0.4f, 1f), decouvert: true);
				break;
			case Coffre:
			case VanneRotation:
			case LevierSequence:
			case LabyrintheBille:
			case ChiffreRevelable:
				CreerMarqueurPour(n as Node3D, TypeMarqueur.Puzzle, TailleMarqueur, new Color(1f, 0.85f, 0.2f, 1f), decouvert: false);
				break;
		}
		foreach (var enfant in n.GetChildren())
		{
			CollecterEtCreerMarqueurs(enfant);
		}
	}

	private void CreerMarqueurPour(Node3D cible, TypeMarqueur type, float taille, Color couleurInitiale, bool decouvert)
	{
		if (cible == null) return;

		var mesh = new MeshInstance3D();
		var plan = new PlaneMesh { Size = new Vector2(taille, taille) };
		mesh.Mesh = plan;

		var mat = new StandardMaterial3D
		{
			AlbedoColor = couleurInitiale,
			AlbedoTexture = ObtenirTexturePastille(),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			NoDepthTest = true,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		mesh.MaterialOverride = mat;
		mesh.Layers = 1u << (CalqueMarqueurs - 1);
		mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		mesh.Visible = decouvert;

		// Attaché à un conteneur racine, pas à la cible : évite le scale 7 du
		// Coffre, le visible=false du Lever, etc. Position synchronisée chaque
		// frame depuis cible.GlobalPosition.
		_marqueursParent.AddChild(mesh);
		mesh.GlobalPosition = cible.GlobalPosition + new Vector3(0, 0.3f, 0);

		_marqueurs.Add(new Marqueur
		{
			Cible = cible,
			Mesh = mesh,
			Mat = mat,
			Type = type,
			Decouvert = decouvert,
		});
	}

	// =========================================================================
	// Mise à jour par image
	// =========================================================================
	public override void _Process(double delta)
	{
		if (_joueur == null || _camMini == null) return;
		var p = _joueur.GlobalPosition;
		float yaw = _joueur.Rotation.Y;
		// Caméra reculée derrière le joueur pour qu'il reste centré malgré l'inclinaison.
		float tiltRad = Mathf.DegToRad(InclinaisonDeg);
		float decalage = HauteurCamera * Mathf.Tan(tiltRad);
		_camMini.GlobalPosition = new Vector3(
			p.X + decalage * Mathf.Sin(yaw),
			p.Y + HauteurCamera,
			p.Z + decalage * Mathf.Cos(yaw)
		);
		_camMini.Rotation = new Vector3(-Mathf.Pi / 2f + tiltRad, yaw, 0f);

		bool dansBt01 = p.Y > SeuilYBt01;
		if (dansBt01 != _dansBt01)
		{
			_dansBt01 = dansBt01;
			MettreAJourModeCamera();
		}

		MettreAJourMarqueurs(p);
		MettreAJourCompas(p);
		MettreAJourFlecheObjectif(p);
	}

	private void MettreAJourMarqueurs(Vector3 posJoueur)
	{
		float distRevele2 = DistanceRevelationPuzzle * DistanceRevelationPuzzle;
		for (int i = _marqueurs.Count - 1; i >= 0; i--)
		{
			var m = _marqueurs[i];
			if (!GodotObject.IsInstanceValid(m.Cible) || !GodotObject.IsInstanceValid(m.Mesh))
			{
				_marqueurs.RemoveAt(i);
				continue;
			}

			// Suivi de position : TopLevel ignore le parent → on synchronise ici.
			m.Mesh.GlobalPosition = m.Cible.GlobalPosition + new Vector3(0, 0.3f, 0);

			// Révélation par proximité pour les puzzles uniquement.
			if (!m.Decouvert && m.Type == TypeMarqueur.Puzzle)
			{
				float d2 = posJoueur.DistanceSquaredTo(m.Cible.GlobalPosition);
				if (d2 <= distRevele2)
				{
					m.Decouvert = true;
					m.Mesh.Visible = true;
				}
			}

			// Mise à jour couleur selon état.
			switch (m.Cible)
			{
				case Porte porte:
					if (porte.EstOuverte) m.Mat.AlbedoColor = new Color(0.35f, 1f, 0.4f, 1f);
					else if (porte.EstDeverrouille) m.Mat.AlbedoColor = new Color(1f, 0.75f, 0.25f, 1f);
					else if (!porte.PrerequisRempli) m.Mat.AlbedoColor = new Color(0.55f, 0.55f, 0.6f, 1f);
					else m.Mat.AlbedoColor = new Color(1f, 0.35f, 0.35f, 1f);
					break;
				case Coffre c:
					m.Mat.AlbedoColor = c.EstOuvert ? new Color(0.35f, 1f, 0.4f, 1f) : new Color(1f, 0.85f, 0.2f, 1f);
					break;
				case VanneRotation v:
					// Vanne fermée = puzzle résolu (la salle 2 demande de fermer la vanne).
					m.Mat.AlbedoColor = v.EstFermee ? new Color(0.35f, 1f, 0.4f, 1f) : new Color(1f, 0.85f, 0.2f, 1f);
					break;
				case LevierSequence l:
					m.Mat.AlbedoColor = l.EstActive ? new Color(0.35f, 1f, 0.4f, 1f) : new Color(1f, 0.85f, 0.2f, 1f);
					break;
				case LabyrintheBille lab:
					m.Mat.AlbedoColor = lab.EstReussie ? new Color(0.35f, 1f, 0.4f, 1f) : new Color(1f, 0.85f, 0.2f, 1f);
					break;
			}
		}

		// Marqueur de badge actif (un seul, mis à jour en synchro avec GameState).
		MettreAJourMarqueurBadge();
	}

	private Marqueur _marqueurBadge;
	private void MettreAJourMarqueurBadge()
	{
		var cible = GameState.Instance?.BadgeCible;
		bool aBesoin = cible != null && GodotObject.IsInstanceValid(cible) && cible.Visible;

		if (aBesoin && _marqueurBadge == null)
		{
			var mesh = new MeshInstance3D();
			mesh.Mesh = new PlaneMesh { Size = new Vector2(TailleMarqueurBadge, TailleMarqueurBadge) };
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.3f, 0.8f, 1f, 1f),
				AlbedoTexture = ObtenirTexturePastille(),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				NoDepthTest = true,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			};
			mesh.MaterialOverride = mat;
			mesh.Layers = 1u << (CalqueMarqueurs - 1);
			mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			_marqueursParent.AddChild(mesh);
			mesh.GlobalPosition = cible.GlobalPosition + new Vector3(0, 0.5f, 0);
			_marqueurBadge = new Marqueur { Cible = cible, Mesh = mesh, Mat = mat, Type = TypeMarqueur.Badge, Decouvert = true };
		}
		else if (!aBesoin && _marqueurBadge != null)
		{
			if (GodotObject.IsInstanceValid(_marqueurBadge.Mesh)) _marqueurBadge.Mesh.QueueFree();
			_marqueurBadge = null;
		}

		if (_marqueurBadge != null && GodotObject.IsInstanceValid(_marqueurBadge.Mesh))
		{
			_marqueurBadge.Mesh.GlobalPosition = _marqueurBadge.Cible.GlobalPosition + new Vector3(0, 0.5f, 0);
			float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin((float)Time.GetTicksMsec() / 250f));
			_marqueurBadge.Mat.AlbedoColor = new Color(0.3f, 0.8f, 1f, 1f) * pulse + new Color(0, 0, 0, 1f - pulse);
		}
	}

	// =========================================================================
	// Compas N (rotation contraire au yaw : pointe vers le -Z monde)
	// =========================================================================
	private void MettreAJourCompas(Vector3 posJoueur)
	{
		if (_labelN == null) return;
		// Projection du point nord (loin, à -Z monde) via la caméra minimap — gère
		// automatiquement l'inclinaison/rotation.
		var posNord = posJoueur + new Vector3(0, 0, -1000f);
		Vector2 screenN = _camMini.UnprojectPosition(posNord);
		Vector2 centre = new Vector2(TaillePixels.X / 2f, TaillePixels.Y / 2f);
		Vector2 dir = screenN - centre;
		if (dir.LengthSquared() < 0.001f) return;
		dir = dir.Normalized();
		float r = TaillePixels.X / 2f - 14f;
		float demi = _labelN.Size.X / 2f;
		_labelN.Position = new Vector2(centre.X + dir.X * r - demi, centre.Y + dir.Y * r - demi);
	}

	// =========================================================================
	// Flèche d'objectif off-screen
	// =========================================================================
	private void MettreAJourFlecheObjectif(Vector3 posJoueur)
	{
		if (_flecheObjectif == null) return;
		// Même cible que le BoussoleHud (badge → salle suivante → sortie...).
		var cible = BoussoleHud.TrouverProchaineAncreStatique(OrdreSalles);
		if (cible == null || !GodotObject.IsInstanceValid(cible))
		{
			_flecheObjectif.Visible = false;
			return;
		}

		Vector2 centre = new Vector2(TaillePixels.X / 2f, TaillePixels.Y / 2f);
		Vector2 screen = _camMini.UnprojectPosition(cible.GlobalPosition);
		Vector2 vers = screen - centre;
		if (vers.LengthSquared() < 0.01f)
		{
			_flecheObjectif.Visible = false;
			return;
		}

		bool dansCadre = screen.X >= 0 && screen.X <= TaillePixels.X
		              && screen.Y >= 0 && screen.Y <= TaillePixels.Y;
		Vector2 pos;
		if (dansCadre)
		{
			// Cible visible sur la minimap : flèche posée sur sa position projetée.
			pos = vers;
		}
		else
		{
			// Hors cadre : clampe au bord intérieur.
			float marge = 14f;
			float maxX = TaillePixels.X / 2f - marge;
			float maxY = TaillePixels.Y / 2f - marge;
			float facteur = Mathf.Min(
				maxX / Mathf.Max(Mathf.Abs(vers.X), 0.001f),
				maxY / Mathf.Max(Mathf.Abs(vers.Y), 0.001f));
			pos = vers * facteur;
		}

		float demiTaille = _flecheObjectif.Size.X / 2f;
		_flecheObjectif.Position = new Vector2(centre.X + pos.X - demiTaille, centre.Y + pos.Y - demiTaille);
		_flecheObjectif.Rotation = Mathf.Atan2(pos.X, -pos.Y);
		_flecheObjectif.Visible = true;
	}

	// =========================================================================
	// Toggle
	// =========================================================================
	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == ToucheToggle)
		{
			if (_conteneur != null)
			{
				_conteneur.Visible = !_conteneur.Visible;
				if (_viewport != null)
				{
					_viewport.RenderTargetUpdateMode = _conteneur.Visible
						? SubViewport.UpdateMode.Always
						: SubViewport.UpdateMode.Disabled;
				}
			}
			GetViewport().SetInputAsHandled();
		}
	}
}
