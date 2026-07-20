using System.Collections.Generic;
using System.Net;
using UnityEngine;
using Laps.Core;
using Laps.Authoring;

/// <summary>
/// Relie le transport eHub UDP aux actions du show.
/// Mode Hôte : seul poste qui envoie Art-Net vers le mur LED.
/// Mode Client : télécommande — commandes sync vers l'hôte, aperçu local uniquement.
/// </summary>
public class EHubNetworkBridge : MonoBehaviour
{
    [SerializeField] private bool _syncEnabled = true;

    private EHubTransport _transport;
    private PixelHubBootstrapper _bootstrap;
    private AudioInteractiveManager _audio;
    private OtherDevicesPanel _otherDevices;
    private DanmarkKeyEffect _danmark;
    private bool _applyingRemote;
    private float _helloTimer;
    private float _beaconTimer;

    public bool IsEnabled => _syncEnabled;
    public bool IsConnected => _transport != null && _transport.IsConnected;
    public bool IsSoloMode => _transport == null || _transport.IsSoloMode;
    public EHubRole Role => _transport?.Role ?? EHubRole.Solo;
    public string ClientId => _transport?.ClientId ?? "—";
    public string SessionId => ConfigManager.Config?.network?.eHubSessionId ?? "—";
    public string LocalIp => _transport?.LocalIp ?? ResolveLocalIpFallback();
    public string HostIp => _transport?.HostIp ?? "";
    public string DiscoveredHostIp => _transport?.DiscoveredHostIp ?? "";
    public string LastConnectionError => _transport?.LastConnectionError ?? "";
    public bool IsClientConnecting => _transport != null && _transport.IsClientConnecting;
    public IReadOnlyList<string> LocalIpCandidates => EHubNetworkUtil.CollectLanCandidates();
    public int Port => ConfigManager.Config?.network?.eHubPort ?? 9000;
    public string PeersConfigLabel => _transport?.PeersConfigLabel ?? "—";
    public string ActivePeersLabel => _transport?.ActivePeersLabel ?? "—";
    public int TotalPostes => _transport?.TotalPostes ?? 1;
    public bool IsHardwareOutputEnabled => EHubStatus.ShouldOutputToHardware;

    private void Awake()
    {
        _bootstrap = GetComponent<PixelHubBootstrapper>();
        _audio = FindObjectOfType<AudioInteractiveManager>();
        _otherDevices = GetComponent<OtherDevicesPanel>();
        _danmark = GetComponent<DanmarkKeyEffect>();
    }

    private void Start()
    {
        if (!_syncEnabled || ConfigManager.Config?.network?.eHubEnabled != true) return;
        EHubSyncBus.RegisterSendHandler(msg => Send(msg));
        EHubStatus.Update(true, false, EHubRole.Solo, "", 1);
        StartDiscovery();
    }

    private void OnDestroy()
    {
        UnsubscribeTransportEvents();
        _transport?.Dispose();
    }

    public void StartAsHost()
    {
        if (!_syncEnabled) return;

        var net = ConfigManager.Config?.network;
        if (net == null) return;

        if (_transport == null || !_transport.IsListening)
            ReplaceTransport(new EHubTransport(net.eHubPort, net.eHubSessionId));
        _transport.StartAsHost();
        EHubStatus.HostDetectedOnLan = false;
        _transport.SendHostBeacon();
        Debug.Log("[eHub] Vous pilotez le mur LED — les clients envoient des commandes uniquement.");
    }

    public void ConnectToHost(string hostIp)
    {
        if (!_syncEnabled || string.IsNullOrWhiteSpace(hostIp)) return;

        var net = ConfigManager.Config?.network;
        if (net == null) return;

        hostIp = hostIp.Trim();
        if (EHubNetworkUtil.IsLoopbackOrSelf(LocalIp, hostIp))
        {
            Debug.LogWarning("[eHub] Vous ne pouvez pas vous connecter à votre propre IP. Demandez l'IP de l'hôte à un coéquipier.");
            return;
        }

        if (!IPAddress.TryParse(hostIp, out _))
        {
            Debug.LogWarning($"[eHub] Adresse IP invalide : « {hostIp} »");
            return;
        }

        PlayerPrefs.SetString("eHub.lastHostIp", hostIp);
        PlayerPrefs.Save();

        if (_transport == null || !_transport.IsListening)
            ReplaceTransport(new EHubTransport(net.eHubPort, net.eHubSessionId));
        _transport.ConnectToHost(hostIp);
    }

