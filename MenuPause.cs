using Godot;

// Menu pause en jeu — overlay plein écran identique en style à PageOptions/PageCommandes.
// Instancié par PartieManager au front montant de l'action "pause" (Échap).
// Émet Reprendre ou MenuPrincipal selon l'action choisie ; pour Options,
// gère lui-même l'ouverture d'une PageOptions par-dessus.
public partial class MenuPause : Control
{
    [Signal] public delegate void ReprendreEventHandler();
    [Signal] public delegate void MenuPrincipalEventHandler();

    private static readonly Color CouleurFond   = new Color(0.025f, 0.035f, 0.055f, 0.97f);
    private static readonly Color CouleurAccent = new Color(0.55f, 0.93f, 1f, 1f);

    // Référence à la page (Options ou Commandes) ouverte par-dessus, le cas
    // échéant — sert à ignorer Échap au niveau du menu pause tant qu'une page
    // est active (sinon Échap fermerait les deux d'un coup).
    private Control pageOuverte;

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

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        vbox.AddThemeConstantOverride("separation", 16);
        center.AddChild(vbox);

        var titre = new Label
        {
            Text = "PAUSE",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titre.AddThemeFontSizeOverride("font_size", 56);
        titre.AddThemeColorOverride("font_color", CouleurAccent);
        vbox.AddChild(titre);

        vbox.AddChild(new HSeparator());

        var btnReprendre = CreerBouton("▶  REPRENDRE");
        btnReprendre.Pressed += OnReprendrePressed;
        vbox.AddChild(btnReprendre);

        var btnCommandes = CreerBouton("⌨  COMMANDES");
        btnCommandes.Pressed += OnCommandesPressed;
        vbox.AddChild(btnCommandes);

        var btnOptions = CreerBouton("⚙  OPTIONS");
        btnOptions.Pressed += OnOptionsPressed;
        vbox.AddChild(btnOptions);

        var btnMenu = CreerBouton("✕  MENU PRINCIPAL");
        btnMenu.Pressed += OnMenuPrincipalPressed;
        vbox.AddChild(btnMenu);

        btnReprendre.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Si une page (Options/Commandes) est ouverte par-dessus, elle gère Échap
        // (ne se ferme qu'elle-même). Sinon, Échap reprend la partie.
        if (pageOuverte != null) return;
        if (@event.IsActionPressed(InputBindings.Pause) || @event.IsActionPressed("ui_cancel"))
        {
            OnReprendrePressed();
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnReprendrePressed()
    {
        EmitSignal(SignalName.Reprendre);
    }

    private void OnCommandesPressed()
    {
        if (pageOuverte != null) return;
        var p = new PageCommandes();
        pageOuverte = p;
        AddChild(p);
        p.Ferme += () => { pageOuverte = null; };
    }

    private void OnOptionsPressed()
    {
        if (pageOuverte != null) return;
        var p = new PageOptions();
        pageOuverte = p;
        AddChild(p);
        p.Ferme += () => { pageOuverte = null; };
    }

    private void OnMenuPrincipalPressed()
    {
        EmitSignal(SignalName.MenuPrincipal);
    }

    private static Button CreerBouton(string texte)
    {
        var btn = new Button
        {
            Text = texte,
            CustomMinimumSize = new Vector2(340, 56),
        };
        btn.AddThemeFontSizeOverride("font_size", 22);
        return btn;
    }
}
