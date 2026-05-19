using Godot;
using System.Collections.Generic;

// HUD principal de la partie — version adaptée à la scène réelle.
// Enfants utilisés (tous optionnels) :
// - Label      "Chrono"     → ex: "⏱ 03:42"
// - ColorRect  "NiveauEau"  → monte de 0 à 100% sur la durée totale
// - Label      "BarreLabel" → ex: "Progression : 58%  (3🚪 + 4🧱)"
//
// Référence externe :
// - ServoGameTimer → pilote le chrono. Si absent, fallback autonome basé sur DureeTotaleFallback.
public partial class ProgressionHud : CanvasLayer
{
	[Export] public NodePath ChronoLabelPath;
	[Export] public NodePath NiveauEauRectPath;
	[Export] public NodePath BarreLabelPath;
	[Export] public NodePath ServoGameTimerPath;

	// LED globale qui clignote vert (scan valide) / rouge (scan invalide).
	// Optionnel : si non renseigné, la LED est simplement ignorée.
	[Export] public NodePath LedColorRectPath;
	[Export] public float LedDureeFlash = 0.6f;

	// Messages éphémères affichés à droite de chaque carte au ramassage.
	[Export] public NodePath BadgeMessage1Path;
	[Export] public NodePath BadgeMessage2Path;
	[Export] public float BadgeMessageDuree = 3.5f;

	// Inventaire bas-gauche : deux slots permanents empilés verticalement,
	// un par badge ramassé. Chaque NodePath pointe vers un TextureRect.
	[Export] public NodePath BadgeIcone1Path;
	[Export] public NodePath BadgeIcone2Path;
	// Texture utilisée pour les icônes ; assignée dans l'inspecteur ou
	// chargée par fallback depuis res://IMG/Pickup Key Card.png.
	[Export] public Texture2D TextureBadge;

	[Export] public float DureeTotaleFallback = 300.0f;
	[Export] public float SeuilUrgence = 60.0f; // secondes restantes → pulse rouge

	[Export] public string[] PorteIdsNormales = new string[]
	{
		"porte_1", "porte_2", "porte_3", "porte_4", "porte_5", "porte_6"
	};

	// IDs des salles affichées dans le HUD, dans l'ORDRE de progression imposé.
	// Le numéro affiché est extrait de l'ID ("salle_3" -> 3), pas l'index.
	[Export] public string[] SalleIds = new string[]
	{
		"salle_1", "salle_3", "salle_4", "salle_5", "salle_2", "salle_6"
	};

	private Label chronoLabel;
	private ColorRect niveauEauRect;
	private Label barreLabel;
	private ServoGameTimer servoTimer;
	private ColorRect led;
	private Label badgeMessage1;
	private Label badgeMessage2;
	private float badgeMessage1Timer = 0f;
	private float badgeMessage2Timer = 0f;
	private TextureRect badgeIcone1;
	private TextureRect badgeIcone2;

	private HashSet<string> idsNormalesSet;

	private float elapsedFallback = 0f;
	private bool fallbackActif = false;
	private float clignoteAccum = 0f;
	private float ledTimer = 0f;
	private Color ledCouleur = new Color(0, 0, 0, 0);
	private ShaderMaterial waterMaterial;

	private bool hudEauMasque = false;

