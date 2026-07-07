using UnityEngine;
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

    private LedPreviewOverlay _previewOverlay;

    [Header("Mode de démarrage")]
    [SerializeField] private StartMode _startMode = StartMode.Timeline;

    private StartMode _currentMode;

    public enum StartMode
    {
        Timeline,    // Authoring classique via ShowTimeline
        Debug,       // Fake state via DebugPanel (pour tester les contrôleurs)
        Manual       // L'utilisateur choisit via l'UI
    }

    private void Awake()
    {
        // Auto-câblage si la scène n'a que ConfigManager (cas actuel du projet)
        EnsureComponents();
    }

    private void Start()
    {
        // La config est chargée par ConfigManager.Awake() — on attend juste qu'elle soit prête
        if (ConfigManager.Config == null)
        {
            Debug.LogError("[PixelHubBootstrapper] ConfigManager non trouvé ou config non chargée !");
            return;
        }

        // Connecter le DebugPanel au RoutingEngine
        _debugPanel?.SetRoutingEngine(_routingEngine);

        // Choisir le state provider selon le mode
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

            case StartMode.Manual:
                _currentMode = StartMode.Manual;
                Debug.Log("[PixelHubBootstrapper] Mode Manuel — en attente de sélection via l'UI.");
                break;
        }

        // Démarrer le thread de routage
        _routingEngine.StartRouting();

        // Prévisualisation à l'écran (onglet Game)
        _previewOverlay = GetComponent<LedPreviewOverlay>() ?? gameObject.AddComponent<LedPreviewOverlay>();
        _previewOverlay.Init(_routingEngine);
        UpdatePreviewProvider();

        Debug.Log("[PixelHubBootstrapper] PixelHub démarré avec succès !");
        Debug.Log("[PixelHubBootstrapper] → Onglet GAME pour voir l'aperçu. Touches : 1=1ère LED | R/G/B | 0=off | T=timeline");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
            SwitchToTimeline();
        else if (Input.GetKeyDown(KeyCode.D))
            SwitchToDebug();
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
    }

    // ── API pour l'UI ─────────────────────────────────────────

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

    private void UpdatePreviewProvider()
    {
        if (_previewOverlay == null) return;
        if (_currentMode == StartMode.Timeline)
            _previewOverlay.SetProvider(_showTimeline, "Timeline — le continent");
        else if (_currentMode == StartMode.Debug)
            _previewOverlay.SetProvider(_debugPanel, "Debug");
    }

    public void PlayShow()    => _showTimeline?.Play();
    public void PauseShow()   => _showTimeline?.Pause();
    public void StopShow()    => _showTimeline?.Stop();
    public void ReloadConfig() => _configManager?.LoadConfig();
}