    public void ConnectToDiscoveredHost()
    {
        if (string.IsNullOrEmpty(DiscoveredHostIp)) return;
        ConnectToHost(DiscoveredHostIp);
    }

    public void Disconnect()
    {
        UnsubscribeTransportEvents();
        _transport?.Dispose();
        _transport = null;
        _helloTimer = 0f;
        _beaconTimer = 0f;
        EHubStatus.Update(_syncEnabled, false, EHubRole.Solo, "", 1);
        EHubStatus.HostDetectedOnLan = false;
        Debug.Log("[eHub] Déconnecté — contrôle distant désactivé.");
        StartDiscovery();
    }

    public static string GetSavedHostIp() => PlayerPrefs.GetString("eHub.lastHostIp", "");

    private void StartDiscovery()
    {
        if (!_syncEnabled || ConfigManager.Config?.network?.eHubEnabled != true) return;
        if (_transport != null && _transport.IsConnected) return;

        var net = ConfigManager.Config.network;
        ReplaceTransport(new EHubTransport(net.eHubPort, net.eHubSessionId));
        _transport.StartDiscoveryListen();
    }

    private void ReplaceTransport(EHubTransport transport)
    {
        UnsubscribeTransportEvents();
        _transport?.Dispose();
        _transport = transport;
        SubscribeTransportEvents();
    }

    private void SubscribeTransportEvents()
    {
        if (_transport == null) return;
        _transport.ClientJoined += OnClientJoined;
        _transport.ClientLinked += OnClientLinked;
        _transport.HostDiscovered += OnHostDiscovered;
        _transport.HostConflictDetected += OnHostConflictDetected;
        _transport.ClientLinkFailed += OnClientLinkFailed;
    }

    private void UnsubscribeTransportEvents()
    {
        if (_transport == null) return;
        _transport.ClientJoined -= OnClientJoined;
        _transport.ClientLinked -= OnClientLinked;
        _transport.HostDiscovered -= OnHostDiscovered;
        _transport.HostConflictDetected -= OnHostConflictDetected;
        _transport.ClientLinkFailed -= OnClientLinkFailed;
    }

    private void OnClientJoined(string clientIp)
    {
        if (_transport == null || _transport.Role != EHubRole.Host) return;
        PushFullStateTo(clientIp);
    }

    private void OnClientLinked()
    {
        _helloTimer = 0f;
        _transport?.RequestStateSyncOnNextHello();
        _transport?.SendHello();
        Debug.Log("[eHub] Connecté à l'hôte — télécommande active. État synchronisé depuis l'hôte.");
    }

    private void OnHostDiscovered(string hostIp)
    {
        EHubStatus.HostDetectedOnLan = true;
        Debug.Log($"[eHub] Hôte détecté sur le réseau : {hostIp} — connectez-vous en CLIENT.");
    }

    private void OnClientLinkFailed(string error)
    {
        Debug.LogWarning($"[eHub] Échec connexion : {error}");
    }

    private void OnHostConflictDetected(string otherHostIp)
    {
        Debug.LogWarning(
            $"[eHub] CONFLIT — un autre hôte ({otherHostIp}) est actif sur le même réseau. " +
            "Un seul poste doit être hôte pour éviter les artefacts sur le mur LED.");
    }

    private void PushFullStateTo(string clientIp)
    {
        if (_transport == null || _bootstrap == null) return;

        _bootstrap.GetFullSyncState(
            out int mode,
            out int timelineState,
            out float timelineTime,
            out float directorTime,
            out int directorState);

        string directorPayload = directorTime >= 0f
            ? $"dt:{directorTime:F3}|ds:{directorState}"
            : "";

        _transport.SendToPeer(new EHubMessage
        {
            type = EHubMessageTypes.StateSync,
            intArg = mode,
            intArg2 = timelineState,
            floatArg = (_audio != null && _audio.IsPaused) ? 1f : 0f,
            floatArg2 = timelineTime,
            stringArg = directorPayload
        }, clientIp);

        _transport.SendToPeer(new EHubMessage
        {
            type = EHubMessageTypes.VolumeSet,
            floatArg = AudioListener.volume
        }, clientIp);

        if (_otherDevices != null)
        {
            _transport.SendToPeer(new EHubMessage
            {
                type = EHubMessageTypes.LyreControl,
                intArg = EHubLyreAction.SyncSnapshot,
                stringArg = _otherDevices.BuildSyncSnapshot()
            }, clientIp);
        }
    }

