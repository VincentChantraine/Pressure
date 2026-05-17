using Godot;
using System.Collections.Generic;

// À déclarer en AUTOLOAD dans Projet → Paramètres du projet → Autoload
// Nom : "GameState", Chemin : res://GameState.cs
public partial class GameState : Node
{
	public static GameState Instance { get; private set; }

	// =========================================================================
	// UIDs DES BADGES — HARDCODÉS
	// =========================================================================
	public const string BADGE_1_UID = "23ED5333";
	public const string BADGE_2_UID = "EA89CA80";

	// =========================================================================
	// CHEMINS DES SCÈNES (pour les transitions menu/jeu/fin)
	// Adapte si tu renommes tes .tscn
	// =========================================================================
	[Export] public string SceneMenu = "res://MenuPrincipal.tscn";
	[Export] public string SceneJeu  = "res://node_3d.tscn";
	[Export] public string SceneFin  = "res://EcranFin.tscn";

	// Résultat de la dernière partie — lu par EcranFin pour savoir quoi afficher
	public enum ResultatPartie { Aucun, Victoire, Defaite }
	public ResultatPartie DernierResultat = ResultatPartie.Aucun;
	public float DernierTempsEcoule = 0f;

	// Étapes de fin de partie pour la boussole (post-6-salles).
	// PorteSortieFranchie : true quand le joueur a passé la porte_sortie
	//   (détecté par l'entrée dans la zone PorteRondeTP).
	// SortieFinaleAtteinte : true quand JoueurEntreeBT01 a été émis
	//   (téléportation effectuée vers le BT-01).
	public bool PorteSortieFranchie { get; set; } = false;
	public bool SortieFinaleAtteinte { get; set; } = false;

	// Référence au chrono courant : injectée par PartieManager au démarrage de la
	// partie. Permet à MarquerSalleVisitee de capter l'elapsed sans coupler les
	// callsites de puzzle au timer.
	public ServoGameTimer Timer { get; set; }

	// Suivi de progression
	public HashSet<string> PortesDeverrouilles = new HashSet<string>();
	public HashSet<string> SallesVisitees = new HashSet<string>();

	// Ancres "cible" de la boussole, rempli par les Portes uniquement.
	// La boussole pointe vers ces nœuds (= portes menant aux salles à valider).
	// Si une salle n'a pas de porte associée (ex: salle_1 dans le hub), pas
	// de flèche pour cette salle — c'est volontaire (pas de spoil).
	public Dictionary<string, Node3D> AncresSalles = new Dictionary<string, Node3D>();

	// Centres de salle (= position des puzzles), rempli par les puzzles eux-mêmes.
	// Sert UNIQUEMENT à la détection de proximité : si le joueur est près d'un
	// puzzle, il est dans la salle correspondante → la boussole se masque pour
	// ne pas donner la solution.
	public Dictionary<string, Node3D> CentresSalles = new Dictionary<string, Node3D>();

	// Chrono des validations : ordre + temps écoulé à la validation de chaque salle.
	// L'ordre suit la séquence réelle de résolution des puzzles (utile pour calculer
	// la durée passée par salle dans EcranFin).
	public List<string> OrdreSallesValidees = new List<string>();
	public Dictionary<string, float> TempsValidationParSalle = new Dictionary<string, float>();

	// File des scans RFID en attente de consommation.
	// On utilise une file (FIFO) plutôt qu'un seul UID pour ne plus PERDRE
	// les scans rapides : si le joueur scanne deux badges avant d'atteindre
	// une porte, les deux sont conservés et traités dans l'ordre.
	// L'API publique (HasPendingRfid, PendingRfidUid, ConsumePendingRfid)
	// reste identique à l'ancienne version "string unique" pour ne rien casser
	// côté consommateurs (Porte.cs).
	private Queue<string> rfidQueue = new Queue<string>();

	public string PendingRfidUid => rfidQueue.Count > 0 ? rfidQueue.Peek() : "";
	public bool HasPendingRfid => rfidQueue.Count > 0;

	// Signaux
	[Signal] public delegate void ScanValideEventHandler(string porteId);
	[Signal] public delegate void ScanInvalideEventHandler(string raison);
	[Signal] public delegate void PorteFinaleDebloqueeEventHandler();
	[Signal] public delegate void PorteFinaleOuverteEventHandler();
	[Signal] public delegate void JoueurEntreeBT01EventHandler();
	// Émis à chaque salle nouvellement validée (premier appel par id).
	// Sert au screen shake + flash blanc côté PartieManager.
	[Signal] public delegate void SalleValideeEventHandler(string salleId);

	public override void _EnterTree()
	{
		Instance = this;
		InputBindings.EnregistrerActionsParDefaut();
		RemapJeu.Charger(); // écrase les défauts par les overrides utilisateur
		ParametresJeu.Charger();
	}

