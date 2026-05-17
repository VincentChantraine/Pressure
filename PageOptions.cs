using Godot;

// Page Options : modale plein écran avec sliders volume / sensibilité souris + invert Y.
// Instanciée par MenuPrincipal au clic sur le bouton OPTIONS.
// Émet le signal Ferme quand le joueur clique RETOUR (ou ESC).
//
// UI construite intégralement en code pour éviter la duplication des StyleBox
// du menu principal — l'esthétique reste cohérente (couleurs alignées) sans
// fichier .tscn supplémentaire à maintenir.
public partial class PageOptions : Control
{
    [Signal] public delegate void FermeEventHandler();

    // Couleurs alignées sur celles de MenuPrincipal.tscn pour rester cohérent.
    private static readonly Color CouleurFond  = new Color(0.025f, 0.035f, 0.055f, 0.97f);
    private static readonly Color CouleurAccent = new Color(0.55f, 0.93f, 1f, 1f);
    private static readonly Color CouleurTexte  = new Color(0.85f, 0.97f, 1f, 1f);
    private static readonly Color CouleurLabel  = new Color(0.7f, 0.85f, 0.92f, 0.9f);

    private HSlider sliderVolume;
    private Label   valeurVolume;
    private HSlider sliderSensibilite;
    private Label   valeurSensibilite;
    private HSlider sliderVitesseRotation;
    private Label   valeurVitesseRotation;
    private HSlider sliderFOV;
    private Label   valeurFOV;
    private HSlider sliderVitesseMarche;
    private Label   valeurVitesseMarche;
    private HSlider sliderSprint;
    private Label   valeurSprint;
    private CheckButton checkInvertY;
    private CheckButton checkInvertX;
    private CheckButton checkAfficherBoussole;

