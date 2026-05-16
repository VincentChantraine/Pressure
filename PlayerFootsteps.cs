using Godot;
using System.Collections.Generic;

public partial class PlayerFootsteps : Node
{
	[Export] public NodePath PlayerPath;
	[Export] public NodePath AudioPath;

	[Export] public AudioStream[] SamplesMarche = new AudioStream[0];
	[Export] public AudioStream[] SamplesCourse = new AudioStream[0];

	[Export] public float IntervalleMarche = 0.5f;
	[Export] public float IntervalleCourse = 0.3f;

	[Export] public float SeuilVitesse = 0.5f;
	[Export] public float SeuilCourse = 7.0f;

	[Export] public float VariationPitch = 0.1f;

	[Export] public float VolumeDbMarche = -6f;
	[Export] public float VolumeDbCourse = -3f;

	[Export] public float PitchCourseFallback = 1.3f;

	private Player player;
	private AudioStreamPlayer3D audio;

	private float timer = 0f;
	private int dernierIndex = -1;
	private RandomNumberGenerator rng = new RandomNumberGenerator();

	public override void _Ready()
	{
		if (PlayerPath != null && !PlayerPath.IsEmpty)
			player = GetNodeOrNull<Player>(PlayerPath);

		if (AudioPath != null && !AudioPath.IsEmpty)
		audio = GetNodeOrNull<AudioStreamPlayer3D>(AudioPath);

		if (player == null)
			GD.PrintErr("[PlayerFootsteps] Player introuvable.");
		if (audio == null)
			GD.PrintErr("[PlayerFootsteps] AudioStreamPlayer introuvable.");
		if (SamplesMarche.Length == 0)
			GD.PrintErr("[PlayerFootsteps] SamplesMarche vide — aucun son ne sera joué.");

		rng.Randomize();
	}

	public override void _Process(double delta)
	{
		if (player == null || audio == null) return;
		if (SamplesMarche.Length == 0) return;

		if (!player.IsOnFloor())
		{
			timer = 0f;
			if (audio.Playing) audio.Stop();
			return;
		}

		Vector3 v = player.Velocity;
		v.Y = 0f;
		float vitesse = v.Length();

		if (vitesse < SeuilVitesse)
		{
			timer = 0f;
			if (audio.Playing) audio.Stop();
			return;
		}

		bool courseActive = vitesse >= SeuilCourse;
		float intervalle = courseActive ? IntervalleCourse : IntervalleMarche;

		timer += (float)delta;
		if (timer >= intervalle)
		{
			timer = 0f;
			JouerPas(courseActive);
		}
	}

	private void JouerPas(bool course)
	{
		AudioStream[] banque;
		float pitchBase;
		float volumeDb;

		if (course && SamplesCourse.Length > 0)
		{
			banque = SamplesCourse;
			pitchBase = 1.0f;
			volumeDb = VolumeDbCourse;
		}
		else
		{
			banque = SamplesMarche;
			pitchBase = course ? PitchCourseFallback : 1.0f;
			volumeDb = course ? VolumeDbCourse : VolumeDbMarche;
		}

		int index;
		if (banque.Length == 1)
		{
			index = 0;
		}
		else
		{
			do { index = rng.RandiRange(0, banque.Length - 1); }
			while (index == dernierIndex);
		}
		dernierIndex = index;

		float variation = rng.RandfRange(-VariationPitch, VariationPitch);
		audio.PitchScale = pitchBase + variation;
		audio.VolumeDb = volumeDb;
		audio.Stream = banque[index];
		audio.Play();
	}
}