	/// <summary>
	/// Enregistre la CIBLE de boussole d'une salle — typiquement la porte qui
	/// y mène. Appelé par Porte._Ready. La boussole pointera vers cette ancre
	/// tant que la salle n'est pas validée.
	/// </summary>
	public void RegistrerAncreSalle(string salleId, Node3D ancre)
	{
		if (string.IsNullOrEmpty(salleId) || ancre == null) return;
		AncresSalles[salleId] = ancre;
	}

	/// <summary>
	/// Enregistre le CENTRE physique d'une salle (typiquement le puzzle principal).
	/// Appelé par les puzzles dans _Ready. Utilisé uniquement par BoussoleHud
	/// pour le masquage de proximité — JAMAIS comme cible de flèche.
	/// </summary>
	public void RegistrerCentreSalle(string salleId, Node3D centre)
	{
		if (string.IsNullOrEmpty(salleId) || centre == null) return;
		CentresSalles[salleId] = centre;
	}

	public void NotifyRfidScan(string uid)
	{
		string clean = uid?.Trim().ToUpper();
		if (string.IsNullOrEmpty(clean)) return;

		// Dédoublonnage défensif : si ce même UID est déjà en file d'attente,
		// inutile de l'empiler à nouveau (ex: scan répété, rebond capteur).
		if (rfidQueue.Contains(clean)) return;

		rfidQueue.Enqueue(clean);
		GD.Print($"[GameState] Scan RFID reçu : {clean} (file : {rfidQueue.Count})");
	}

	public void ConsumePendingRfid()
	{
		if (rfidQueue.Count > 0)
			rfidQueue.Dequeue();
	}

	public void MarquerPorteDeverrouille(string porteId, bool estFinale = false)
	{
		PortesDeverrouilles.Add(porteId);
		EmitSignal(SignalName.ScanValide, porteId);
		GD.Print($"[GameState] Porte {porteId} déverrouillée.");

		if (estFinale)
			GD.Print("[GameState] Porte finale déverrouillée — le joueur peut passer.");
	}

	public void NotifierEntreeBT01()
	{
		SortieFinaleAtteinte = true;
		EmitSignal(SignalName.JoueurEntreeBT01);
	}

	// Appelé par VictoireCommandeTrigger quand le joueur atteint les commandes
	// du BT-01 (téléporté depuis la sortie via FinPartieTrigger).
	public void DeclencherVictoire()
	{
		GD.Print("[GameState] Joueur a repris les commandes — VICTOIRE.");
		EmitSignal(SignalName.PorteFinaleOuverte);
	}

	public void MarquerSalleVisitee(string salleId)
	{
		if (SallesVisitees.Add(salleId))
		{
			float t = Timer != null ? Timer.GetElapsed() : 0f;
			OrdreSallesValidees.Add(salleId);
			TempsValidationParSalle[salleId] = t;
			GD.Print($"[GameState] Salle {salleId} visitée à {t:F1}s. Total : {SallesVisitees.Count}/6");
			EmitSignal(SignalName.SalleValidee, salleId);
		}

		if (SallesVisitees.Count >= 6)
			EmitSignal(SignalName.PorteFinaleDebloquee);
	}

	public bool PorteFinalePeutEtreDeverrouille()
	{
		return SallesVisitees.Count >= 6;
	}

	public void SignalerScanInvalide(string raison)
	{
		EmitSignal(SignalName.ScanInvalide, raison);
		GD.Print($"[GameState] Scan invalide : {raison}");
	}

	public bool EstBadge1(string uid) => uid == BADGE_1_UID;
	public bool EstBadge2(string uid) => uid == BADGE_2_UID;

	// =========================================================================
	// Reset / transitions de scène
	// =========================================================================
	public void ResetPartie()
	{
		PortesDeverrouilles.Clear();
		SallesVisitees.Clear();
		OrdreSallesValidees.Clear();
		TempsValidationParSalle.Clear();
		AncresSalles.Clear();
		CentresSalles.Clear();
		rfidQueue.Clear();
		DernierResultat = ResultatPartie.Aucun;
		DernierTempsEcoule = 0f;
		PorteSortieFranchie = false;
		SortieFinaleAtteinte = false;
		// Timer non remis à null : la nouvelle scène de jeu réinjectera la référence
		// via PartieManager._Ready(). Conserver l'ancienne ne sert à rien mais ne
		// nuit pas (elle sera écrasée).
		GD.Print("[GameState] Partie réinitialisée.");
	}

	public void ChargerMenu()
	{
		GetTree().ChangeSceneToFile(SceneMenu);
	}

	public void LancerPartie()
	{
		ResetPartie();
		GetTree().ChangeSceneToFile(SceneJeu);
	}

	public void TerminerPartie(ResultatPartie resultat, float tempsEcoule)
	{
		DernierResultat = resultat;
		DernierTempsEcoule = tempsEcoule;
		// Déféré pour éviter "Removing a CollisionObject during a physics callback"
		// quand la fin est déclenchée par un BodyEntered (FinPartieTrigger).
		CallDeferred(nameof(ChangerVersSceneFin));
	}

	private void ChangerVersSceneFin()
	{
		GetTree().ChangeSceneToFile(SceneFin);
	}
}
