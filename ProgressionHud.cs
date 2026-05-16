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

	private HashSet<string> idsNormalesSet;

	private float elapsedFallback = 0f;
	private bool fallbackActif = false;
	private float clignoteAccum = 0f;
	private float ledTimer = 0f;
	private Color ledCouleur = new Color(0, 0, 0, 0);

	public override void _Ready()
	{
		chronoLabel   = GetNodeOrNull<Label>(ChronoLabelPath);
		niveauEauRect = GetNodeOrNull<ColorRect>(NiveauEauRectPath);
		barreLabel    = GetNodeOrNull<Label>(BarreLabelPath);
		servoTimer    = GetNodeOrNull<ServoGameTimer>(ServoGameTimerPath);
		led           = GetNodeOrNull<ColorRect>(LedColorRectPath);

		if (led != null) led.Color = new Color(0, 0, 0, 0);

		fallbackActif = (servoTimer == null);
		if (fallbackActif)
			GD.Print("[ProgressionHud] Pas de ServoGameTimer lié — timer autonome activé.");

		idsNormalesSet = new HashSet<string>(PorteIdsNormales);

		if (GameState.Instance != null)
		{
			GameState.Instance.ScanValide += OnScanValide;
			GameState.Instance.ScanInvalide += OnScanInvalide;
			GameState.Instance.PorteFinaleDebloquee += OnPorteFinaleDebloquee;
		}

		// Le ColorRect "NiveauEau" remplit la largeur écran et monte depuis le bas.
		if (niveauEauRect != null)
		{
			niveauEauRect.AnchorLeft   = 0.0f;
			niveauEauRect.AnchorRight  = 1.0f;
			niveauEauRect.AnchorTop    = 1.0f;
			niveauEauRect.AnchorBottom = 1.0f;
			niveauEauRect.OffsetLeft   = 0;
			niveauEauRect.OffsetRight  = 0;
			niveauEauRect.OffsetTop    = 0;
			niveauEauRect.OffsetBottom = 0;
		}

		RafraichirBarreLabel();
	}

	public override void _Process(double delta)
	{
		RafraichirBarreLabel();
		RafraichirChronoEtEau((float)delta);
		RafraichirLed((float)delta);
	}

	// =========================================================================
	// LED globale (flash vert/rouge sur scan badge)
	// =========================================================================
	private void RafraichirLed(float delta)
	{
		if (led == null || ledTimer <= 0f) return;

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
		if (led == null) return;
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
		if (barreLabel == null || GameState.Instance == null) return;

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

		// --- Niveau d'eau : monte de bas en haut ---
		if (niveauEauRect != null)
		{
			niveauEauRect.AnchorTop = 1.0f - t;

			Color couleur;
			if (t < 0.6f)
				couleur = new Color(0.2f, 0.5f, 0.9f, 0.45f);
			else if (t < 0.9f)
			{
				float k = (t - 0.6f) / 0.3f;
				couleur = new Color(
					Mathf.Lerp(0.2f, 0.4f, k),
					Mathf.Lerp(0.5f, 0.2f, k),
					Mathf.Lerp(0.9f, 0.7f, k),
					0.55f
				);
			}
			else
			{
				float k = (t - 0.9f) / 0.1f;
				couleur = new Color(
					Mathf.Lerp(0.4f, 0.7f, k),
					Mathf.Lerp(0.2f, 0.1f, k),
					Mathf.Lerp(0.7f, 0.2f, k),
					0.65f
				);
			}
			niveauEauRect.Color = couleur;
		}
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
