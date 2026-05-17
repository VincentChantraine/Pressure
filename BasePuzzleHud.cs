using Godot;

// Classe de base pour les HUDs de puzzle.
// Mutualise :
//  - Le Label "Message" + timer d'affichage temporaire (ShowMessage).
//  - L'option "VisibleSeulementDansZone" et la bascule de visibilité.
//
// Les sous-classes overridelent OnHudReady() / OnHudProcess() au lieu de
// _Ready() / _Process() pour bénéficier automatiquement du comportement de base.
// Elles peuvent aussi override OnJoueurEntreZone() / OnJoueurSortZone() en
// appelant base.OnXxx() pour conserver la bascule de visibilité.
public abstract partial class BasePuzzleHud : CanvasLayer
{
	[Export] public NodePath MessageLabelPath;
	[Export] public bool VisibleSeulementDansZone = true;

	// Compteur global de HUDs actifs (= joueur dans la zone d'un puzzle).
	// Lu par SurvolHud pour masquer son label texte quand un puzzle HUD
	// affiche déjà l'info détaillée — évite la redondance.
	// Le réticule + le glow 3D du SurvolHud restent, eux, toujours utiles.
	public static int NbHudsActifs { get; private set; } = 0;

	protected Label messageLabel;
	private float messageTimer = 0f;
	// Garde si ce HUD a déjà compté son "entrée zone" — évite les doubles
	// décréments en cas de signaux redondants ou d'exit-tree.
	private bool compteCommeActif = false;

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
		OnHudProcess(delta);
	}

	protected virtual void OnHudReady() { }
	protected virtual void OnHudProcess(double delta) { }

	protected void ShowMessage(string msg, float duration)
	{
		if (messageLabel != null) messageLabel.Text = msg;
		messageTimer = duration;
	}

	protected virtual void OnJoueurEntreZone()
	{
		if (VisibleSeulementDansZone) Visible = true;
		if (!compteCommeActif)
		{
			compteCommeActif = true;
			NbHudsActifs++;
		}
	}

	protected virtual void OnJoueurSortZone()
	{
		if (VisibleSeulementDansZone) Visible = false;
		if (compteCommeActif)
		{
			compteCommeActif = false;
			NbHudsActifs = Mathf.Max(0, NbHudsActifs - 1);
		}
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