	public override void _Ready()
	{
		chronoLabel       = GetNodeOrNull<Label>(ChronoLabelPath);
		niveauEauRect     = GetNodeOrNull<ColorRect>(NiveauEauRectPath);
		barreLabel        = GetNodeOrNull<Label>(BarreLabelPath);
		servoTimer        = GetNodeOrNull<ServoGameTimer>(ServoGameTimerPath);
		led               = GetNodeOrNull<ColorRect>(LedColorRectPath);
		badgeMessage1     = GetNodeOrNull<Label>(BadgeMessage1Path);
		badgeMessage2     = GetNodeOrNull<Label>(BadgeMessage2Path);

		if (led != null) led.Color = new Color(0, 0, 0, 0);
		if (badgeMessage1 != null)
		{
			badgeMessage1.Visible = false;
			badgeMessage1.Modulate = new Color(1f, 1f, 1f, 0f);
		}
		if (badgeMessage2 != null)
		{
			badgeMessage2.Visible = false;
			badgeMessage2.Modulate = new Color(1f, 1f, 1f, 0f);
		}

		// --- Inventaire de cartes (deux slots permanents) ---
		badgeIcone1 = GetNodeOrNull<TextureRect>(BadgeIcone1Path);
		badgeIcone2 = GetNodeOrNull<TextureRect>(BadgeIcone2Path);
		// Fallback : si la texture n'est pas assignée dans l'inspecteur, on tente
		// res://IMG/Pickup Key Card.png.
		if (TextureBadge == null)
			TextureBadge = GD.Load<Texture2D>("res://IMG/Pickup Key Card.png");
		InitSlotBadge(badgeIcone1, GameState.Instance?.BadgeRamasse1 ?? false);
		InitSlotBadge(badgeIcone2, GameState.Instance?.BadgeRamasse2 ?? false);

		fallbackActif = (servoTimer == null);
		if (fallbackActif)
			GD.Print("[ProgressionHud] Pas de ServoGameTimer lié — timer autonome activé.");

		idsNormalesSet = new HashSet<string>(PorteIdsNormales);

		if (GameState.Instance != null)
		{
			GameState.Instance.ScanValide += OnScanValide;
			GameState.Instance.ScanInvalide += OnScanInvalide;
			GameState.Instance.PorteFinaleDebloquee += OnPorteFinaleDebloquee;
			GameState.Instance.JoueurEntreeBT01 += OnJoueurEntreeBT01;
			GameState.Instance.BadgeRamasse += OnBadgeRamasse;
		}

		// Le ColorRect couvre tout l'écran ; le shader gère lui-même la zone eau.
		if (niveauEauRect != null)
		{
			niveauEauRect.AnchorLeft   = 0.0f;
			niveauEauRect.AnchorRight  = 1.0f;
			niveauEauRect.AnchorTop    = 0.0f;
			niveauEauRect.AnchorBottom = 1.0f;
			niveauEauRect.OffsetLeft   = 0;
			niveauEauRect.OffsetRight  = 0;
			niveauEauRect.OffsetTop    = 0;
			niveauEauRect.OffsetBottom = 0;
			niveauEauRect.Color        = new Color(0, 0, 0, 0); // transparent par défaut

			var shader = GD.Load<Shader>("res://WaterHUD.gdshader");
			if (shader != null)
			{
				waterMaterial = new ShaderMaterial { Shader = shader };
				niveauEauRect.Material = waterMaterial;
			}
			else
			{
				GD.PrintErr("[ProgressionHud] WaterHUD.gdshader introuvable — shader eau désactivé.");
				// Repli : retrouver l'ancien comportement (AnchorTop dynamique + couleur unie)
				niveauEauRect.AnchorTop = 1.0f;
			}
		}

		RafraichirBarreLabel();
	}

	private void InitSlotBadge(TextureRect slot, bool ramasse)
	{
		if (slot == null) return;
		if (TextureBadge != null) slot.Texture = TextureBadge;
		slot.Visible = ramasse;
		slot.Modulate = new Color(1f, 1f, 1f, ramasse ? 1f : 0f);
	}

	public override void _ExitTree()
	{
		// Désabonnement : sinon GameState (autoload, survit aux changements de
		// scène) garde des callbacks sur un HUD dont les nodes natifs sont déjà
		// freed → ObjectDisposedException sur TextureRect/Label/ColorRect.
		if (GameState.Instance != null)
		{
			GameState.Instance.ScanValide -= OnScanValide;
			GameState.Instance.ScanInvalide -= OnScanInvalide;
			GameState.Instance.PorteFinaleDebloquee -= OnPorteFinaleDebloquee;
			GameState.Instance.JoueurEntreeBT01 -= OnJoueurEntreeBT01;
			GameState.Instance.BadgeRamasse -= OnBadgeRamasse;
		}
	}

	public override void _Process(double delta)
	{
		RafraichirBarreLabel();
		RafraichirChronoEtEau((float)delta);
		RafraichirLed((float)delta);
		RafraichirBadgeMessage((float)delta);
	}

