using Godot;

// À attacher à un AudioStreamPlayer3D pour garantir un bouclage continu,
// même si l'import .wav ne contient pas la loop_mode.
// Au signal Finished, le son est relancé immédiatement.
public partial class LoopingAudio : AudioStreamPlayer3D
{
	private bool stoppe = false;

	public override void _Ready()
	{
		Finished += OnFinished;
		if (Autoplay && !Playing)
			Play();
	}

	private void OnFinished()
	{
		if (stoppe) return;
		Play();
	}

	// Stoppe le son et empêche toute relance par le loop.
	public void StopDefinitivement()
	{
		stoppe = true;
		Stop();
	}
}
