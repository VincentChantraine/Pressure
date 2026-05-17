using Godot;

// Définit toutes les actions InputMap du jeu en code (plutôt que dans project.godot)
// pour faciliter le remap utilisateur futur et garder les bindings versionnés ici.
//
// Les touches sont enregistrées via `PhysicalKeycode` : la position physique de la
// touche sur le clavier, indépendante du layout. Concrètement :
//   - `Key.W` (physique) = touche Z en AZERTY, W en QWERTY
//   - `Key.A` (physique) = touche Q en AZERTY, A en QWERTY
//   - `Key.Q` (physique) = touche A en AZERTY, Q en QWERTY
// → ZQSD en AZERTY et WASD en QWERTY fonctionnent sans config.
//
// Appelé depuis GameState._EnterTree() pour que les actions existent
// dès le premier frame.
public static class InputBindings
{
    // === Déplacement ===
    public const string Avancer = "avancer";
    public const string Reculer = "reculer";
    public const string Gauche  = "gauche";
    public const string Droite  = "droite";

    // === Rotation (ancien slider Arduino) ===
    public const string RotationGauche = "rotation_gauche";
    public const string RotationDroite = "rotation_droite";

    // === Actions ===
    public const string Sprint       = "sprint";
    public const string Interagir    = "interagir";     // clic gauche : ouvrir porte, etc.
    public const string LampeTorche  = "lampe_torche";  // clic droit (sera toggle en phase 2)
    public const string Lift         = "lift";          // ancien ultrason : monter
    public const string Pause        = "pause";

    // === Coffre (encodeur) ===
    public const string EncodeurGauche = "encodeur_gauche";
    public const string EncodeurDroite = "encodeur_droite";
    public const string Valider        = "valider";  // bouton encodeur

    // === RFID ===
    public const string Badge1 = "badge_1";
    public const string Badge2 = "badge_2";

    public static void EnregistrerActionsParDefaut()
    {
        BindKey(Avancer, Key.W);
        BindKey(Reculer, Key.S);
        BindKey(Gauche,  Key.A);
        BindKey(Droite,  Key.D);

        BindKey(RotationGauche, Key.Q);
        BindKey(RotationDroite, Key.E);

        BindKey(Sprint, Key.Shift);
        BindKey(Lift,   Key.Space);
        BindKey(Pause,  Key.Escape);

        BindMouse(Interagir,   MouseButton.Left);
        BindMouse(LampeTorche, MouseButton.Right);

        BindKey(EncodeurGauche, Key.Left);
        BindKey(EncodeurDroite, Key.Right);
        BindKey(Valider,        Key.Enter);

        BindKey(Badge1, Key.Key1);
        BindKey(Badge2, Key.Key2);
    }

    private static void BindKey(string action, Key physicalKey, float deadzone = 0.5f)
    {
        EnsureAction(action, deadzone);
        var evt = new InputEventKey { PhysicalKeycode = physicalKey };
        InputMap.ActionAddEvent(action, evt);
    }

    private static void BindMouse(string action, MouseButton button, float deadzone = 0.5f)
    {
        EnsureAction(action, deadzone);
        var evt = new InputEventMouseButton { ButtonIndex = button };
        InputMap.ActionAddEvent(action, evt);
    }

    private static void EnsureAction(string action, float deadzone)
    {
        if (InputMap.HasAction(action))
        {
            // Au reset de scène les actions persistent — on vide les events pour
            // repartir propre (sinon double-binding si on remap plus tard).
            InputMap.ActionEraseEvents(action);
        }
        else
        {
            InputMap.AddAction(action, deadzone);
        }
    }
}
