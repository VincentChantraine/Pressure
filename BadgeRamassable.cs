using Godot;

// Badge 3D unique posé dans la scène. Représente le Badge 1 au démarrage ;
// quand les 6 salles sont validées (signal PorteFinaleDebloquee), il réapparaît
// en tant que Badge 2 — soit au même endroit, soit à la position d'un Marker3D
// optionnel (MarqueurPosition2).
//
// Pickup automatique : le joueur entre dans la Area3D de ramassage → consommation
// immédiate avec animation scale-down + montée + rotation, son optionnel.
//
// Setup éditeur attendu :
//   - Node3D racine avec ce script.
//   - MeshInstance3D de la carte/badge.
//   - Area3D + CollisionShape3D (~30-50 cm de rayon), assignée via ZoneRamassagePath.
//   - (Optionnel) Marker3D ailleurs dans la scène pour Badge 2, assigné via MarqueurPosition2.
//   - (Optionnel) AudioStream pour le son de ramassage.
public partial class BadgeRamassable : Node3D
{
	[Export] public NodePath ZoneRamassagePath;
	// Si renseigné : le badge se téléporte ici pour devenir Badge 2 quand les
	// 6 salles sont validées. Sinon, il réapparaît à sa position initiale.
	[Export] public NodePath MarqueurPosition2;
	// Labels 3D "1" et "2" affichés sur la carte. On n'affiche que celui qui
	// correspond au numéro courant du badge.
	[Export] public NodePath Label1Path;
	[Export] public NodePath Label2Path;

	[Export] public AudioStream SonRamassage;
	[Export] public float VolumeDbRamassage = -8f;

	// Animation de disparition : durée totale + amplitude de la montée verticale
	// + nombre de tours sur lui-même. À régler dans l'inspecteur si besoin.
	[Export] public float DureeDisparition = 0.45f;
	[Export] public float HauteurMontee = 0.6f;
	[Export] public float ToursDisparition = 1.5f;

	private Area3D zoneRamassage;
	private AudioStreamPlayer3D playerSon;
	private Label3D label1;
	private Label3D label2;
	private int numeroActuel = 1;
	private bool ramasse = false;

	// Sauvegarde de l'état initial pour pouvoir réapparaître proprement
	// quand le badge passe à Badge 2.
	private Vector3 posLocaleInitiale;
	private Vector3 echelleInitiale;
	private Vector3 rotationInitiale;

	public override void _Ready()
	{
		posLocaleInitiale = Position;
		echelleInitiale   = Scale;
		rotationInitiale  = Rotation;

		if (ZoneRamassagePath != null && !ZoneRamassagePath.IsEmpty)
			zoneRamassage = GetNodeOrNull<Area3D>(ZoneRamassagePath);
		if (zoneRamassage != null)
			zoneRamassage.BodyEntered += OnBodyEntered;
		else
			GD.PushWarning("[BadgeRamassable] ZoneRamassagePath non assignée — le badge ne sera jamais ramassé.");

		if (Label1Path != null && !Label1Path.IsEmpty)
			label1 = GetNodeOrNull<Label3D>(Label1Path);
		if (Label2Path != null && !Label2Path.IsEmpty)
			label2 = GetNodeOrNull<Label3D>(Label2Path);
		// Fallback : si les NodePath ne sont pas assignés dans l'inspecteur, on
		// tente de retrouver les labels par nom (Label3D 1 / Label3D 2 ou tout
		// enfant Label3D dont le nom contient "1" / "2").
		if (label1 == null || label2 == null)
		{
			foreach (Node child in GetChildren())
			{
				if (child is not Label3D lbl) continue;
				if (label1 == null && lbl.Name.ToString().Contains("1")) label1 = lbl;
				else if (label2 == null && lbl.Name.ToString().Contains("2")) label2 = lbl;
			}
		}
		AppliquerVisibiliteLabels();

		if (SonRamassage == null)
			SonRamassage = GD.Load<AudioStream>("res://Son/item-pick-up.mp3");
		if (SonRamassage != null)
		{
			playerSon = new AudioStreamPlayer3D
			{
				Stream = SonRamassage,
				VolumeDb = VolumeDbRamassage,
			};
			AddChild(playerSon);
		}

		if (GameState.Instance != null)
		{
			GameState.Instance.PorteFinaleDebloquee += ActiverBadge2;
			// Badge 1 disponible dès le démarrage → la boussole peut pointer vers nous.
			GameState.Instance.BadgeCible = this;
		}
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
		{
			GameState.Instance.PorteFinaleDebloquee -= ActiverBadge2;
			if (GameState.Instance.BadgeCible == this)
				GameState.Instance.BadgeCible = null;
		}
	}

