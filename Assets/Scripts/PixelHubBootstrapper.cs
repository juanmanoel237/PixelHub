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
                _routingEngine.SetStateProvider(_showTimeline);
                _currentMode = StartMode.Timeline;
                Debug.Log("[PixelHubBootstrapper] Mode Timeline actif.");
                break;

            case StartMode.Debug:
                _routingEngine.SetStateProvider(_debugPanel);
                _debugPanel.SetFakeStateActive(true);
                _currentMode = StartMode.Debug;
                Debug.Log("[PixelHubBootstrapper] Mode Debug actif (fake state).");
                break;

            case StartMode.EHub:
                _routingEngine.SetStateProvider(_eHubReceiver);
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
        Debug.Log("[PixelHubBootstrapper] → Onglet GAME pour voir l'aperçu. Touches : 1=1ère LED | R/G/B | 0=off | T=timeline | E=eHuB | A=audio | V=video");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            SwitchToTimeline();
        else if (Input.GetKeyDown(KeyCode.D))
            SwitchToDebug();
        else if (Input.GetKeyDown(KeyCode.E))
            SwitchToEHub();
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Q))
            SwitchToAudioReactive();
        else if (Input.GetKeyDown(KeyCode.V))
            SwitchToVideoCapture();
        else if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SwitchToDebug();
            _debugPanel?.SendFirstLedTest(Color.red);
            _previewOverlay?.SetProvider(_debugPanel, "Debug — 1ère LED rouge");
        }
        else if (Input.GetKeyDown(KeyCode.R))
        {
            SwitchToDebug();
            _debugPanel?.SendTestColor(Color.red);
        }
        else if (Input.GetKeyDown(KeyCode.G))
        {
            SwitchToDebug();
            _debugPanel?.SendTestColor(Color.green);
        }
        else if (Input.GetKeyDown(KeyCode.B))
        {
            SwitchToDebug();
            _debugPanel?.SendTestColor(Color.blue);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            SwitchToDebug();
            _debugPanel?.SendBlackOut();
        }
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
    }

    public void SwitchToTimeline()
    {
        _routingEngine.StopRoutingThread();
        _showTimeline.LoadShow();
        _showTimeline.Play();
        _routingEngine.SetStateProvider(_showTimeline);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Timeline;
        UpdatePreviewProvider();
        Debug.Log("[PixelHubBootstrapper] Mode Timeline actif.");
    }

    public void SwitchToDebug()
    {
        _routingEngine.StopRoutingThread();
        _debugPanel.SetFakeStateActive(true);
        _routingEngine.SetStateProvider(_debugPanel);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Debug;
        UpdatePreviewProvider();
        Debug.Log("[PixelHubBootstrapper] Mode Debug actif.");
    }

    public void SwitchToEHub()
    {
        _routingEngine.StopRoutingThread();
        _routingEngine.SetStateProvider(_eHubReceiver);
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
            if (director.state != PlayState.Playing)
                director.Play();
        }

        _audioReactive?.ResetIntro();

        _routingEngine.SetStateProvider(_audioReactive);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Manual;
        _previewOverlay?.SetProvider(_audioReactive, "Audio-reactif — pump/kick");
        Debug.Log("[PixelHubBootstrapper] Mode Audio-réactif actif (basses/kicks).");
    }

    public void SwitchToVideoCapture()
    {
        if (_videoCapture == null) _videoCapture = FindObjectOfType<VideoCaptureProvider>();
        if (_videoCapture == null) return;

        _routingEngine.StopRoutingThread();
        _routingEngine.SetStateProvider(_videoCapture);
        _routingEngine.StartRouting();
        _currentMode = StartMode.Manual;
        _previewOverlay?.SetProvider(_videoCapture, "Video Capture (Feux d'artifice)");
        Debug.Log("[PixelHubBootstrapper] Mode Video Capture actif (Caméra -> LEDs).");
    }

    private void UpdatePreviewProvider()
    {
        if (_previewOverlay == null) return;
        if (_currentMode == StartMode.Timeline)
            _previewOverlay.SetProvider(_showTimeline, "Timeline");
        else if (_currentMode == StartMode.Debug)
            _previewOverlay.SetProvider(_debugPanel, "Debug");
        else if (_currentMode == StartMode.EHub)
            _previewOverlay.SetProvider(_eHubReceiver, "eHuB — UDP");
    }

    public void PlayShow()    => _showTimeline?.Play();
    public void PauseShow()   => _showTimeline?.Pause();
    public void StopShow()    => _showTimeline?.Stop();
    public void ReloadConfig() => _configManager?.LoadConfig();
}
