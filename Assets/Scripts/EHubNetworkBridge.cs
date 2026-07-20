using System.Net;
using UnityEngine;
using Laps.Core;
using Laps.Authoring;

/// <summary>
/// Relie le transport eHub UDP aux actions du show.
/// Mode Hôte : les autres saisissent votre IP.
/// Mode Client : saisir l'IP de l'hôte puis « Connecter ».
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

    public bool IsEnabled => _syncEnabled;
    public bool IsConnected => _transport != null && _transport.IsConnected;
    public bool IsSoloMode => _transport == null || _transport.IsSoloMode;
    public EHubRole Role => _transport?.Role ?? EHubRole.Solo;
    public string ClientId => _transport?.ClientId ?? "—";
    public string SessionId => ConfigManager.Config?.network?.eHubSessionId ?? "—";
    public string LocalIp => _transport?.LocalIp ?? ResolveLocalIpFallback();
    public string HostIp => _transport?.HostIp ?? "";
    public int Port => ConfigManager.Config?.network?.eHubPort ?? 9000;
    public string PeersConfigLabel => _transport?.PeersConfigLabel ?? "—";
    public string ActivePeersLabel => _transport?.ActivePeersLabel ?? "—";
    public int TotalPostes => _transport?.TotalPostes ?? 1;

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
    }

    private void OnDestroy()
    {
        UnsubscribeTransportEvents();
        _transport?.Dispose();
    }

    /// <summary>Une personne de l'équipe clique « Je suis l'hôte ».</summary>
    public void StartAsHost()
    {
        if (!_syncEnabled) return;

        var net = ConfigManager.Config?.network;
        if (net == null) return;

        ReplaceTransport(new EHubTransport(net.eHubPort, net.eHubSessionId));
        _transport.StartAsHost();
    }

    /// <summary>Les autres membres saisissent l'IP de l'hôte et cliquent « Connecter ».</summary>
    public void ConnectToHost(string hostIp)
    {
        if (!_syncEnabled || string.IsNullOrWhiteSpace(hostIp)) return;

        var net = ConfigManager.Config?.network;
        if (net == null) return;

        hostIp = hostIp.Trim();
        if (!IPAddress.TryParse(hostIp, out _))
        {
            Debug.LogWarning($"[eHub] Adresse IP invalide : « {hostIp} »");
            return;
        }

        PlayerPrefs.SetString("eHub.lastHostIp", hostIp);
        PlayerPrefs.Save();

        ReplaceTransport(new EHubTransport(net.eHubPort, net.eHubSessionId));
        _transport.ConnectToHost(hostIp);
    }

    /// <summary>Quitte la session hôte/client et repasse en mode solo.</summary>
    public void Disconnect()
    {
        UnsubscribeTransportEvents();
        _transport?.Dispose();
        _transport = null;
        _helloTimer = 0f;
        EHubStatus.Update(_syncEnabled, false, EHubRole.Solo, "", 1);
        Debug.Log("[eHub] Déconnecté — contrôle distant désactivé.");
    }

    public static string GetSavedHostIp() => PlayerPrefs.GetString("eHub.lastHostIp", "");

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
    }

    private void UnsubscribeTransportEvents()
    {
        if (_transport == null) return;
        _transport.ClientJoined -= OnClientJoined;
        _transport.ClientLinked -= OnClientLinked;
    }

    private void OnClientJoined(string clientIp)
    {
        if (_transport == null || _transport.Role != EHubRole.Host) return;
        PushFullStateTo(clientIp);
        Debug.Log($"[eHub] Client connecté ({clientIp}) — état du show envoyé.");
    }

    private void OnClientLinked()
    {
        Debug.Log("[eHub] Connecté à l'hôte — vous voyez le même état et pouvez contrôler le show.");
    }

    private void PushFullStateTo(string clientIp)
    {
        if (_transport == null || _bootstrap == null) return;

        _transport.SendToPeer(new EHubMessage
        {
            type = EHubMessageTypes.StateSync,
            intArg = (int)_bootstrap.CurrentMode,
            floatArg = (_audio != null && _audio.IsPaused) ? 1f : 0f
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

        if (_transport.Role == EHubRole.Client)
        {
            _helloTimer += Time.unscaledDeltaTime;
            if (_helloTimer >= 2f)
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
                    _bootstrap?.ApplySwitchMode((PixelHubBootstrapper.StartMode)msg.intArg);
                    _audio?.SetPaused(msg.floatArg >= 0.5f, fromNetwork: true);
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

    private static string ResolveLocalIpFallback()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
            }
        }
        catch { /* ignore */ }
        return "?.?.?.?";
    }
}
