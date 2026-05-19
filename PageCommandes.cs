using Godot;
using System.Collections.Generic;
using System.Text;

// Page Commandes : modale plein écran qui liste les actions et leurs touches actuelles,
// lues directement depuis InputMap (pas de duplication / risque de désynchronisation).
//
// Mapping AZERTY visible : pour les touches dont le PhysicalKeycode ne correspond
// pas au caractère AZERTY (W/A/Q), on affiche le label AZERTY attendu.
public partial class PageCommandes : Control
{
    [Signal] public delegate void FermeEventHandler();

    private static readonly Color CouleurFond     = new Color(0.025f, 0.035f, 0.055f, 0.97f);
    private static readonly Color CouleurAccent   = new Color(0.55f, 0.93f, 1f, 1f);
    private static readonly Color CouleurAction   = new Color(0.85f, 0.97f, 1f, 1f);
    private static readonly Color CouleurTouche   = new Color(0.95f, 0.99f, 1f, 1f);
    private static readonly Color CouleurCategorie = new Color(0.31f, 0.78f, 0.88f, 0.8f);

    // Groupement éditorial : (catégorie, [(action InputMap, libellé, touche hardcodée)])
    // - Si Action est renseignée, la touche est lue depuis InputMap.
    // - Sinon, on affiche TouchesFallback (utile pour les actions Godot natives
    //   non re-déclarées chez nous, ex : flèches du labyrinthe sur ui_left/right/up/down).
    private static readonly (string Categorie, (string Action, string Libelle, string TouchesFallback)[] Lignes)[] Sections = new[]
    {
        ("DÉPLACEMENT", new[] {
            (InputBindings.Avancer, "Avancer",      (string)null),
            (InputBindings.Reculer, "Reculer",      (string)null),
            (InputBindings.Gauche,  "Pas à gauche", (string)null),
            (InputBindings.Droite,  "Pas à droite", (string)null),
            (InputBindings.Sprint,  "Sprinter",     (string)null),
            ((string)null,          "Regarder",     "Souris"),
        }),
        ("INTERACTION", new[] {
            (InputBindings.Interagir,   "Interagir / Ouvrir porte", (string)null),
            (InputBindings.LampeTorche, "Lampe torche",             (string)null),
            (InputBindings.Lift,        "Monter (vol)",             (string)null),
        }),
        ("LEVIER", new[] {
            (InputBindings.RotationGauche, "Tirer le levier à gauche", (string)null),
            (InputBindings.RotationDroite, "Tirer le levier à droite", (string)null),
        }),
        ("COFFRE", new[] {
            (InputBindings.EncodeurGauche, "Tourner la molette à gauche", (string)null),
            (InputBindings.EncodeurDroite, "Tourner la molette à droite", (string)null),
            (InputBindings.Valider,        "Valider le chiffre",          (string)null),
        }),
        ("LABYRINTHE", new[] {
            ((string)null, "Déplacer", "←  →  ↑  ↓"),
        }),
        ("BADGES RFID", new[] {
            (InputBindings.Badge1, "Présenter badge 1", (string)null),
            (InputBindings.Badge2, "Présenter badge 2", (string)null),
        }),
        ("SYSTÈME", new[] {
            (InputBindings.Pause, "Pause / Libérer la souris", (string)null),
        }),
    };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var fond = new ColorRect { Color = CouleurFond };
        fond.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(fond);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        vbox.AddThemeConstantOverride("separation", 10);
        center.AddChild(vbox);

        var titre = new Label
        {
            Text = "COMMANDES",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titre.AddThemeFontSizeOverride("font_size", 48);
        titre.AddThemeColorOverride("font_color", CouleurAccent);
        vbox.AddChild(titre);

        vbox.AddChild(new HSeparator());

        foreach (var (categorie, lignes) in Sections)
        {
            var labelCat = new Label
            {
                Text = categorie,
                CustomMinimumSize = new Vector2(0, 28),
            };
            labelCat.AddThemeFontSizeOverride("font_size", 18);
            labelCat.AddThemeColorOverride("font_color", CouleurCategorie);
            vbox.AddChild(labelCat);

            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 28);
            grid.AddThemeConstantOverride("v_separation", 4);
            vbox.AddChild(grid);

            foreach (var (action, libelle, touchesFallback) in lignes)
            {
                var lAction = new Label { Text = libelle, CustomMinimumSize = new Vector2(360, 0) };
                lAction.AddThemeColorOverride("font_color", CouleurAction);
                grid.AddChild(lAction);

                string texteTouches = !string.IsNullOrEmpty(action)
                    ? LireTouches(action)
                    : (touchesFallback ?? "—");
                var lTouche = new Label { Text = texteTouches };
                lTouche.AddThemeColorOverride("font_color", CouleurTouche);
                lTouche.AddThemeFontSizeOverride("font_size", 16);
                grid.AddChild(lTouche);
            }
        }

        vbox.AddChild(new HSeparator());

        var btnRetour = new Button { Text = "RETOUR", CustomMinimumSize = new Vector2(160, 48) };
        btnRetour.AddThemeFontSizeOverride("font_size", 18);
        btnRetour.Pressed += OnRetourPressed;

        var hboxBtn = new HBoxContainer();
        hboxBtn.Alignment = BoxContainer.AlignmentMode.Center;
        hboxBtn.AddChild(btnRetour);
        vbox.AddChild(hboxBtn);

        btnRetour.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputBindings.Pause) || @event.IsActionPressed("ui_cancel"))
        {
            OnRetourPressed();
            GetViewport().SetInputAsHandled();
        }
    }

    private static string LireTouches(string action)
    {
        if (!InputMap.HasAction(action)) return "—";

        var noms = new List<string>();
        foreach (var evt in InputMap.ActionGetEvents(action))
        {
            string nom = NomEvenement(evt);
            if (!string.IsNullOrEmpty(nom)) noms.Add(nom);
        }

        if (noms.Count == 0) return "—";

        var sb = new StringBuilder();
        for (int i = 0; i < noms.Count; i++)
        {
            if (i > 0) sb.Append("  /  ");
            sb.Append(noms[i]);
        }
        return sb.ToString();
    }

    private static string NomEvenement(InputEvent evt) => evt switch
    {
        InputEventKey k         => NomTouche(k.PhysicalKeycode != 0 ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => NomBoutonSouris(m.ButtonIndex),
        _ => evt.AsText(),
    };

    // Mapping AZERTY-friendly : la touche W (physique) est libellée "Z" car
    // c'est ce que voit l'utilisateur français. Mapping inspiré du clavier FR standard.
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
        Key.Key1   => "1",
        Key.Key2   => "2",
        Key.Key3   => "3",
        Key.Key4   => "4",
        Key.Key5   => "5",
        Key.Key6   => "6",
        Key.Key7   => "7",
        Key.Key8   => "8",
        Key.Key9   => "9",
        Key.Key0   => "0",
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

    private void OnRetourPressed()
    {
        EmitSignal(SignalName.Ferme);
        QueueFree();
    }
}
