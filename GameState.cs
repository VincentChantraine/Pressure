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

	// Suivi de progression
	public HashSet<string> PortesDeverrouilles = new HashSet<string>();
	public HashSet<string> SallesVisitees = new HashSet<string>();

	// Événement du dernier scan
	public string PendingRfidUid = "";
	public bool HasPendingRfid => !string.IsNullOrEmpty(PendingRfidUid);

	// Signaux
	[Signal] public delegate void ScanValideEventHandler(string porteId);
	[Signal] public delegate void ScanInvalideEventHandler(string raison);
	[Signal] public delegate void PorteFinaleDebloqueeEventHandler();
	[Signal] public delegate void PorteFinaleOuverteEventHandler();
	[Signal] public delegate void JoueurEntreeBT01EventHandler();

	public override void _EnterTree()
	{
		Instance = this;
	}

	public void NotifyRfidScan(string uid)
	{
		PendingRfidUid = uid.Trim().ToUpper();
		GD.Print($"[GameState] Scan RFID reçu : {PendingRfidUid}");
	}

	public void ConsumePendingRfid()
	{
		PendingRfidUid = "";
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
			GD.Print($"[GameState] Salle {salleId} visitée. Total : {SallesVisitees.Count}/6");

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
		PendingRfidUid = "";
		DernierResultat = ResultatPartie.Aucun;
		DernierTempsEcoule = 0f;
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