	private void AppliquerVisibiliteLabels()
	{
		if (label1 != null) label1.Visible = numeroActuel == 1;
		if (label2 != null) label2.Visible = numeroActuel == 2;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (ramasse) return;
		if (body is Player) Ramasser();
	}

	private void Ramasser()
	{
		if (GameState.Instance == null || ramasse) return;
		ramasse = true;

		GameState.Instance.RamasserBadge(numeroActuel);

		if (playerSon != null && playerSon.Stream != null)
			playerSon.Play();

		// Animation : scale → 0, montée verticale, rotation autour de Y, en parallèle.
		var tween = CreateTween();
		tween.SetParallel(true);
		// On évite Vector3.Zero pile : l'Area3D enfant hériterait d'un basis
		// dont le déterminant est nul → spam d'erreurs "det == 0" en console.
		// 0.001 reste imperceptible visuellement.
		tween.TweenProperty(this, "scale", Vector3.One * 0.001f, DureeDisparition)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.In);
		tween.TweenProperty(this, "position",
			Position + Vector3.Up * HauteurMontee, DureeDisparition)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "rotation:y",
			Rotation.Y + Mathf.Tau * ToursDisparition, DureeDisparition);
		// En fin d'anim : on masque (et on coupe la zone pour ne pas re-trigger).
		tween.Chain().TweenCallback(Callable.From(MasquerApresAnim));
	}

	private void MasquerApresAnim()
	{
		Visible = false;
		if (zoneRamassage != null)
			zoneRamassage.Monitoring = false;
		// Plus de cible badge pour la boussole tant que Badge 2 n'apparaît pas.
		if (GameState.Instance != null && GameState.Instance.BadgeCible == this)
			GameState.Instance.BadgeCible = null;
	}

	private void ActiverBadge2()
	{
		// Idempotence : si on a déjà basculé en Badge 2, on ignore.
		if (numeroActuel >= 2) return;
		numeroActuel = 2;

		// Position : marqueur si configuré, sinon position initiale du Badge 1.
		if (MarqueurPosition2 != null && !MarqueurPosition2.IsEmpty)
		{
			var marker = GetNodeOrNull<Node3D>(MarqueurPosition2);
			if (marker != null)
				GlobalPosition = marker.GlobalPosition;
			else
				Position = posLocaleInitiale;
		}
		else
		{
			Position = posLocaleInitiale;
		}

		Scale = echelleInitiale;
		// Rotation initiale - 90° autour de Y : marque visuellement que ce n'est
		// plus le même badge (Badge 2 plutôt que Badge 1) au même emplacement.
		Vector3 rot = rotationInitiale;
		rot.Y -= Mathf.Pi * 0.5f;
		Rotation = rot;
		Visible = true;
		ramasse = false;
		AppliquerVisibiliteLabels();
		if (zoneRamassage != null)
			zoneRamassage.Monitoring = true;
		// La boussole peut de nouveau pointer vers nous (en tant que Badge 2).
		if (GameState.Instance != null)
			GameState.Instance.BadgeCible = this;
	}
}
