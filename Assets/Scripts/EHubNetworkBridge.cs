using UnityEngine;
using Laps.Core;
using Laps.Authoring;

/// <summary>
/// Relie le transport eHub UDP aux actions du show.
/// Mode Hôte : les autres saisissent votre IP.
/// Mode Client : saisir l'IP de l'hôte puis « Se connecter ».
/// </summary>
public class EHubNetworkBridge : MonoBehaviour
{
    [SerializeField] private bool _syncEnabled = true;

    private EHubTransport _transport;
    private PixelHubBootstrapper _bootstrap;
    private AudioInteractiveManager _audio;
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
    public string PeersConfigLabel => _transport?.PeersConfigLabel ?? "—";
    public string ActivePeersLabel => _transport?.ActivePeersLabel ?? "—";
    public int TotalPostes => _transport?.TotalPostes ?? 1;

    private void Awake()
    {
        _bootstrap = GetComponent<PixelHubBootstrapper>();
        _audio = FindObjectOfType<AudioInteractiveManager>();
    }

    private void Start()
    {
        if (!_syncEnabled || ConfigManager.Config?.network?.eHubEnabled != true) return;
        EHubSyncBus.RegisterSendHandler(msg => Send(msg));
        EHubStatus.Update(true, false, EHubRole.Solo, "", 1);
    }

    private void OnDestroy()
    {
        _transport?.Dispose();
    }

    /// <summary>Une personne de l'équipe clique « Je suis l'hôte ».</summary>
    public void StartAsHost()
    {
        if (!_syncEnabled) return;

        var net = ConfigManager.Config?.network;
        if (net == null) return;

        _transport?.Dispose();
        _transport = new EHubTransport(net.eHubPort, net.eHubSessionId);
        _transport.StartAsHost();
    }

    /// <summary>Les autres membres saisissent l'IP de l'hôte et cliquent « Se connecter ».</summary>
    public void ConnectToHost(string hostIp)
    {
        if (!_syncEnabled || string.IsNullOrWhiteSpace(hostIp)) return;

        var net = ConfigManager.Config?.network;
        if (net == null) return;

        hostIp = hostIp.Trim();
        PlayerPrefs.SetString("eHub.lastHostIp", hostIp);
        PlayerPrefs.Save();

        _transport?.Dispose();
        _transport = new EHubTransport(net.eHubPort, net.eHubSessionId);
        _transport.ConnectToHost(hostIp);
    }

    public static string GetSavedHostIp() => PlayerPrefs.GetString("eHub.lastHostIp", "");

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
            }

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
