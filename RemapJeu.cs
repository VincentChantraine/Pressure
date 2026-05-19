using Godot;
using System.Collections.Generic;

// Persistance et application des remaps de touches utilisateur.
// Sœur de ParametresJeu : .cfg séparé pour ne pas mélanger les domaines.
//
// Cycle :
//   1. GameState._EnterTree() → InputBindings.EnregistrerActionsParDefaut()
//      pose les défauts (W/A/Q/E/Shift/clic gauche/…).
//   2. RemapJeu.Charger() écrase ces défauts par les overrides utilisateur.
//   3. PageRemap.Definir(action, evt) modifie un binding à la volée +
//      sauvegarde dans user://remap.cfg.
//
// Sérialisation : chaque event est encodé en string compacte —
//   key:<physicalKeycodeInt>   (ex: "key:65" pour A physique)
//   mouse:<buttonIndexInt>     (ex: "mouse:1" pour Left)
// Format volontairement minimaliste : pas de modifiers/joypad pour l'instant,
// le jeu n'en utilise pas. Facile à étendre plus tard.
public static class RemapJeu
{
	private const string CheminFichier = "user://remap.cfg";
	private const string SectionBindings = "bindings";

	// Liste des actions remappables (= toutes celles définies dans InputBindings).
	// Centralisée ici pour que la PageRemap puisse itérer dessus dans l'ordre choisi.
	public static readonly (string Action, string Libelle)[] ActionsRemappables = new[]
	{
		(InputBindings.Avancer,        "Avancer"),
		(InputBindings.Reculer,        "Reculer"),
		(InputBindings.Gauche,         "Pas à gauche"),
		(InputBindings.Droite,         "Pas à droite"),
		(InputBindings.Sprint,         "Sprinter"),
		(InputBindings.RotationGauche, "Levier ←"),
		(InputBindings.RotationDroite, "Levier →"),
		(InputBindings.Interagir,      "Interagir"),
		(InputBindings.LampeTorche,    "Lampe torche"),
		(InputBindings.Lift,           "Monter (vol)"),
		(InputBindings.EncodeurGauche, "Coffre : molette ←"),
		(InputBindings.EncodeurDroite, "Coffre : molette →"),
		(InputBindings.Valider,        "Coffre : valider"),
		(InputBindings.Badge1,         "Badge 1"),
		(InputBindings.Badge2,         "Badge 2"),
		(InputBindings.Pause,          "Pause"),
	};

	// Cache mémoire des overrides en cours, pour ne pas re-lire le .cfg à chaque modif.
	private static readonly Dictionary<string, string> overrides = new();

	public static event System.Action Changes;

	public static void Charger()
	{
		var cfg = new ConfigFile();
		var err = cfg.Load(CheminFichier);
		if (err != Error.Ok) return;

		overrides.Clear();
		foreach (var (action, _) in ActionsRemappables)
		{
			string encoded = (string)cfg.GetValue(SectionBindings, action, "");
			if (string.IsNullOrEmpty(encoded)) continue;

			var evt = Decoder(encoded);
			if (evt == null) continue;

			overrides[action] = encoded;
			AppliquerInputMap(action, evt);
		}
		GD.Print($"[RemapJeu] {overrides.Count} bindings personnalisés chargés.");
	}

	/// <summary>
	/// Remplace le binding d'une action et sauvegarde. evt doit être un
	/// InputEventKey ou InputEventMouseButton (les autres sont rejetés).
	/// </summary>
	public static bool Definir(string action, InputEvent evt)
	{
		string encoded = Encoder(evt);
		if (encoded == null) return false;

		AppliquerInputMap(action, evt);
		overrides[action] = encoded;
		Sauvegarder();
		Changes?.Invoke();
		return true;
	}

	/// <summary>
	/// Réinitialise toutes les actions à leur binding par défaut.
	/// </summary>
	public static void ReinitialiserTout()
	{
		overrides.Clear();
		InputBindings.EnregistrerActionsParDefaut();
		Sauvegarder();
		Changes?.Invoke();
		GD.Print("[RemapJeu] Bindings réinitialisés aux valeurs par défaut.");
	}

	/// <summary>
	/// Représentation lisible du binding courant pour une action ("Z", "Clic gauche"...).
	/// </summary>
	public static string Libelle(string action)
	{
		if (!InputMap.HasAction(action)) return "—";
		foreach (var evt in InputMap.ActionGetEvents(action))
		{
			string nom = NomEvenement(evt);
			if (!string.IsNullOrEmpty(nom)) return nom;
		}
		return "—";
	}

	private static void AppliquerInputMap(string action, InputEvent evt)
	{
		if (!InputMap.HasAction(action))
			InputMap.AddAction(action);
		InputMap.ActionEraseEvents(action);
		InputMap.ActionAddEvent(action, evt);
	}

	private static void Sauvegarder()
	{
		var cfg = new ConfigFile();
		foreach (var (action, encoded) in overrides)
			cfg.SetValue(SectionBindings, action, encoded);
		var err = cfg.Save(CheminFichier);
		if (err != Error.Ok)
			GD.PrintErr($"[RemapJeu] Échec sauvegarde : {err}");
	}

	private static string Encoder(InputEvent evt) => evt switch
	{
		InputEventKey k         => $"key:{(int)(k.PhysicalKeycode != 0 ? k.PhysicalKeycode : k.Keycode)}",
		InputEventMouseButton m => $"mouse:{(int)m.ButtonIndex}",
		_ => null,
	};

	private static InputEvent Decoder(string s)
	{
		if (string.IsNullOrEmpty(s)) return null;
		int sep = s.IndexOf(':');
		if (sep <= 0) return null;
		string type = s.Substring(0, sep);
		if (!int.TryParse(s.Substring(sep + 1), out int val)) return null;
		return type switch
		{
			"key"   => new InputEventKey { PhysicalKeycode = (Key)val },
			"mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)val },
			_ => null,
		};
	}

	// Reprend (en plus simple) la logique d'affichage de PageCommandes —
	// duplication minime acceptée pour ne pas créer de dépendance circulaire
	// (PageCommandes connaîtrait RemapJeu et inversement).
	private static string NomEvenement(InputEvent evt) => evt switch
	{
		InputEventKey k         => NomTouche(k.PhysicalKeycode != 0 ? k.PhysicalKeycode : k.Keycode),
		InputEventMouseButton m => NomBoutonSouris(m.ButtonIndex),
		_ => evt.AsText(),
	};

	private static string NomTouche(Key key) => key switch
	{
		Key.W      => "Z",
		Key.A      => "Q",
		Key.Q      => "A",
		Key.S      => "S",
		Key.D      => "D",
		Key.E      => "E",
		Key.F      => "F",
		Key.R      => "R",
		Key.Shift  => "Maj",
		Key.Ctrl   => "Ctrl",
		Key.Alt    => "Alt",
		Key.Space  => "Espace",
		Key.Enter  => "Entrée",
		Key.Escape => "Échap",
		Key.Tab    => "Tab",
		Key.Left   => "←",
		Key.Right  => "→",
		Key.Up     => "↑",
		Key.Down   => "↓",
		_ => OS.GetKeycodeString(key),
	};

	private static string NomBoutonSouris(MouseButton b) => b switch
	{
		MouseButton.Left   => "Clic gauche",
		MouseButton.Right  => "Clic droit",
		MouseButton.Middle => "Clic milieu",
		MouseButton.WheelUp   => "Molette ↑",
		MouseButton.WheelDown => "Molette ↓",
		_ => $"Souris {(int)b}",
	};
}