    private void Update()
    {
        if (_transport == null) return;

        _transport.Tick();

        if (_transport.Role == EHubRole.Host)
        {
            _beaconTimer += Time.unscaledDeltaTime;
            if (_beaconTimer >= 3f)
            {
                _beaconTimer = 0f;
                _transport.SendHostBeacon();
            }
        }

        if (_transport.Role == EHubRole.Client)
        {
            _helloTimer += Time.unscaledDeltaTime;
            if (_helloTimer >= 1f)
            {
                _helloTimer = 0f;
                _transport.SendHello();
            }
        }

        while (_transport.TryDequeue(out EHubMessage msg))
            ApplyRemote(msg);

        EHubStatus.Update(
            _syncEnabled,
            _transport.IsConnected,
            _transport.Role,
            _transport.HostIp,
            _transport.TotalPostes);
    }

    private void Send(EHubMessage msg)
    {
        if (!_syncEnabled || _transport == null || _applyingRemote || !_transport.IsConnected) return;
        _transport.Send(msg);
    }

    private void ApplyRemote(EHubMessage msg)
    {
        _applyingRemote = true;
        try
        {
            switch (msg.type)
            {
                case EHubMessageTypes.SwitchMode:
                    _bootstrap?.ApplySwitchMode((PixelHubBootstrapper.StartMode)msg.intArg);
                    break;

                case EHubMessageTypes.DebugColor:
                    _bootstrap?.ApplyDebugColor(msg.intArg);
                    break;

                case EHubMessageTypes.SfxTrigger:
                    _audio?.TriggerEffect(msg.intArg, fromNetwork: true);
                    break;

                case EHubMessageTypes.PauseState:
                    _audio?.SetPaused(msg.intArg == 1, fromNetwork: true);
                    break;

                case EHubMessageTypes.StateSync:
                    ParseDirectorPayload(msg.stringArg, out float directorTime, out int directorState);
                    _bootstrap?.ApplyFullStateSync(
                        msg.intArg,
                        msg.intArg2,
                        msg.floatArg2,
                        directorTime,
                        directorState,
                        msg.floatArg >= 0.5f);
                    break;

                case EHubMessageTypes.DeviceAction:
                    _bootstrap?.ApplyDeviceAction(msg.intArg);
                    break;

                case EHubMessageTypes.LyreControl:
                    _otherDevices?.ApplyLyreControl(msg);
                    break;

                case EHubMessageTypes.VolumeSet:
                    _audio?.ApplyVolume(msg.floatArg, fromNetwork: true);
                    break;

                case EHubMessageTypes.TimelineControl:
                    _bootstrap?.ApplyTimelineControl(msg.intArg);
                    break;

                case EHubMessageTypes.DanmarkLetter:
                    _danmark?.ApplyDanmarkFromNetwork(msg.stringArg);
                    break;
            }

            if (msg.type != EHubMessageTypes.Hello && msg.type != EHubMessageTypes.HelloAck)
                Debug.Log($"[eHub] ← {msg.type} (de {msg.senderId})");
            _bootstrap?.RefreshDisplay();
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    private static void ParseDirectorPayload(string payload, out float directorTime, out int directorState)
    {
        directorTime = -1f;
        directorState = 0;
        if (string.IsNullOrEmpty(payload)) return;

        foreach (string part in payload.Split('|'))
        {
            if (part.StartsWith("dt:", System.StringComparison.Ordinal))
            {
                float.TryParse(part.Substring(3), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out directorTime);
            }
            else if (part.StartsWith("ds:", System.StringComparison.Ordinal))
            {
                int.TryParse(part.Substring(3), out directorState);
            }
        }
    }

    private static string ResolveLocalIpFallback() => EHubNetworkUtil.ResolveBestLocalIpv4();
}
