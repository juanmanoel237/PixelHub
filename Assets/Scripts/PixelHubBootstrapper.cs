using UnityEngine;
using UnityEngine.Playables;
using Laps.Core;
using Laps.Routing;
using Laps.Authoring;

/// <summary>
/// Point d'entrée principal de PixelHub.
///
/// Ce MonoBehaviour orchestre tous les modules :
///   1. ConfigManager → charge config.json
///   2. RoutingEngine → prêt à recevoir un IStateProvider
///   3. ShowTimeline  → authoring, s'enregistre comme IStateProvider
///   4. DebugPanel    → outils de débogage
///
/// À attacher sur un GameObject "PixelHub" dans la scène principale.
/// </summary>
public class PixelHubBootstrapper : MonoBehaviour
{
    [Header("Modules (assignés dans l'Inspector)")]
    [SerializeField] private ConfigManager  _configManager;
    [SerializeField] private RoutingEngine  _routingEngine;
    [SerializeField] private ShowTimeline   _showTimeline;
    [SerializeField] private DebugPanel     _debugPanel;
    [SerializeField] private EHubReceiver   _eHubReceiver;
    [SerializeField] private AudioReactiveProvider _audioReactive;
    [SerializeField] private VideoCaptureProvider _videoCapture;
    [SerializeField] private OtherDevicesPanel _otherDevices;
    private EHubNetworkBridge _eHub;
    private LedPreviewOverlay _previewOverlay;

    [Header("Mode de démarrage")]
    [SerializeField] private StartMode _startMode = StartMode.Timeline;

    private StartMode _currentMode;

    public enum StartMode
    {
        Timeline,     // Authoring classique via ShowTimeline
        Debug,        // Fake state via DebugPanel (pour tester les contrôleurs)
        EHub,         // Réception eHuB UDP (P7 bonus)
        Manual,       // L'utilisateur choisit via l'UI
        VideoCapture  // Démarre directement sur la caméra
    }

    private void Awake()
    {
        EnsureComponents();
        _eHub = GetComponent<EHubNetworkBridge>() ?? gameObject.AddComponent<EHubNetworkBridge>();
        if (GetComponent<EHubControlPanel>() == null)
            gameObject.AddComponent<EHubControlPanel>();
        if (GetComponent<RouterConfigPanel>() == null)
            gameObject.AddComponent<RouterConfigPanel>();

        _previewOverlay = GetComponent<LedPreviewOverlay>() ?? gameObject.AddComponent<LedPreviewOverlay>();
        _previewOverlay.Init(_routingEngine);

        // Vidéo overlay sur un GameObject séparé pour que GUI.depth fonctionne
        // indépendamment des autres panneaux OnGUI
        var videoOverlayGO = new GameObject("VideoOverlayRenderer");
        videoOverlayGO.transform.SetParent(transform);
        videoOverlayGO.AddComponent<Laps.Authoring.VideoOverlayRenderer>();
    }

