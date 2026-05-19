using Godot;

// Classe de base pour les HUDs de puzzle.
// Mutualise :
//  - Le Label "Message" + timer d'affichage temporaire (ShowMessage).
//  - L'option "VisibleSeulementDansZone" et la bascule de visibilité.
//
// Pilotage de la visibilité — deux modes :
//  1. Legacy (zone Area3D) : la sous-classe abonne `OnJoueurEntreZone` /
//     `OnJoueurSortZone` aux signaux du puzzle. Utilisé pour les puzzles qu'on
//     ne peut pas "viser" (labyrinthe, triggers de zone).
//  2. Raycast (Survol3D) : la sous-classe assigne `CibleSurvol` à un Node3D
//     dans OnHudReady (typiquement le puzzle lui-même). Le HUD apparaît alors
//     uniquement quand le joueur regarde ce node, et disparaît après
//     `DelaiMasquage` secondes sans regarder. Le mode raycast prend le pas
//     sur les signaux d'Area3D quand CibleSurvol est non-nul.
//
// Les sous-classes overridelent OnHudReady() / OnHudProcess() au lieu de
// _Ready() / _Process() pour bénéficier automatiquement du comportement de base.
// `OnHudVisible()` / `OnHudHidden()` sont appelés à chaque transition de
// visibilité (les deux modes confondus) — c'est là que la sous-classe rafraîchit
// l'état affiché.
public abstract partial class BasePuzzleHud : CanvasLayer
{
	[Export] public NodePath MessageLabelPath;
	[Export] public bool VisibleSeulementDansZone = true;
	// Délai (en s) sans regarder la cible avant de masquer le HUD. Évite que le
	// HUD clignote si le joueur tourne brièvement la tête. 0 = masquage instantané.
	[Export] public float DelaiMasquage = 0.25f;

	// Compteur global de HUDs actifs (= joueur dans la zone d'un puzzle).
	// Lu par SurvolHud pour masquer son label texte quand un puzzle HUD
	// affiche déjà l'info détaillée — évite la redondance.
	// Le réticule + le glow 3D du SurvolHud restent, eux, toujours utiles.
	public static int NbHudsActifs { get; private set; } = 0;

	protected Label messageLabel;
	// Si non-null : la visibilité est pilotée par le raycast (Survol3D) sur ce
	// node, et non par les signaux d'Area3D. À assigner par la sous-classe dans
	// OnHudReady (typiquement = le puzzle lui-même).
	protected Node3D CibleSurvol;

	private float messageTimer = 0f;
	// Garde si ce HUD a déjà compté son "entrée zone" — évite les doubles
	// décréments en cas de signaux redondants ou d'exit-tree.
	private bool compteCommeActif = false;
	private float tempsHorsRegard = 0f;

	public override void _Ready()
	{
		if (MessageLabelPath != null && !MessageLabelPath.IsEmpty)
			messageLabel = GetNodeOrNull<Label>(MessageLabelPath);
		if (messageLabel != null) messageLabel.Text = "";

		OnHudReady();

		if (VisibleSeulementDansZone)
			Visible = false;
	}

	public override void _Process(double delta)
	{
		if (messageTimer > 0f)
		{
			messageTimer -= (float)delta;
			if (messageTimer <= 0f && messageLabel != null)
				messageLabel.Text = "";
		}

		// Mode raycast : la sous-classe a renseigné CibleSurvol. On pilote la
		// visibilité depuis Survol3D, indépendamment des signaux d'Area3D.
		if (CibleSurvol != null && VisibleSeulementDansZone)
		{
			bool regarde = Survol3D.CibleCourante == CibleSurvol;
			if (regarde)
			{
				tempsHorsRegard = 0f;
				if (!compteCommeActif) ActiverHud();
			}
			else
			{
				tempsHorsRegard += (float)delta;
				if (compteCommeActif && tempsHorsRegard >= DelaiMasquage)
					DesactiverHud();
			}
		}

		OnHudProcess(delta);
	}

	protected virtual void OnHudReady() { }
	protected virtual void OnHudProcess(double delta) { }
	// Appelés sur transition de visibilité, peu importe le mode (zone ou raycast).
	// Idempotents : non rappelés tant que la visibilité ne change pas.
	protected virtual void OnHudVisible() { }
	protected virtual void OnHudHidden() { }

	protected void ShowMessage(string msg, float duration)
	{
		if (messageLabel != null) messageLabel.Text = msg;
		messageTimer = duration;
	}

	private void ActiverHud()
	{
		Visible = true;
		compteCommeActif = true;
		NbHudsActifs++;
		OnHudVisible();
	}

	private void DesactiverHud()
	{
		Visible = false;
		compteCommeActif = false;
		NbHudsActifs = Mathf.Max(0, NbHudsActifs - 1);
		OnHudHidden();
	}

	protected virtual void OnJoueurEntreZone()
	{
		// Si la sous-classe a opté pour le pilotage raycast, on ignore les
		// signaux d'Area3D côté visibilité — c'est _Process qui décide.
		if (CibleSurvol != null) return;
		if (compteCommeActif) return;
		if (VisibleSeulementDansZone) Visible = true;
		compteCommeActif = true;
		NbHudsActifs++;
		OnHudVisible();
	}

	protected virtual void OnJoueurSortZone()
	{
		if (CibleSurvol != null) return;
		if (!compteCommeActif) return;
		if (VisibleSeulementDansZone) Visible = false;
		compteCommeActif = false;
		NbHudsActifs = Mathf.Max(0, NbHudsActifs - 1);
		OnHudHidden();
	}

	public override void _ExitTree()
	{
		// Filet de sécurité : si on change de scène alors que le joueur est
		// encore dans une zone, le décrément n'aurait jamais lieu.
		if (compteCommeActif)
		{
			compteCommeActif = false;
			NbHudsActifs = Mathf.Max(0, NbHudsActifs - 1);
		}
	}
}