	// =========================================================================
	// Message ramassage badge (bas-gauche, fade-out)
	// =========================================================================
	private void RafraichirBadgeMessage(float delta)
	{
		// Chaque message a son propre timer indépendant.
		FadeUnMessage(badgeMessage1, ref badgeMessage1Timer, delta);
		FadeUnMessage(badgeMessage2, ref badgeMessage2Timer, delta);
	}

	private void FadeUnMessage(Label label, ref float timer, float delta)
	{
		if (label == null || timer <= 0f) return;
		timer -= delta;
		float t = Mathf.Clamp(timer / Mathf.Max(0.001f, BadgeMessageDuree), 0f, 1f);
		float alpha = t > 0.66f ? 1f : t / 0.66f;
		label.Modulate = new Color(1f, 1f, 1f, alpha);
		if (timer <= 0f)
		{
			label.Visible = false;
			label.Modulate = new Color(1f, 1f, 1f, 0f);
		}
	}

	private void OnBadgeRamasse(int numero)
	{
		if (hudEauMasque) return;

		// Slot d'inventaire correspondant + petit "pop" d'apparition.
		TextureRect slot = numero == 1 ? badgeIcone1 : (numero == 2 ? badgeIcone2 : null);
		if (slot != null)
		{
			slot.Visible = true;
			slot.Modulate = new Color(1f, 1f, 1f, 1f);
			slot.PivotOffset = slot.Size * 0.5f;
			slot.Scale = Vector2.One * 1.3f;
			var tween = CreateTween();
			tween.TweenProperty(slot, "scale", Vector2.One, 0.25f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out);
		}

		// Message texte associé à la bonne carte (s'auto-efface).
		Label msg = numero == 1 ? badgeMessage1 : (numero == 2 ? badgeMessage2 : null);
		if (msg != null)
		{
			msg.Text = $"Badge {numero} ramassé";
			msg.Visible = true;
			msg.Modulate = new Color(1f, 1f, 1f, 1f);
		}
		if (numero == 1) badgeMessage1Timer = BadgeMessageDuree;
		else if (numero == 2) badgeMessage2Timer = BadgeMessageDuree;
	}

	// =========================================================================
	// LED globale (flash vert/rouge sur scan badge)
	// =========================================================================
	private void RafraichirLed(float delta)
	{
		if (!GodotObject.IsInstanceValid(led) || ledTimer <= 0f) return;

		ledTimer -= delta;
		float t = Mathf.Clamp(ledTimer / Mathf.Max(0.001f, LedDureeFlash), 0f, 1f);
		Color c = ledCouleur;
		c.A = t * 0.6f;
		led.Color = c;

		if (ledTimer <= 0f)
			led.Color = new Color(0, 0, 0, 0);
	}

	private void DeclencherFlashLed(Color couleur)
	{
		// IsInstanceValid : le signal peut arriver après que le HUD a été freed
		// (changement de scène, fin de partie) — led.Color toucherait un wrapper
		// C# encore vivant mais dont l'objet natif est disposé.
		if (!GodotObject.IsInstanceValid(led)) return;
		ledCouleur = couleur;
		ledTimer = LedDureeFlash;
		led.Color = new Color(couleur.R, couleur.G, couleur.B, 0.6f);
	}

	// =========================================================================
	// Calcul de progression (portes normales + salles visitées)
	// =========================================================================
	private int CompterPortesOuvertes()
	{
		if (GameState.Instance == null) return 0;
		int n = 0;
		foreach (var id in GameState.Instance.PortesDeverrouilles)
			if (idsNormalesSet.Contains(id)) n++;
		return n;
	}

	private void RafraichirBarreLabel()
	{
		if (hudEauMasque || barreLabel == null || GameState.Instance == null) return;

		var sb = new System.Text.StringBuilder();
		bool prochaineMarquee = false;
		for (int i = 0; i < SalleIds.Length; i++)
		{
			string id = SalleIds[i];
			bool faite = GameState.Instance.SallesVisitees.Contains(id);
			if (i > 0) sb.Append(" → ");

			string numero = ExtraireNumero(id);
			string marqueur;
			if (faite)
				marqueur = "✅";
			else if (!prochaineMarquee)
			{
				marqueur = "▶";
				prochaineMarquee = true;
			}
			else
				marqueur = "🔒";

			sb.Append($"S{numero}{marqueur}");
		}

		if (GameState.Instance.PorteFinalePeutEtreDeverrouille())
			sb.Append(" → SORTIE ✅");
		else
			sb.Append(" → SORTIE 🔒");

		barreLabel.Text = sb.ToString();
	}