    private void Start()
    {
        if (ConfigManager.Config == null)
        {
            Debug.LogError("[PixelHubBootstrapper] ConfigManager non trouvé ou config non chargée !");
            return;
        }

        _debugPanel?.SetRoutingEngine(_routingEngine);

        switch (_startMode)
        {
            case StartMode.Timeline:
                _showTimeline.LoadShow();
                _showTimeline.Play();
                SetProviderWithDevices(_showTimeline);
                _currentMode = StartMode.Timeline;
                Debug.Log("[PixelHubBootstrapper] Mode Timeline actif.");
                break;

            case StartMode.Debug:
                SetProviderWithDevices(_debugPanel);
                _debugPanel.SetFakeStateActive(true);
                _currentMode = StartMode.Debug;
                Debug.Log("[PixelHubBootstrapper] Mode Debug actif (fake state).");
                break;

            case StartMode.EHub:
                SetProviderWithDevices(_eHubReceiver);
                _currentMode = StartMode.EHub;
                Debug.Log("[PixelHubBootstrapper] Mode eHuB actif (réception UDP).");
                break;

            case StartMode.Manual:
                _currentMode = StartMode.Manual;
                Debug.Log("[PixelHubBootstrapper] Mode Manuel — en attente de sélection via l'UI.");
                break;

            case StartMode.VideoCapture:
                _showTimeline.LoadShow();
                _showTimeline.Play();
                SwitchToVideoCapture();
                break;
        }

        _routingEngine.StartRouting();

        _previewOverlay = GetComponent<LedPreviewOverlay>() ?? gameObject.AddComponent<LedPreviewOverlay>();
        _previewOverlay.Init(_routingEngine);
        UpdatePreviewProvider();

        Debug.Log("[PixelHubBootstrapper] PixelHub démarré avec succès !");
        Debug.Log("[PixelHubBootstrapper] → Onglet GAME pour voir l'aperçu. Touches : T=timeline | D=debug | E=eHuB | A=audio | V=video");
        Debug.Log("[PixelHubBootstrapper] → En mode DEBUG uniquement : 1, R, G, B, 0");
        Debug.Log("[PixelHubBootstrapper] → eHub : panneau bas — « Je suis l'hôte » ou saisir IP + Connecter.");
        Debug.Log("[PixelHubBootstrapper] → F6 = panneau config routeur (IP contrôleurs BC216).");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            RequestSwitchMode(StartMode.Timeline);
        else if (Input.GetKeyDown(KeyCode.D))
            RequestSwitchMode(StartMode.Debug);
        else if (Input.GetKeyDown(KeyCode.E))
            RequestSwitchMode(StartMode.EHub);
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Q))
            RequestSwitchMode(StartMode.Manual);
        else if (Input.GetKeyDown(KeyCode.V))
            RequestSwitchMode(StartMode.VideoCapture);
        else if (_currentMode == StartMode.Debug && Input.GetKeyDown(KeyCode.Alpha1))
            RequestDebugColor(EHubDebugColor.FirstLed);
        else if (_currentMode == StartMode.Debug && Input.GetKeyDown(KeyCode.R))
            RequestDebugColor(EHubDebugColor.Red);
        else if (_currentMode == StartMode.Debug && Input.GetKeyDown(KeyCode.G))
            RequestDebugColor(EHubDebugColor.Green);
        else if (_currentMode == StartMode.Debug && Input.GetKeyDown(KeyCode.B))
            RequestDebugColor(EHubDebugColor.Blue);
        else if (_currentMode == StartMode.Debug && Input.GetKeyDown(KeyCode.Alpha0))
            RequestDebugColor(EHubDebugColor.BlackOut);
        else if (Input.GetKeyDown(KeyCode.F1))
            TestMovingHead(1);
        else if (Input.GetKeyDown(KeyCode.F4))
            TestMovingHead(4);
        else if (Input.GetKeyDown(KeyCode.Alpha6))
            TestMovingHead(1);
        else if (Input.GetKeyDown(KeyCode.Alpha7))
            TestMovingHead(2);
        else if (Input.GetKeyDown(KeyCode.Alpha8))
            TestMovingHead(3);
        else if (Input.GetKeyDown(KeyCode.Alpha9))
            TestMovingHead(4);
        else if (Input.GetKeyDown(KeyCode.P))
            TestStaticProjector();
        else if (Input.GetKeyDown(KeyCode.F5))
            BlackOutLyres();
    }

    /// <summary>Local + sync eHub (clavier ou boutons UI).</summary>
    public void RequestSwitchMode(StartMode mode)
    {
        EHubSyncBus.PublishLocal(new EHubMessage { type = EHubMessageTypes.SwitchMode, intArg = (int)mode });
        ApplySwitchMode(mode);
    }

    /// <summary>Local + sync eHub (clavier ou boutons UI).</summary>
    public void RequestDebugColor(int colorCode)
    {
        EHubSyncBus.PublishLocal(new EHubMessage { type = EHubMessageTypes.DebugColor, intArg = colorCode });
        ApplyDebugColor(colorCode);
    }

    /// <summary>Appelé localement ou via eHub (autre poste).</summary>
    public void ApplySwitchMode(StartMode mode)
    {
        switch (mode)
        {
            case StartMode.Timeline:      SwitchToTimeline(); break;
            case StartMode.Debug:         SwitchToDebug(); break;
            case StartMode.Manual:        SwitchToAudioReactive(); break;
            case StartMode.VideoCapture:  SwitchToVideoCapture(); break;
            case StartMode.EHub:          SwitchToEHub(); break;
        }
        RefreshDisplay();
    }

    /// <summary>Appelé localement ou via eHub (autre poste).</summary>
    public void ApplyDebugColor(int colorCode)
    {
        if (_currentMode != StartMode.Debug)
        {
            Debug.Log("[PixelHubBootstrapper] DebugColor ignoré (mode non-debug). Appuyez sur D pour activer les tests couleur.");
            return;
        }

        switch (colorCode)
        {
            case EHubDebugColor.Red:
                _debugPanel?.SendTestColor(Color.red);
                SyncPreview(_debugPanel, "Debug — Rouge");
                break;
            case EHubDebugColor.Green:
                _debugPanel?.SendTestColor(Color.green);
                SyncPreview(_debugPanel, "Debug — Vert");
                break;
            case EHubDebugColor.Blue:
                _debugPanel?.SendTestColor(Color.blue);
                SyncPreview(_debugPanel, "Debug — Bleu");
                break;
            case EHubDebugColor.BlackOut:
                _debugPanel?.SendBlackOut();
                SyncPreview(_debugPanel, "Debug — Off");
                break;
            case EHubDebugColor.FirstLed:
                _debugPanel?.SendFirstLedTest(Color.red);
                SyncPreview(_debugPanel, "Debug — 1ère LED rouge");
                break;
        }
        RefreshDisplay();
    }

    private void TestMovingHead(int headIndex)
    {
        _otherDevices?.TestMovingHead(headIndex);
        SyncPreview(_debugPanel, $"Lyres — MovingHead{headIndex} ON");
    }

    private void TestStaticProjector()
    {
        _otherDevices?.TestStaticProjector();
        SyncPreview(_debugPanel, "Lyres — StaticProjector ON");
    }

    private void BlackOutLyres()
    {
        _otherDevices?.BlackOutAllLyres();
        SyncPreview(_debugPanel, "Lyres — OFF");
    }

    private void EnsureComponents()
    {
        if (_configManager == null)
            _configManager = FindObjectOfType<ConfigManager>() ?? gameObject.AddComponent<ConfigManager>();
        if (_routingEngine == null)
            _routingEngine = GetComponent<RoutingEngine>() ?? gameObject.AddComponent<RoutingEngine>();
        if (_showTimeline == null)
            _showTimeline = GetComponent<ShowTimeline>() ?? gameObject.AddComponent<ShowTimeline>();
        if (_debugPanel == null)
            _debugPanel = GetComponent<DebugPanel>() ?? gameObject.AddComponent<DebugPanel>();
        if (_eHubReceiver == null)
            _eHubReceiver = GetComponent<EHubReceiver>() ?? gameObject.AddComponent<EHubReceiver>();
        if (_audioReactive == null)
            _audioReactive = GetComponent<AudioReactiveProvider>() ?? gameObject.AddComponent<AudioReactiveProvider>();
        if (_videoCapture == null)
            _videoCapture = FindObjectOfType<VideoCaptureProvider>();
        if (_otherDevices == null)
            _otherDevices = GetComponent<OtherDevicesPanel>() ?? gameObject.AddComponent<OtherDevicesPanel>();
    }

    public void SwitchToTimeline()
    {
        _routingEngine.StopRoutingThread();
        _showTimeline.LoadShow();
        _showTimeline.Play();
        SetProviderWithDevices(_showTimeline);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Timeline;
        SyncPreview(_showTimeline, "Timeline");
        Debug.Log("[PixelHubBootstrapper] Mode Timeline actif.");
    }

    public void SwitchToDebug()
    {
        _routingEngine.StopRoutingThread();
        _debugPanel.SetFakeStateActive(true);
        SetProviderWithDevices(_debugPanel);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Debug;
        SyncPreview(_debugPanel, "Debug");
        Debug.Log("[PixelHubBootstrapper] Mode Debug actif.");
    }

    public void SwitchToEHub()
    {
        _routingEngine.StopRoutingThread();
        SetProviderWithDevices(_eHubReceiver);
        _routingEngine.StartRouting();
        _currentMode = StartMode.EHub;
        UpdatePreviewProvider();
        Debug.Log("[PixelHubBootstrapper] Mode eHuB actif.");
    }

    public void SwitchToAudioReactive()
    {
        _routingEngine.StopRoutingThread();

        var director = FindObjectOfType<PlayableDirector>();
        if (director != null)
        {
            var src = director.GetComponent<AudioSource>();
            if (src != null)
                _audioReactive.SetAudioSource(src);
            // Une seule source : la Timeline pilote l'AudioSource (pas de src.Play() en plus).
            if (director.state != PlayState.Playing)
                director.Play();
        }

        _audioReactive?.ResetIntro();

        SetProviderWithDevices(_audioReactive);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Manual;
        SyncPreview(_audioReactive, "Audio-reactif — pump/kick");
        Debug.Log("[PixelHubBootstrapper] Mode Audio-réactif actif (basses/kicks).");
    }

    public void SwitchToVideoCapture()
    {
        if (_videoCapture == null) _videoCapture = FindObjectOfType<VideoCaptureProvider>();
        if (_videoCapture == null) return;

        _routingEngine.StopRoutingThread();
        SetProviderWithDevices(_videoCapture);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Manual;
        SyncPreview(_videoCapture, "Video Capture (Feux d'artifice)");
        Debug.Log("[PixelHubBootstrapper] Mode Video Capture actif (Caméra -> LEDs).");
    }

    private void SetProviderWithDevices(IStateProvider baseProvider)
    {
        var composite = new Laps.Core.CompositeStateProvider(baseProvider, _otherDevices);
        _routingEngine.SetStateProvider(composite);
    }

    private void UpdatePreviewProvider()
    {
        if (_currentMode == StartMode.Timeline)
            SyncPreview(_showTimeline, "Timeline");
        else if (_currentMode == StartMode.Debug)
            SyncPreview(_debugPanel, "Debug");
        else if (_currentMode == StartMode.EHub)
            SyncPreview(_eHubReceiver, "eHuB — UDP");
    }

    private void SyncPreview(IStateProvider provider, string label)
    {
        if (_previewOverlay == null || provider == null) return;
        _previewOverlay.SetProvider(provider, label);
    }

    /// <summary>Force la mise à jour de l'aperçu Game sur ce poste (sync eHub).</summary>
    public void RefreshDisplay()
    {
        _previewOverlay?.ForceRefresh();
    }

    public void PlayShow()    => _showTimeline?.Play();
    public void PauseShow()   => _showTimeline?.Pause();
    public void StopShow()    => _showTimeline?.Stop();
    public void ReloadConfig() => _configManager?.LoadConfig();
}
