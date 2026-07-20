using System.Collections.Generic;
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
    private OtherDevicesPanel _otherDevices;
    private DanmarkKeyEffect _danmark;
    private EHubNetworkBridge _eHub;
    private string _hostIpInput = "";

    private void Awake()
    {
        _bootstrap = GetComponent<PixelHubBootstrapper>();
        _eHub = GetComponent<EHubNetworkBridge>();
        _audio = FindObjectOfType<AudioInteractiveManager>();
        _otherDevices = GetComponent<OtherDevicesPanel>();
        _danmark = GetComponent<DanmarkKeyEffect>();
        _hostIpInput = EHubNetworkBridge.GetSavedHostIp();
    }

    private void OnGUI()
    {
        const int margin = 10;
        const int panelW = 360;
        int panelH = ComputePanelHeight();

        float x = margin;
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
        DrawTimelineButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawDebugButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawDeviceButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawLyreButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawPauseAndVolume(innerX, ref lineY, innerW);
        lineY += 4;
        DrawSfxButtons(innerX, ref lineY, innerW);
        lineY += 4;
        DrawDanmarkButtons(innerX, ref lineY, innerW);
    }

    private int ComputePanelHeight()
    {
        if (_eHub == null || !_eHub.IsConnected)
        {
            int disconnectedH = 210;
            if (!string.IsNullOrEmpty(_eHub?.DiscoveredHostIp)) disconnectedH += 46;
            if (_eHub != null && _eHub.IsClientConnecting) disconnectedH += 24;
            if (!string.IsNullOrEmpty(_eHub?.LastConnectionError)) disconnectedH += 36;
            if (_eHub?.LocalIpCandidates != null && _eHub.LocalIpCandidates.Count > 1) disconnectedH += 18;
            return disconnectedH;
        }

        int h = 420;
        if (_audio != null && _audio.soundMappings.Count > 0)
            h += Mathf.Min(_audio.soundMappings.Count, 4) * 24 + 8;
        return h;
    }

    private void DrawConnectionPanel(float x, ref float y, float w)
    {
        if (_eHub == null || !_eHub.IsEnabled)
        {
            GUI.Label(new Rect(x, y, w, 18), "eHub désactivé (config.json)");
            y += 18;
            return;
        }

        GUI.Label(new Rect(x, y, w, 18), $"Votre IP : {_eHub.LocalIp}  (port {_eHub.Port})");
        y += 18;

        IReadOnlyList<string> ips = _eHub.LocalIpCandidates;
        if (ips != null && ips.Count > 1)
        {
            GUI.Label(new Rect(x, y, w, 16), $"Autres IP locales : {string.Join(", ", ips)}");
            y += 18;
        }
        else
        {
            y += 2;
        }

        if (_eHub.IsClientConnecting)
        {
            GUI.color = new Color(0.7f, 0.9f, 1f);
            GUI.Label(new Rect(x, y, w, 18), $"Connexion à {_hostIpInput}…");
            GUI.color = Color.white;
            y += 22;
        }

        if (!string.IsNullOrEmpty(_eHub.LastConnectionError))
        {
            GUI.color = new Color(1f, 0.75f, 0.55f);
            GUI.Label(new Rect(x, y, w, 32), _eHub.LastConnectionError);
            GUI.color = Color.white;
            y += 34;
        }

        if (_eHub.IsConnected)
        {
            DrawConnectedPanel(x, ref y, w);
            return;
        }

        DrawDisconnectedPanel(x, ref y, w);
    }

    private void DrawConnectedPanel(float x, ref float y, float w)
    {
        if (_eHub.Role == EHubRole.Host)
        {
            GUI.Label(new Rect(x, y, w, 18),
                $"★ HÔTE — mur LED actif  [{_eHub.SessionId}]");
            y += 18;
            GUI.Label(new Rect(x, y, w, 18),
                $"Partagez cette IP aux clients : {_eHub.LocalIp}:{_eHub.Port}");
            y += 18;
            GUI.Label(new Rect(x, y, w, 18), $"Clients ({_eHub.TotalPostes - 1}) : {_eHub.ActivePeersLabel}");
            y += 18;
            GUI.Label(new Rect(x, y, w, 32),
                "Seul ce poste envoie vers le mur LED.\nLes clients contrôlent à distance via sync.");
        }
        else
        {
            GUI.Label(new Rect(x, y, w, 18),
                $"● CLIENT — {_eHub.HostIp}:{_eHub.Port}  [{_eHub.SessionId}]");
            y += 18;
            GUI.Label(new Rect(x, y, w, 48),
                "Télécommande : touches et boutons synchronisés avec l'hôte.\n" +
                "Aperçu local OK — le mur LED physique est piloté par l'hôte uniquement.");
        }

        y += 52;

        if (GUI.Button(new Rect(x, y, w, 26), "Se déconnecter"))
            _eHub.Disconnect();

        y += 30;
    }

    private void DrawDisconnectedPanel(float x, ref float y, float w)
    {
        float halfW = (w - 4) / 2f;
        if (GUI.Button(new Rect(x, y, halfW, 28), "Je suis l'hôte"))
            _eHub.StartAsHost();

        GUI.Label(new Rect(x + halfW + 4, y + 6, halfW, 18), "ou client :");
        y += 32;

        GUI.Label(new Rect(x, y, w, 16), "IP de l'hôte (Wi-Fi du hôte) :");
        y += 18;

        _hostIpInput = GUI.TextField(new Rect(x, y, w - 84, 24), _hostIpInput);
        if (GUI.Button(new Rect(x + w - 78, y, 78, 24), "Connecter"))
        {
            if (!string.IsNullOrWhiteSpace(_hostIpInput))
                _eHub.ConnectToHost(_hostIpInput);
        }

        y += 28;

        if (!string.IsNullOrEmpty(_eHub.DiscoveredHostIp) &&
            !EHubNetworkUtil.IpEquals(_eHub.DiscoveredHostIp, _eHub.LocalIp))
        {
            GUI.Label(new Rect(x, y, w, 16), $"Hôte détecté sur le Wi-Fi : {_eHub.DiscoveredHostIp}");
            y += 18;
            if (GUI.Button(new Rect(x, y, w, 24), $"Se connecter à {_eHub.DiscoveredHostIp}"))
                _eHub.ConnectToDiscoveredHost();
            y += 28;
        }

        GUI.Label(new Rect(x, y, w, 64),
            "N'importe qui peut être HÔTE ou CLIENT.\n" +
            "1) L'hôte clique « Je suis l'hôte » et lit son IP\n" +
            "2) Les autres entrent cette IP (ou bouton auto)\n" +
            "3) L'hôte ET le client autorisent Unity (pare-feu privé)");
        y += 66;
    }

    private void DrawModeButtons(float x, ref float y, float w)
    {
        float btnW = (w - 8) / 5f;
        if (GUI.Button(new Rect(x, y, btnW, 22), "Timeline"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.Timeline);
        if (GUI.Button(new Rect(x + btnW + 2, y, btnW, 22), "Debug"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.Debug);
        if (GUI.Button(new Rect(x + (btnW + 2) * 2, y, btnW, 22), "Audio"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.Manual);
        if (GUI.Button(new Rect(x + (btnW + 2) * 3, y, btnW, 22), "Video"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.VideoCapture);
        if (GUI.Button(new Rect(x + (btnW + 2) * 4, y, btnW, 22), "eHuB"))
            _bootstrap?.RequestSwitchMode(PixelHubBootstrapper.StartMode.EHub);
        y += 26;
    }

    private void DrawTimelineButtons(float x, ref float y, float w)
    {
        float btnW = (w - 4) / 3f;
        if (GUI.Button(new Rect(x, y, btnW, 22), "▶ Play"))
            _bootstrap?.RequestPlayShow();
        if (GUI.Button(new Rect(x + btnW + 2, y, btnW, 22), "⏸ TL Pause"))
            _bootstrap?.RequestPauseShow();
        if (GUI.Button(new Rect(x + (btnW + 2) * 2, y, btnW, 22), "⏹ Stop"))
            _bootstrap?.RequestStopShow();
        y += 26;
    }

    private void DrawDebugButtons(float x, ref float y, float w)
    {
        float btnW = (w - 10) / 5f;
        if (GUI.Button(new Rect(x, y, btnW, 22), "1ère LED"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.FirstLed);
        if (GUI.Button(new Rect(x + (btnW + 2), y, btnW, 22), "R"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.Red);
        if (GUI.Button(new Rect(x + (btnW + 2) * 2, y, btnW, 22), "G"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.Green);
        if (GUI.Button(new Rect(x + (btnW + 2) * 3, y, btnW, 22), "B"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.Blue);
        if (GUI.Button(new Rect(x + (btnW + 2) * 4, y, btnW, 22), "Off"))
            _bootstrap?.RequestDebugColor(EHubDebugColor.BlackOut);
        y += 26;
    }

    private void DrawDeviceButtons(float x, ref float y, float w)
    {
        float btnW = (w - 10) / 5f;
        if (GUI.Button(new Rect(x, y, btnW, 22), "MH1"))
            _bootstrap?.RequestTestMovingHead(1);
        if (GUI.Button(new Rect(x + btnW + 2, y, btnW, 22), "MH2"))
            _bootstrap?.RequestTestMovingHead(2);
        if (GUI.Button(new Rect(x + (btnW + 2) * 2, y, btnW, 22), "MH3"))
            _bootstrap?.RequestTestMovingHead(3);
        if (GUI.Button(new Rect(x + (btnW + 2) * 3, y, btnW, 22), "MH4"))
            _bootstrap?.RequestTestMovingHead(4);
        if (GUI.Button(new Rect(x + (btnW + 2) * 4, y, btnW, 22), "Proj P"))
            _bootstrap?.RequestTestStaticProjector();
        y += 26;

        if (GUI.Button(new Rect(x, y, w, 22), "Lyres OFF (F5)"))
            _bootstrap?.RequestBlackOutLyres();
        y += 26;
    }

    private void DrawLyreButtons(float x, ref float y, float w)
    {
        if (_otherDevices == null) return;

        float btnW = (w - 8) / 5f;
        for (int i = 0; i < 4; i++)
        {
            if (GUI.Button(new Rect(x + i * (btnW + 2), y, btnW, 22), $"L{i + 1}"))
                _otherDevices.RequestPresetColor(i, (i * 2) % 8);
        }
        if (GUI.Button(new Rect(x + 4 * (btnW + 2), y, btnW, 22), "F3 beat"))
            _otherDevices.RequestNightclubToggle();
        y += 26;

        float half = (w - 2) / 2f;
        if (GUI.Button(new Rect(x, y, half, 22), "Toutes R"))
            _otherDevices.RequestSetAllPreset(0);
        if (GUI.Button(new Rect(x + half + 2, y, half, 22), "Toutes Off"))
            _otherDevices.RequestSetAllPreset(999);
        y += 26;
    }

    private void DrawPauseAndVolume(float x, ref float y, float w)
    {
        float third = (w - 4) / 3f;
        string pauseLabel = _audio != null && _audio.IsPaused ? "▶ Reprendre" : "⏸ Pause globale";
        if (GUI.Button(new Rect(x, y, third, 24), pauseLabel))
            _audio?.RequestPauseToggle();
        if (GUI.Button(new Rect(x + third + 2, y, third, 24), "Vol +"))
            _audio?.RequestVolumeDelta(_audio != null ? _audio.volumeStep : 0.1f);
        if (GUI.Button(new Rect(x + 2 * (third + 2), y, third, 24), "Vol −"))
            _audio?.RequestVolumeDelta(_audio != null ? -_audio.volumeStep : -0.1f);
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

        float fxW = (w - 6) / 4f;
        if (GUI.Button(new Rect(x, y, fxW, 22), "Flamme ←"))
            _audio.RequestTriggerEffect(-99);
        if (GUI.Button(new Rect(x + fxW + 2, y, fxW, 22), "Flamme →"))
            _audio.RequestTriggerEffect(-98);
        if (GUI.Button(new Rect(x + 2 * (fxW + 2), y, fxW, 22), "Laser L"))
            _audio.RequestTriggerEffect(-97);
        if (GUI.Button(new Rect(x + 3 * (fxW + 2), y, fxW, 22), "Shock S"))
            _audio.RequestTriggerEffect(-96);
        y += 26;
    }

    private void DrawDanmarkButtons(float x, ref float y, float w)
    {
        if (_danmark == null) return;

        float btnW = (w - 12) / 7f;
        string letters = "DANEMRK";
        for (int i = 0; i < letters.Length; i++)
        {
            string ch = letters[i].ToString();
            if (GUI.Button(new Rect(x + i * (btnW + 2), y, btnW, 22), ch))
                _danmark.RequestSpawnLetter(ch);
        }
        y += 26;

        float half = (w - 2) / 2f;
        if (GUI.Button(new Rect(x, y, half, 22), "DANEMARK complet"))
            _danmark.RequestShowDanmarkComplete();
        if (GUI.Button(new Rect(x + half + 2, y, half, 22), "Masquer DANEMARK"))
            _danmark.RequestHideDanmarkComplete();
        y += 26;
    }
}
