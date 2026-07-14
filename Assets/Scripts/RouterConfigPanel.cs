using UnityEngine;
using Laps.Core;

/// <summary>
/// Panneau OnGUI P1 : édition des IP contrôleurs + rechargement config routeur.
/// Touche F6 pour afficher / masquer.
/// </summary>
public class RouterConfigPanel : MonoBehaviour
{
    private ConfigManager _configManager;
    private bool _show;
    private string[] _ipFields;
    private string _feedback = "";
    private float _feedbackUntil;

    private void Awake()
    {
        _configManager = FindObjectOfType<ConfigManager>();
        SyncFieldsFromConfig();
        ConfigManager.OnConfigReloaded += SyncFieldsFromConfig;
    }

    private void OnDestroy()
    {
        ConfigManager.OnConfigReloaded -= SyncFieldsFromConfig;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6))
            _show = !_show;
    }

    private void SyncFieldsFromConfig()
    {
        var cfg = ConfigManager.Config;
        if (cfg?.network?.controllers == null) return;

        int n = cfg.network.controllers.Length;
        if (_ipFields == null || _ipFields.Length != n)
            _ipFields = new string[n];

        for (int i = 0; i < n; i++)
            _ipFields[i] = cfg.network.controllers[i].ip ?? "";
    }

    private void OnGUI()
    {
        if (!_show) return;

        const int panelW = 300;
        const int margin = 10;
        int panelH = 280 + (ConfigManager.Config?.network?.controllers?.Length ?? 0) * 22;
        float x = Screen.width - panelW - margin;
        float y = margin + 10;

        GUI.Box(new Rect(x, y, panelW, panelH), "Routeur — Config (P1)");

        float lineY = y + 22;
        float innerX = x + 8;
        float innerW = panelW - 16;

        DrawStatus(innerX, ref lineY, innerW);
        lineY += 6;
        DrawIpFields(innerX, ref lineY, innerW);
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
            $"Lyres : {cfg.mapping.lyres?.Length ?? 0} | Art-Net : {cfg.network.artNetPort}");
        y += 18;
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

    private void DrawButtons(float x, ref float y, float w)
    {
        float half = (w - 6) * 0.5f;

        if (GUI.Button(new Rect(x, y, half, 28), "Sauvegarder"))
            SaveIps();

        if (GUI.Button(new Rect(x + half + 6, y, half, 28), "Recharger JSON"))
            ReloadFromDisk();

        y += 32;
        GUI.Label(new Rect(x, y, w, 18), "F6 = masquer ce panneau");
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
