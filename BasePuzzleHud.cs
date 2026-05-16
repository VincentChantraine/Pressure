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

	protected Label messageLabel;
	private float messageTimer = 0f;

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
	}

	protected virtual void OnJoueurSortZone()
	{
		if (VisibleSeulementDansZone) Visible = false;
	}
}