	private static string ExtraireNumero(string salleId)
	{
		int sep = salleId.LastIndexOf('_');
		return sep >= 0 && sep < salleId.Length - 1 ? salleId.Substring(sep + 1) : salleId;
	}

	// =========================================================================
	// Chrono + niveau d'eau
	// =========================================================================
	private void RafraichirChronoEtEau(float delta)
	{
		if (hudEauMasque) return;
		float elapsed;
		float total;
		bool demarre;
		bool fini;

		if (!fallbackActif && servoTimer != null)
		{
			elapsed = servoTimer.GetElapsed();
			total   = servoTimer.DureeTotale;
			demarre = servoTimer.EstDemarre();
			fini    = servoTimer.EstFini();
		}
		else
		{
			elapsedFallback += delta;
			elapsed = elapsedFallback;
			total   = DureeTotaleFallback;
			demarre = true;
			fini    = (elapsed >= total);
		}

		if (!demarre)
		{
			if (chronoLabel != null) chronoLabel.Text = "⏱ --:--";
			return;
		}

		float restant = Mathf.Max(0f, total - elapsed);
		float t = Mathf.Clamp(elapsed / total, 0f, 1f);

		// --- Label Chrono + pulse dramatique ---
		if (chronoLabel != null)
		{
			int totalSec = Mathf.FloorToInt(restant);
			int mm = totalSec / 60;
			int ss = totalSec % 60;
			chronoLabel.Text = $"⏱ {mm:D2}:{ss:D2}";

			if (fini)
			{
				chronoLabel.Text = "⏱ 00:00 — TEMPS ÉCOULÉ";
				chronoLabel.Modulate = new Color(1f, 0f, 0f, 1f);
				chronoLabel.Scale = new Vector2(1.2f, 1.2f);
				chronoLabel.PivotOffset = chronoLabel.Size * 0.5f;
			}
			else if (restant <= SeuilUrgence)
			{
				// Dernière minute : pulse + couleur rouge animée
				clignoteAccum += delta;

				float freq = Mathf.Lerp(2f, 5f, 1f - (restant / SeuilUrgence));
				float phase = Mathf.Sin(clignoteAccum * Mathf.Tau * freq);

				float alpha   = 0.5f + 0.5f * Mathf.Abs(phase);
				float rougeur = 0.2f + 0.8f * Mathf.Abs(phase);
				chronoLabel.Modulate = new Color(1f, 1f - rougeur, 1f - rougeur, alpha);

				float scaleK = 1f + 0.15f * Mathf.Abs(phase);
				chronoLabel.Scale = new Vector2(scaleK, scaleK);
				chronoLabel.PivotOffset = chronoLabel.Size * 0.5f;
			}
			else
			{
				chronoLabel.Modulate = Colors.White;
				chronoLabel.Scale = Vector2.One;
			}
		}

		// --- Niveau d'eau : shader avec vagues animées (ou repli rectangle simple) ---
		if (waterMaterial != null)
		{
			waterMaterial.SetShaderParameter("water_level", t);
			waterMaterial.SetShaderParameter("time_offset", elapsed);
		}
		else if (niveauEauRect != null)
		{
			// Repli sans shader : ancien comportement rectangle
			niveauEauRect.AnchorTop = 1.0f - t;
			niveauEauRect.Color = t < 0.6f
				? new Color(0.2f, 0.5f, 0.9f, 0.45f)
				: new Color(Mathf.Lerp(0.2f, 0.7f, (t - 0.6f) / 0.4f), 0.1f, 0.5f, 0.6f);
		}
	}

	private void OnJoueurEntreeBT01()
	{
		hudEauMasque = true;
		Visible = false;
	}

	private void OnScanValide(string porteId)
	{
		RafraichirBarreLabel();
		DeclencherFlashLed(new Color(0f, 1f, 0f));
	}

	private void OnScanInvalide(string raison)
	{
		DeclencherFlashLed(new Color(1f, 0f, 0f));
	}

	private void OnPorteFinaleDebloquee()
	{
		RafraichirBarreLabel();
	}
}
