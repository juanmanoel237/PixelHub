using UnityEngine;
using Laps.Core;

/// <summary>
/// Panneau OnGUI P1 : édition des IP contrôleurs + rechargement config routeur.
/// Touche I (pas F6) pour afficher / masquer — compatible Mac / PC.
/// </summary>
public class RouterConfigPanel : MonoBehaviour
{
    private ConfigManager _configManager;
    private bool _show;
    private string[] _ipFields;
    private string _widthField = "128";
    private string _heightField = "128";
    private string _feedback = "";
    private float _feedbackUntil;

    private void Awake()
    {
        _configManager = FindObjectOfType<ConfigManager>();
        SyncFieldsFromConfig();
        ConfigManager.OnConfigReloaded += SyncFieldsFromConfig;
    }

    private void OnEnable()
    {
        RouterPanelBus.ToggleConfigRequested += ToggleVisible;
    }

    private void OnDisable()
    {
        RouterPanelBus.ToggleConfigRequested -= ToggleVisible;
    }

    private void OnDestroy()
    {
        ConfigManager.OnConfigReloaded -= SyncFieldsFromConfig;
    }

    public void ToggleVisible()
    {
        _show = !_show;
        Debug.Log($"[RouterConfigPanel] Panneau config {(_show ? "ouvert" : "fermé")} — touche I ou bouton « Config IP »");
    }

    private void SyncFieldsFromConfig()
    {
        var cfg = ConfigManager.Config;
        if (cfg == null) return;

        if (cfg.network?.controllers != null)
        {
            int n = cfg.network.controllers.Length;
            if (_ipFields == null || _ipFields.Length != n)
                _ipFields = new string[n];

            for (int i = 0; i < n; i++)
                _ipFields[i] = cfg.network.controllers[i].ip ?? "";
        }

        if (cfg.mapping != null)
        {
            _widthField = (cfg.mapping.screenWidth > 0 ? cfg.mapping.screenWidth : 128).ToString();
            _heightField = (cfg.mapping.screenHeight > 0 ? cfg.mapping.screenHeight : 128).ToString();
        }
    }

    private void OnGUI()
    {
        Event ev = Event.current;
        // Ignore I pendant la saisie d'une IP (TextField a le focus).
        if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.I && GUIUtility.keyboardControl == 0)
        {
            ToggleVisible();
            ev.Use();
        }
        if (_show && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
        {
            _show = false;
            GUIUtility.keyboardControl = 0;
            ev.Use();
        }

        if (!_show) return;

        GUI.depth = 200;

        const int panelW = 300;
        const int margin = 10;
        int panelH = 420 + (ConfigManager.Config?.network?.controllers?.Length ?? 0) * 22;
        float x = (Screen.width - panelW) * 0.5f;
        float y = (Screen.height - panelH) * 0.5f;

        GUI.Box(new Rect(x, y, panelW, panelH), "Routeur — Config (touche I)");

        if (GUI.Button(new Rect(x + panelW - 78, y + 4, 70, 20), "Fermer"))
            _show = false;

        float lineY = y + 22;
        float innerX = x + 8;
        float innerW = panelW - 16;

        DrawStatus(innerX, ref lineY, innerW);
        lineY += 6;
        DrawIpFields(innerX, ref lineY, innerW);
        lineY += 8;
        DrawOrientationToggles(innerX, ref lineY, innerW);
        lineY += 8;
        DrawScreenSizeFields(innerX, ref lineY, innerW);
        lineY += 8;
        DrawButtons(innerX, ref lineY, innerW);

        if (!string.IsNullOrEmpty(_feedback) && Time.unscaledTime < _feedbackUntil)
        {
            GUI.color = new Color(0.4f, 1f, 0.5f);
            GUI.Label(new Rect(innerX, lineY, innerW, 36), _feedback);
            GUI.color = Color.white;
        }
    }

    private static void DrawStatus(float x, ref float y, float w)
    {
        var cfg = ConfigManager.Config;
        if (cfg == null)
        {
            GUI.Label(new Rect(x, y, w, 18), "Config non chargée.");
            y += 18;
            return;
        }

        GUI.Label(new Rect(x, y, w, 18),
            $"Mur : {cfg.mapping.ledCount} LEDs ({cfg.mapping.screenWidth}×{cfg.mapping.screenHeight})");
        y += 18;
        GUI.Label(new Rect(x, y, w, 18), $"Layout : {cfg.mapping.layout}");
        y += 18;
        GUI.Label(new Rect(x, y, w, 18),
            $"Mapping CSV : {ConfigManager.EntityMap?.Count ?? 0} entités");
        y += 18;
        GUI.Label(new Rect(x, y, w, 18),
            $"Lyres : {cfg.mapping.lyres?.Length ?? 0} | Art-Net : {cfg.network.artNetPort} | eHuB : {cfg.network.ehubProtocolPort}");
        y += 18;

        int patchCount = cfg.router?.controllerPatch?.Length ?? 0;
        if (patchCount > 0)
        {
            GUI.Label(new Rect(x, y, w, 18), $"Patch map : {patchCount} reroutage(s) actif(s)");
            y += 18;
        }
    }

