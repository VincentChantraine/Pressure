using Godot;
using System.Linq;

// Logique du coffre-fort à combinaison.
// Attache-le à un Node3D (ou StaticBody3D) représentant le coffre.
// Ajoute un Area3D enfant pour détecter le joueur (référencé via ZonePath).
//
// Fonctionnement :
// - Le joueur entre dans la zone : le coffre "s'arme".
// - Chiffre N°1 : tourner dans le sens voulu du nombre de crans requis, puis cliquer le bouton encodeur.
// - Chiffre N°2 : sens OPPOSÉ au précédent, nombre de crans requis, clic.
// - Etc.
// - À chaque clic : si le cran compté ET le sens correspondent, on passe au suivant. Sinon, RESET.
// - Dernier chiffre validé : coffre ouvert, signal émis.
public partial class Coffre : Node3D
{
	[Export] public NodePath ArduinoPath;
	[Export] public NodePath ZonePath;

	// Sons (optionnels). À brancher sur des AudioStreamPlayer3D dans la scène.
	// SonChiffreValidePath  : joué à chaque chiffre validé (court click/déverrouillage).
	// SonOuverturePath      : joué une seule fois quand le coffre s'ouvre.
	[Export] public NodePath SonChiffreValidePath;
	[Export] public NodePath SonOuverturePath;

	// Si renseigné, valide cette salle dans GameState quand le coffre s'ouvre.
	[Export] public string SalleIdAValider = "";

	// La combinaison. Indices pairs (0, 2, ...) = sens GAUCHE, indices impairs = DROITE.
	// Exemple "4 gauche, 3 droite, 6 gauche, 8 droite" :
	[Export] public int[] Combinaison = new int[] { 4, 3, 6, 8 };

	// Si l'encodeur tourne "à l'envers" physiquement (cran droit = valeur négative),
	// mettre à true pour compenser.
	[Export] public bool InvertEncoder = false;

	// Tolérance en crans autorisée sur chaque chiffre (0 = strict, 1 = ±1 cran, etc.)
	[Export] public int Tolerance = 0;

	[Signal] public delegate void ChiffreValideEventHandler(int indexChiffre);
	[Signal] public delegate void CombinaisonRateeEventHandler(string raison);
	[Signal] public delegate void CoffreOuvertEventHandler();
	[Signal] public delegate void ProgressionChangeeEventHandler(int indexChiffre, int cransCourants, int sensAttendu);
	[Signal] public delegate void JoueurEntreZoneEventHandler();
	[Signal] public delegate void JoueurSortZoneEventHandler();

	private Node3d arduino;
	private Area3D zone;
	private AudioStreamPlayer3D sonChiffreValide;
	private AudioStreamPlayer3D sonOuverture;

	private bool joueurDansZone = false;
	private bool ouvert = false;
	private int indexCourant = 0;   // quel chiffre on attend
	private int cransAccumules = 0; // compteur depuis le dernier clic

	public override void _Ready()
	{
		if (ArduinoPath != null && !ArduinoPath.IsEmpty)
			arduino = GetNode<Node3d>(ArduinoPath);

		if (ZonePath != null && !ZonePath.IsEmpty)
		{
			zone = GetNode<Area3D>(ZonePath);
			zone.BodyEntered += (Node3D body) =>
			{
				if (body is Player)
				{
					joueurDansZone = true;
					EmitSignal(SignalName.JoueurEntreZone);
				}
			};
			zone.BodyExited += (Node3D body) => 
			{ 
				if (body is Player) 
				{
					joueurDansZone = false;
					EmitSignal(SignalName.JoueurSortZone);
				}
			};
		}
		else
		{
			// Pas de zone : coffre toujours actif.
			joueurDansZone = true;
		}

		if (SonChiffreValidePath != null && !SonChiffreValidePath.IsEmpty)
			sonChiffreValide = GetNodeOrNull<AudioStreamPlayer3D>(SonChiffreValidePath);
		if (SonOuverturePath != null && !SonOuverturePath.IsEmpty)
			sonOuverture = GetNodeOrNull<AudioStreamPlayer3D>(SonOuverturePath);

		GameState.Instance?.RegistrerCentreSalle(SalleIdAValider, this);

		AddToGroup("interactif");
		SetMeta("libelle_interaction", "Molette ← / →  ·  Entrée pour valider");
	}

	public override void _Process(double delta)
	{
		if (ouvert || arduino == null || !joueurDansZone) return;

		// 1) Consommer les crans depuis la dernière frame
		int d = arduino.ConsumeEncoderDelta();
		if (InvertEncoder) d = -d;

		if (d != 0)
		{
			cransAccumules += d;
			EmettreProgression();
		}

		// 2) Détection clic bouton encodeur
		if (arduino.EncoderButtonJustPressed)
		{
			ValiderChiffreCourant();
		}
	}

	private int SensAttendu(int idx)
	{
		// idx pair (0, 2, 4...) = GAUCHE = négatif
		// idx impair (1, 3, 5...) = DROITE = positif
		return (idx % 2 == 0) ? -1 : +1;
	}

	private void ValiderChiffreCourant()
	{
		int attendu = Combinaison[indexCourant];   // toujours positif
		int sens = SensAttendu(indexCourant);
		int cible = attendu * sens;                // ex: -4 pour "4 à gauche"

		int diff = Mathf.Abs(cransAccumules - cible);

		// Sens correct ?
		bool bonSens = (cransAccumules == 0 && attendu == 0)
					   || (Mathf.Sign(cransAccumules) == sens);

		if (!bonSens || diff > Tolerance)
		{
			EmitSignal(SignalName.CombinaisonRatee, $"Chiffre {indexCourant + 1} raté");
			Reset();
			return;
		}

		EmitSignal(SignalName.ChiffreValide, indexCourant);
		indexCourant++;
		cransAccumules = 0;

		if (indexCourant >= Combinaison.Length)
		{
			ouvert = true;
			// Si un click "chiffre valide" est en cours, on le coupe pour laisser place à l'ouverture.
			if (sonChiffreValide != null && sonChiffreValide.Playing) sonChiffreValide.Stop();
			if (sonOuverture != null) sonOuverture.Play();
			EmitSignal(SignalName.CoffreOuvert);
			GD.Print("[Coffre] Ouvert !");

			if (!string.IsNullOrEmpty(SalleIdAValider))
				GameState.Instance?.MarquerSalleVisitee(SalleIdAValider);
		}
		else
		{
			// Click court à chaque chiffre intermédiaire validé.
			if (sonChiffreValide != null)
			{
				if (sonChiffreValide.Playing) sonChiffreValide.Stop();
				sonChiffreValide.Play();
			}
		}

		if (indexCourant < Combinaison.Length)
			EmettreProgression();
	}

	private void Reset()
	{
		indexCourant = 0;
		cransAccumules = 0;
		EmettreProgression();
	}

	private void EmettreProgression()
	{
		if (indexCourant < Combinaison.Length)
			EmitSignal(SignalName.ProgressionChangee, indexCourant, cransAccumules, SensAttendu(indexCourant));
	}

	public bool EstOuvert => ouvert;
	public int[] GetCombinaison() => Combinaison.ToArray();
}
