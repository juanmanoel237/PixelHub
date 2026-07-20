using UnityEngine;
using Laps.Core;
using Laps.Authoring;

/// <summary>
/// Panneau OnGUI : connexion hôte/client + boutons synchronisés eHub.
/// </summary>
public class EHubControlPanel : MonoBehaviour
{
    private PixelHubBootstrapper _bootstrap;
    private AudioInteractiveManager _audio;
    private EHubNetworkBridge _eHub;
    private string _hostIpInput = "";

    private void Awake()
    {
        _bootstrap = GetComponent<PixelHubBootstrapper>();
        _eHub = GetComponent<EHubNetworkBridge>();
        _audio = FindObjectOfType<AudioInteractiveManager>();
        _hostIpInput = EHubNetworkBridge.GetSavedHostIp();
    }

    private void OnGUI()
    {
        const int margin = 10;
        const int panelW = 320;
        int panelH = _eHub != null && _eHub.IsConnected ? 200 : 130;

        if (_eHub != null && _eHub.IsConnected && _audio != null && _audio.soundMappings.Count > 0)
            panelH += Mathf.Min(_audio.soundMappings.Count, 4) * 24 + 8;

        float x = margin;
        // Réserve la zone projecteurs au-dessus (évite le chevauchement).
        float y = Screen.height - panelH - margin;

        GUI.Box(new Rect(x, y, panelW, panelH), "eHub — sync équipe");

        float lineY = y + 22;
        float innerX = x + 8;
        float innerW = panelW - 16;

        DrawConnectionPanel(innerX, ref lineY, innerW);

        if (_eHub == null || !_eHub.IsConnected)
            return;

        lineY += 6;
        DrawModeButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawDebugButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawPauseButton(innerX, ref lineY, innerW);
        lineY += 4;
        DrawSfxButtons(innerX, ref lineY, innerW);
    }

    private void DrawConnectionPanel(float x, ref float y, float w)
    {
        if (_eHub == null || !_eHub.IsEnabled)
        {
            GUI.Label(new Rect(x, y, w, 18), "eHub désactivé (config.json)");
            y += 18;
            return;
        }

        GUI.Label(new Rect(x, y, w, 18), $"Votre IP : {_eHub.LocalIp}  (donnez-la à l'équipe si vous êtes hôte)");
        y += 20;

        if (_eHub.IsConnected)
        {
            if (_eHub.Role == EHubRole.Host)
            {
                GUI.Label(new Rect(x, y, w, 18), $"★ HÔTE — {_eHub.TotalPostes} poste(s)  [{_eHub.SessionId}]");
                y += 18;
                GUI.Label(new Rect(x, y, w, 18), $"Clients : {_eHub.ActivePeersLabel}");
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 18), $"Connecté à l'hôte {_eHub.HostIp}  [{_eHub.SessionId}]");
            }
            y += 22;
            return;
        }

        // Pas encore connecté
        float halfW = (w - 4) / 2f;
        if (GUI.Button(new Rect(x, y, halfW, 28), "Je suis l'hôte"))
            _eHub.StartAsHost();

        GUI.Label(new Rect(x + halfW + 4, y + 6, halfW, 18), "ou IP hôte :");
        y += 30;

        _hostIpInput = GUI.TextField(new Rect(x, y, w - 84, 24), _hostIpInput);
        if (GUI.Button(new Rect(x + w - 78, y, 78, 24), "Connecter"))
            _eHub.ConnectToHost(_hostIpInput);

        y += 28;
        GUI.Label(new Rect(x, y, w, 32),
            "1) Une personne = Hôte\n2) Les autres tapent son IP puis Connecter");
        y += 34;
    }

    private void DrawModeButtons(float x, ref float y, float w)
    {
        float btnW = (w - 6) / 4f;
        if (GUI.Button(new Rect(x, y, btnW, 22), "Timeline"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.Timeline);
        if (GUI.Button(new Rect(x + btnW + 2, y, btnW, 22), "Debug"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.Debug);
        if (GUI.Button(new Rect(x + (btnW + 2) * 2, y, btnW, 22), "Audio"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.Manual);
        if (GUI.Button(new Rect(x + (btnW + 2) * 3, y, btnW, 22), "Video"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.VideoCapture);
        y += 26;
    }

    private void DrawDebugButtons(float x, ref float y, float w)
    {
        float btnW = (w - 10) / 5f;
        if (GUI.Button(new Rect(x, y, btnW, 22), "1ère LED"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.FirstLed);
        if (GUI.Button(new Rect(x + btnW + 2, y, btnW, 22), "R"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.Red);
        if (GUI.Button(new Rect(x + (btnW + 2) * 2, y, btnW, 22), "G"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.Green);
        if (GUI.Button(new Rect(x + (btnW + 2) * 3, y, btnW, 22), "B"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.Blue);
        if (GUI.Button(new Rect(x + (btnW + 2) * 4, y, btnW, 22), "Off"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.BlackOut);
        y += 26;
    }

    private void DrawPauseButton(float x, ref float y, float w)
    {
        string label = _audio != null && _audio.IsPaused ? "▶ Reprendre" : "⏸ Pause";
        if (GUI.Button(new Rect(x, y, w, 24), label))
            _audio?.RequestPauseToggle();
        y += 28;
    }

    private void DrawSfxButtons(float x, ref float y, float w)
    {
        if (_audio == null || _audio.soundMappings.Count == 0) return;

        int count = Mathf.Min(_audio.soundMappings.Count, 4);
        float btnW = (w - (count - 1) * 2f) / count;

        for (int i = 0; i < count; i++)
        {
            var mapping = _audio.soundMappings[i];
            string label = mapping.clip != null ? mapping.clip.name : $"SFX {i + 1}";
            if (label.Length > 10) label = label.Substring(0, 10);
            label += $" [{mapping.key}]";

            if (GUI.Button(new Rect(x + i * (btnW + 2), y, btnW, 22), label))
                _audio.RequestTriggerEffect(i);
        }
        y += 26;
    }
}