    private void DrawIpFields(float x, ref float y, float w)
    {
        var controllers = ConfigManager.Config?.network?.controllers;
        if (controllers == null || _ipFields == null) return;

        GUI.Label(new Rect(x, y, w, 18), "IP contrôleurs BC216 :");
        y += 20;

        for (int i = 0; i < controllers.Length; i++)
        {
            var c = controllers[i];
            GUI.Label(new Rect(x, y, 72, 20), $"#{i + 1} (U{c.startUniverse}+)");
            _ipFields[i] = GUI.TextField(new Rect(x + 76, y, w - 76, 20), _ipFields[i]);
            y += 24;
        }
    }

    private void DrawOrientationToggles(float x, ref float y, float w)
    {
        var cfg = ConfigManager.Config?.mapping;
        if (cfg == null) return;

        GUI.Label(new Rect(x, y, w, 18), "Orientation mur (test en direct) :");
        y += 20;

        float half = (w - 6) * 0.5f;
        bool newFlipY = GUI.Toggle(new Rect(x, y, half, 22), cfg.flipY, "Flip Y (haut/bas)");
        bool newFlipX = GUI.Toggle(new Rect(x + half + 6, y, half, 22), cfg.flipX, "Flip X (gauche/droite)");
        y += 26;

        if (newFlipY != cfg.flipY || newFlipX != cfg.flipX)
        {
            cfg.flipY = newFlipY;
            cfg.flipX = newFlipX;
            _configManager?.SaveConfig();
            ShowFeedback($"Orientation → flipY={cfg.flipY}, flipX={cfg.flipX}");
        }
    }

    private void DrawScreenSizeFields(float x, ref float y, float w)
    {
        if (ConfigManager.Config?.mapping == null) return;

        GUI.Label(new Rect(x, y, w, 18), "Taille affichage LED (Width × Height) :");
        y += 20;

        float half = (w - 6) * 0.5f;
        GUI.Label(new Rect(x, y, 52, 20), "Width");
        _widthField = GUI.TextField(new Rect(x + 52, y, half - 52, 20), _widthField);
        GUI.Label(new Rect(x + half + 6, y, 52, 20), "Height");
        _heightField = GUI.TextField(new Rect(x + half + 58, y, half - 52, 20), _heightField);
        y += 24;

        if (GUI.Button(new Rect(x, y, w, 26), "Appliquer la taille"))
            ApplyScreenSize();
        y += 28;

        GUI.Label(new Rect(x, y, w, 18), "Tout le projet se projette sur cette taille.");
        y += 18;
    }

    private void ApplyScreenSize()
    {
        if (_configManager == null)
        {
            ShowFeedback("ConfigManager introuvable.");
            return;
        }

        if (!int.TryParse(_widthField?.Trim(), out int width) ||
            !int.TryParse(_heightField?.Trim(), out int height))
        {
            ShowFeedback("Width / Height invalides (entiers requis).");
            return;
        }

        if (width < 1 || height < 1 || width > 512 || height > 512)
        {
            ShowFeedback("Taille hors limites (1–512).");
            return;
        }

        if (_configManager.SetScreenSize(width, height))
        {
            SyncFieldsFromConfig();
            var cfg = ConfigManager.Config.mapping;
            ShowFeedback($"Taille → {cfg.screenWidth}×{cfg.screenHeight} ({cfg.ledCount} LEDs)");
        }
        else
            ShowFeedback("Erreur : impossible d'appliquer la taille.");
    }

    private void DrawButtons(float x, ref float y, float w)
    {
        float half = (w - 6) * 0.5f;

        if (GUI.Button(new Rect(x, y, half, 28), "Sauvegarder"))
            SaveIps();

        if (GUI.Button(new Rect(x + half + 6, y, half, 28), "Recharger JSON"))
            ReloadFromDisk();

        y += 32;
        GUI.Label(new Rect(x, y, w, 18), "I ou Esc = masquer | Recharger JSON = disque");
        y += 18;
    }

    private void SaveIps()
    {
        if (_configManager == null || _ipFields == null)
        {
            ShowFeedback("ConfigManager introuvable.");
            return;
        }

        if (_configManager.SetAllControllerIps(_ipFields))
            ShowFeedback("IPs sauvegardées — routage mis à jour.");
        else
            ShowFeedback("Erreur : vérifiez les IP.");
    }

    private void ReloadFromDisk()
    {
        _configManager?.LoadConfig();
        SyncFieldsFromConfig();
        ShowFeedback("config.json rechargé depuis le disque.");
    }

    private void ShowFeedback(string message)
    {
        _feedback = message;
        _feedbackUntil = Time.unscaledTime + 3f;
        Debug.Log($"[RouterConfigPanel] {message}");
    }
}
