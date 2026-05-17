using Godot;
using System;

// Stockage et application des paramètres utilisateur (volume, souris).
// Classe statique : pas besoin d'autoload, accessible partout via ParametresJeu.X.
//
// Persistance : user://parametres.cfg (ConfigFile Godot).
// Application : AudioServer + propagation aux Players via l'événement Changes.
//
// Cycle :
//   1. GameState._EnterTree() → Charger() : lit le .cfg, applique.
//   2. PageOptions modifie via Definir(...) : applique + sauve + émet Changes.
//   3. Player s'abonne à Changes pour mettre à jour sensibilité/invert à chaud.
public static class ParametresJeu
{
    private const string CheminFichier = "user://parametres.cfg";

    // === Valeurs par défaut ===
    private const float VolumeLineaireDefaut  = 0.8f;    // 0..1 (≈ -2 dB)
    private const float SensibiliteDefaut     = 0.0025f; // rad/pixel (cf. Player.cs)
    private const bool  InvertYDefaut         = false;
    private const bool  InvertXDefaut         = false;
    private const float VitesseRotationDefaut = 2.5f;    // rad/s à fond (cf. Player.RotationSpeed)
    private const float FOVDefaut             = 75f;     // degrés — défaut Godot Camera3D
    private const float VitesseMarcheDefaut   = 5.0f;    // m/s (cf. Player.Speed)
    private const float MultiplicateurSprintDefaut = 2.0f; // (cf. Player.SprintMultiplier)
    private const bool  AfficherBoussoleDefaut = true;

    // === Bornes (utilisées par les sliders) ===
    public const float SensibiliteMin     = 0.0005f;
    public const float SensibiliteMax     = 0.01f;
    public const float VitesseRotationMin = 0.5f;
    public const float VitesseRotationMax = 5.0f;
    public const float FOVMin             = 60f;
    public const float FOVMax             = 100f;
    public const float VitesseMarcheMin   = 2.0f;
    public const float VitesseMarcheMax   = 8.0f;
    public const float MultiplicateurSprintMin = 1.0f;
    public const float MultiplicateurSprintMax = 3.0f;

    // === Valeurs courantes ===
    // Volume stocké en linéaire (0..1) ; converti en dB à l'application.
    // Plus intuitif pour un slider que des dB directs.
    public static float VolumeMasterLineaire { get; private set; } = VolumeLineaireDefaut;
    public static float SensibiliteSouris    { get; private set; } = SensibiliteDefaut;
    public static bool  InvertY              { get; private set; } = InvertYDefaut;
    public static bool  InvertX              { get; private set; } = InvertXDefaut;
    public static float VitesseRotation      { get; private set; } = VitesseRotationDefaut;
    public static float FOV                  { get; private set; } = FOVDefaut;
    public static float VitesseMarche        { get; private set; } = VitesseMarcheDefaut;
    public static float MultiplicateurSprint { get; private set; } = MultiplicateurSprintDefaut;
    public static bool  AfficherBoussole     { get; private set; } = AfficherBoussoleDefaut;

    // Meilleur temps de victoire (secondes). -1 = aucun record enregistré.
    // Persisté dans la section "records" du même .cfg pour rester simple.
    public static float MeilleurTempsVictoire { get; private set; } = -1f;

    // Détail du meilleur run : tableaux parallèles (id de salle ↔ durée en secondes).
    // Sérialisés comme strings séparées par '|' pour éviter les soucis de
    // marshalling Variant ↔ array C# entre versions de Godot.
    public static string[] MeilleurSallesIds    { get; private set; } = System.Array.Empty<string>();
    public static float[]  MeilleurSallesDurees { get; private set; } = System.Array.Empty<float>();

    // Émis après Definir(...) : permet au Player de relire la sensibilité à chaud
    // sans avoir à redémarrer la partie.
    public static event Action Changes;

    public static void Charger()
    {
        var cfg = new ConfigFile();
        var err = cfg.Load(CheminFichier);
        if (err == Error.Ok)
        {
            VolumeMasterLineaire = (float)(double)cfg.GetValue("audio",     "volume_master",    (double)VolumeLineaireDefaut);
            SensibiliteSouris    = (float)(double)cfg.GetValue("souris",    "sensibilite",      (double)SensibiliteDefaut);
            InvertY              = (bool) cfg.GetValue("souris",            "invert_y",         InvertYDefaut);
            InvertX              = (bool) cfg.GetValue("souris",            "invert_x",         InvertXDefaut);
            VitesseRotation      = (float)(double)cfg.GetValue("controles", "vitesse_rotation", (double)VitesseRotationDefaut);
            FOV                  = (float)(double)cfg.GetValue("camera",    "fov",              (double)FOVDefaut);
            VitesseMarche        = (float)(double)cfg.GetValue("deplacement", "vitesse_marche", (double)VitesseMarcheDefaut);
            MultiplicateurSprint = (float)(double)cfg.GetValue("deplacement", "sprint",         (double)MultiplicateurSprintDefaut);
            AfficherBoussole     = (bool) cfg.GetValue("hud",         "boussole",       AfficherBoussoleDefaut);
            MeilleurTempsVictoire = (float)(double)cfg.GetValue("records",  "meilleur_temps",   -1.0);
            string idsBruts    = (string)cfg.GetValue("records", "salles_ids",    "");
            string dureesBruts = (string)cfg.GetValue("records", "salles_durees", "");
            (MeilleurSallesIds, MeilleurSallesDurees) = DeserialiserDetails(idsBruts, dureesBruts);
            GD.Print($"[ParametresJeu] Chargés depuis {CheminFichier}");
        }
        else
        {
            GD.Print($"[ParametresJeu] Pas de fichier — valeurs par défaut.");
        }
        Appliquer();
    }

