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

    [Header("Mode de démarrage")]
    [SerializeField] private StartMode _startMode = StartMode.Timeline;

    public enum StartMode
    {
        Timeline,    // Authoring classique via ShowTimeline
        Debug,       // Fake state via DebugPanel (pour tester les contrôleurs)
        Manual       // L'utilisateur choisit via l'UI
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
                _showTimeline.Play(); // Démarre la lecture automatiquement
                _routingEngine.SetStateProvider(_showTimeline);
                Debug.Log("[PixelHubBootstrapper] Mode Timeline actif.");
                break;

            case StartMode.Debug:
                _routingEngine.SetStateProvider(_debugPanel);
                _debugPanel.SetFakeStateActive(true);
                Debug.Log("[PixelHubBootstrapper] Mode Debug actif (fake state).");
                break;

            case StartMode.Manual:
                Debug.Log("[PixelHubBootstrapper] Mode Manuel — en attente de sélection via l'UI.");
                break;
        }

        // Démarrer le thread de routage
        _routingEngine.StartRouting();
        Debug.Log("[PixelHubBootstrapper] PixelHub démarré avec succès !");
    }

    // ── API pour l'UI ─────────────────────────────────────────

    public void SwitchToTimeline()
    {
        _routingEngine.StopRoutingThread();
        _routingEngine.SetStateProvider(_showTimeline);
        _routingEngine.StartRouting();
    }

    public void SwitchToDebug()
    {
        _routingEngine.StopRoutingThread();
        _debugPanel.SetFakeStateActive(true);
        _routingEngine.SetStateProvider(_debugPanel);
        _routingEngine.StartRouting();
    }

    public void PlayShow()    => _showTimeline?.Play();
    public void PauseShow()   => _showTimeline?.Pause();
    public void StopShow()    => _showTimeline?.Stop();
    public void ReloadConfig() => _configManager?.LoadConfig();
}