    public override void _Ready()
    {
        // Couvre tout l'écran et bloque les clics qui passeraient derrière.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var fond = new ColorRect { Color = CouleurFond };
        fond.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(fond);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        vbox.AddThemeConstantOverride("separation", 18);
        center.AddChild(vbox);

        var titre = new Label
        {
            Text = "OPTIONS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        titre.AddThemeFontSizeOverride("font_size", 48);
        titre.AddThemeColorOverride("font_color", CouleurAccent);
        vbox.AddChild(titre);

        vbox.AddChild(new HSeparator());

        // --- Volume principal ---
        AjouterLigneSlider(vbox, "Volume", 0f, 1f, 0.01f,
                           ParametresJeu.VolumeMasterLineaire,
                           v => $"{Mathf.RoundToInt(v * 100)} %",
                           out sliderVolume, out valeurVolume);

        // --- Sensibilité souris ---
        AjouterLigneSlider(vbox, "Sensibilité souris",
                           ParametresJeu.SensibiliteMin, ParametresJeu.SensibiliteMax, 0.0001f,
                           ParametresJeu.SensibiliteSouris,
                           v => $"{v * 1000f:0.0}",
                           out sliderSensibilite, out valeurSensibilite);

        // --- Vitesse de rotation (A/E) ---
        AjouterLigneSlider(vbox, "Vitesse rotation (A/E)",
                           ParametresJeu.VitesseRotationMin, ParametresJeu.VitesseRotationMax, 0.1f,
                           ParametresJeu.VitesseRotation,
                           v => $"{v:0.0}",
                           out sliderVitesseRotation, out valeurVitesseRotation);

        // --- FOV / Champ de vision ---
        AjouterLigneSlider(vbox, "Champ de vision (FOV)",
                           ParametresJeu.FOVMin, ParametresJeu.FOVMax, 1f,
                           ParametresJeu.FOV,
                           v => $"{Mathf.RoundToInt(v)}°",
                           out sliderFOV, out valeurFOV);

        // --- Vitesse de marche ---
        AjouterLigneSlider(vbox, "Vitesse de marche",
                           ParametresJeu.VitesseMarcheMin, ParametresJeu.VitesseMarcheMax, 0.1f,
                           ParametresJeu.VitesseMarche,
                           v => $"{v:0.0} m/s",
                           out sliderVitesseMarche, out valeurVitesseMarche);

        // --- Multiplicateur de sprint ---
        AjouterLigneSlider(vbox, "Sprint (× marche)",
                           ParametresJeu.MultiplicateurSprintMin, ParametresJeu.MultiplicateurSprintMax, 0.1f,
                           ParametresJeu.MultiplicateurSprint,
                           v => $"×{v:0.0}",
                           out sliderSprint, out valeurSprint);

        // --- Invert Y / Invert X (CheckButton : style switch, plus lisible que CheckBox) ---
        checkInvertY = AjouterLigneToggle(vbox, "Inverser l'axe Y de la souris", ParametresJeu.InvertY);
        checkInvertX = AjouterLigneToggle(vbox, "Inverser l'axe X de la souris", ParametresJeu.InvertX);

        // --- Boussole : flèche directionnelle vers la salle non-validée la plus proche ---
        checkAfficherBoussole = AjouterLigneToggle(vbox, "Afficher la boussole d'objectif", ParametresJeu.AfficherBoussole);

        vbox.AddChild(new HSeparator());

        // --- Boutons Appliquer / Retour ---
        var hboxBtn = new HBoxContainer();
        hboxBtn.AddThemeConstantOverride("separation", 12);
        hboxBtn.Alignment = BoxContainer.AlignmentMode.Center;

        var btnRemap = CreerBouton("REMAP TOUCHES");
        btnRemap.Pressed += OnRemapPressed;
        hboxBtn.AddChild(btnRemap);

        var btnAppliquer = CreerBouton("APPLIQUER");
        btnAppliquer.Pressed += OnAppliquerPressed;
        hboxBtn.AddChild(btnAppliquer);

        var btnRetour = CreerBouton("RETOUR");
        btnRetour.Pressed += OnRetourPressed;
        hboxBtn.AddChild(btnRetour);

        vbox.AddChild(hboxBtn);

        btnAppliquer.GrabFocus();
    }

    // Page de remap ouverte par-dessus celle-ci : on garde la ref pour bloquer
    // la fermeture d'Options tant que Remap est ouverte (Échap retombe sur elle).
    private PageRemap pageRemapOuverte;

    private void OnRemapPressed()
    {
        if (pageRemapOuverte != null) return;
        pageRemapOuverte = new PageRemap();
        AddChild(pageRemapOuverte);
        pageRemapOuverte.Ferme += () => { pageRemapOuverte = null; };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputBindings.Pause) || @event.IsActionPressed("ui_cancel"))
        {
            OnRetourPressed();
            GetViewport().SetInputAsHandled();
        }
    }

    private void AjouterLigneSlider(
        VBoxContainer parent, string libelle,
        float min, float max, float step,
        float valeurInitiale,
        System.Func<float, string> formatValeur,
        out HSlider slider, out Label valeurLabel)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 32) };
        row.AddThemeConstantOverride("separation", 12);

        var label = new Label { Text = libelle, CustomMinimumSize = new Vector2(220, 0) };
        label.AddThemeColorOverride("font_color", CouleurLabel);
        row.AddChild(label);

        slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = valeurInitiale,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(slider);

        var valeur = new Label
        {
            Text = formatValeur(valeurInitiale),
            CustomMinimumSize = new Vector2(70, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        valeur.AddThemeColorOverride("font_color", CouleurTexte);
        row.AddChild(valeur);

        // Mise à jour live du label quand le slider bouge (mais on n'applique
        // qu'au clic APPLIQUER pour permettre d'annuler en cliquant RETOUR).
        slider.ValueChanged += v => valeur.Text = formatValeur((float)v);

        parent.AddChild(row);
        valeurLabel = valeur;
    }

    private CheckButton AjouterLigneToggle(VBoxContainer parent, string libelle, bool valeurInitiale)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 32) };
        row.AddThemeConstantOverride("separation", 12);

        var label = new Label { Text = libelle, CustomMinimumSize = new Vector2(280, 0) };
        label.AddThemeColorOverride("font_color", CouleurLabel);
        row.AddChild(label);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        var toggle = new CheckButton { ButtonPressed = valeurInitiale };
        row.AddChild(toggle);

        parent.AddChild(row);
        return toggle;
    }

    private Button CreerBouton(string texte)
    {
        var btn = new Button
        {
            Text = texte,
            CustomMinimumSize = new Vector2(160, 48),
        };
        btn.AddThemeFontSizeOverride("font_size", 18);
        return btn;
    }

    private void OnAppliquerPressed()
    {
        ParametresJeu.Definir(
            (float)sliderVolume.Value,
            (float)sliderSensibilite.Value,
            checkInvertY.ButtonPressed,
            checkInvertX.ButtonPressed,
            (float)sliderVitesseRotation.Value,
            (float)sliderFOV.Value,
            (float)sliderVitesseMarche.Value,
            (float)sliderSprint.Value,
            checkAfficherBoussole.ButtonPressed
        );
        OnRetourPressed();
    }

    private void OnRetourPressed()
    {
        EmitSignal(SignalName.Ferme);
        QueueFree();
    }
}