    public static void Definir(
        float volumeLineaire, float sensibilite, bool invertY,
        bool invertX, float vitesseRotation, float fov,
        float vitesseMarche, float multiplicateurSprint,
        bool afficherBoussole)
    {
        VolumeMasterLineaire = Mathf.Clamp(volumeLineaire, 0f, 1f);
        SensibiliteSouris    = Mathf.Clamp(sensibilite, SensibiliteMin, SensibiliteMax);
        InvertY              = invertY;
        InvertX              = invertX;
        VitesseRotation      = Mathf.Clamp(vitesseRotation, VitesseRotationMin, VitesseRotationMax);
        FOV                  = Mathf.Clamp(fov, FOVMin, FOVMax);
        VitesseMarche        = Mathf.Clamp(vitesseMarche, VitesseMarcheMin, VitesseMarcheMax);
        MultiplicateurSprint = Mathf.Clamp(multiplicateurSprint, MultiplicateurSprintMin, MultiplicateurSprintMax);
        AfficherBoussole     = afficherBoussole;

        Appliquer();
        Sauvegarder();
        Changes?.Invoke();
    }

    /// <summary>
    /// Enregistre un temps de victoire s'il améliore le record (ou si aucun record
    /// n'existe encore). Retourne true si le record a été battu.
    /// Si sallesIds/sallesDurees sont fournis, ils remplacent aussi le détail
    /// par salle du meilleur run.
    /// </summary>
    public static bool EnregistrerTempsVictoire(
        float secondes,
        System.Collections.Generic.IList<string> sallesIds = null,
        System.Collections.Generic.IList<float> sallesDurees = null)
    {
        if (secondes <= 0f) return false;
        if (MeilleurTempsVictoire > 0f && secondes >= MeilleurTempsVictoire)
            return false;
        MeilleurTempsVictoire = secondes;
        if (sallesIds != null && sallesDurees != null
            && sallesIds.Count == sallesDurees.Count)
        {
            MeilleurSallesIds    = new string[sallesIds.Count];
            MeilleurSallesDurees = new float[sallesDurees.Count];
            for (int i = 0; i < sallesIds.Count; i++)
            {
                MeilleurSallesIds[i]    = sallesIds[i];
                MeilleurSallesDurees[i] = sallesDurees[i];
            }
        }
        Sauvegarder();
        return true;
    }

    private static (string[] ids, float[] durees) DeserialiserDetails(string idsBruts, string dureesBruts)
    {
        if (string.IsNullOrEmpty(idsBruts) || string.IsNullOrEmpty(dureesBruts))
            return (System.Array.Empty<string>(), System.Array.Empty<float>());

        var ids = idsBruts.Split('|', System.StringSplitOptions.RemoveEmptyEntries);
        var dureesStr = dureesBruts.Split('|', System.StringSplitOptions.RemoveEmptyEntries);
        int n = System.Math.Min(ids.Length, dureesStr.Length);
        var durees = new float[n];
        for (int i = 0; i < n; i++)
            float.TryParse(dureesStr[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out durees[i]);

        if (ids.Length != n) System.Array.Resize(ref ids, n);
        return (ids, durees);
    }

    private static (string idsSerialise, string dureesSerialise) SerialiserDetails()
    {
        if (MeilleurSallesIds.Length == 0)
            return ("", "");
        var dureesStr = new string[MeilleurSallesDurees.Length];
        for (int i = 0; i < MeilleurSallesDurees.Length; i++)
            dureesStr[i] = MeilleurSallesDurees[i].ToString(
                "0.###", System.Globalization.CultureInfo.InvariantCulture);
        return (string.Join("|", MeilleurSallesIds), string.Join("|", dureesStr));
    }

    private static void Appliquer()
    {
        // Convertit le linéaire (0..1) en dB. Mathf.LinearToDb(0) = -∞ → clamp à -80.
        float db = VolumeMasterLineaire <= 0.0001f
            ? -80f
            : Mathf.LinearToDb(VolumeMasterLineaire);
        int idx = AudioServer.GetBusIndex("Master");
        if (idx >= 0)
            AudioServer.SetBusVolumeDb(idx, db);
    }

    private static void Sauvegarder()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio",     "volume_master",    (double)VolumeMasterLineaire);
        cfg.SetValue("souris",    "sensibilite",      (double)SensibiliteSouris);
        cfg.SetValue("souris",    "invert_y",         InvertY);
        cfg.SetValue("souris",    "invert_x",         InvertX);
        cfg.SetValue("controles", "vitesse_rotation", (double)VitesseRotation);
        cfg.SetValue("camera",    "fov",              (double)FOV);
        cfg.SetValue("deplacement", "vitesse_marche", (double)VitesseMarche);
        cfg.SetValue("deplacement", "sprint",         (double)MultiplicateurSprint);
        cfg.SetValue("hud",         "boussole",       AfficherBoussole);
        cfg.SetValue("records",   "meilleur_temps",   (double)MeilleurTempsVictoire);
        var (idsSerialise, dureesSerialise) = SerialiserDetails();
        cfg.SetValue("records",   "salles_ids",       idsSerialise);
        cfg.SetValue("records",   "salles_durees",    dureesSerialise);
        var err = cfg.Save(CheminFichier);
        if (err != Error.Ok)
            GD.PrintErr($"[ParametresJeu] Échec sauvegarde : {err}");
    }
}
