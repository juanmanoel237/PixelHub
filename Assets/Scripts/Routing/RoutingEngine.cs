using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Laps.Core;

namespace Laps.Routing
{
    /// <summary>
    /// Moteur de routage : lit l'état de l'IStateProvider et l'achemine vers les
    /// contrôleurs via ArtNet sur un thread dédié pour ne jamais bloquer le rendu.
    ///
    /// Architecture (P4) :
    ///   IStateProvider → RoutingEngine → PixelMapping → ArtNetSender → Contrôleurs
    ///
    /// Le RoutingEngine ne sait rien de l'authoring (effets, vidéos, etc.).
    /// Il n'accède qu'à Color32[] via IStateProvider.
    ///
    /// Satisfait P2 : Routage ArtNet performant sur thread séparé.
    /// </summary>
    public class RoutingEngine : MonoBehaviour
    {
        // ── Fréquence cible ────────────────────────────────────
        [Header("Performance")]
        [Tooltip("Fréquence cible du thread de routage (Hz). 44Hz minimum pour synchronisation musique.")]
        [SerializeField] private int _targetFps = 44;

        // ── Etat ───────────────────────────────────────────────
        private IStateProvider _stateProvider;
        private PixelMapping    _pixelMapping;
        private ArtNetSender    _artNetSender;

        // Thread de routage dédié
        private Thread  _routingThread;
        private bool    _running;
        private bool    _dirty; // Demande de mise à jour depuis le main thread

        // Buffers DMX par univers — évite les allocations en boucle
        // Key = univers absolu, Value = tableau de 512 octets
        private Dictionary<int, byte[]> _dmxBuffers = new Dictionary<int, byte[]>();

        // Snapshot protégé par lock (copie des couleurs depuis le main thread)
        private Color32[] _snapshot;
        private LyreState[] _lyreSnapshot;
        private readonly object _lock = new object();

        // ── Statistiques (P8) ──────────────────────────────────
        public int    PacketsSentTotal => _artNetSender?.PacketsSent ?? 0;
        public float  PacketsPerSecond => _artNetSender?.PacketsPerSecond ?? 0;
        public float  RoutingFps { get; private set; }
        public float  RoutingMs  { get; private set; }

        // ── Unity Lifecycle ────────────────────────────────────

        private void Awake()
        {
            _pixelMapping = new PixelMapping();
            _artNetSender = new ArtNetSender();
            ConfigManager.OnConfigReloaded += OnConfigReloaded;
        }

        private void Start()
        {
            if (ConfigManager.Config != null)
                RebuildMapping();
        }

        private void OnDestroy()
        {
            StopRoutingThread();
            _artNetSender?.Dispose();
            ConfigManager.OnConfigReloaded -= OnConfigReloaded;
        }

        // ── API publique ───────────────────────────────────────

        /// <summary>
        /// Enregistre le fournisseur d'état (authoring, eHub, etc.).
        /// </summary>
        public void SetStateProvider(IStateProvider provider)
        {
            _stateProvider = provider;
            Debug.Log($"[RoutingEngine] StateProvider enregistré : {provider.GetType().Name}");
        }

        /// <summary>
        /// Démarre le thread de routage.
        /// </summary>
        public void StartRouting()
        {
            if (_running) return;
            _running = true;
            _routingThread = new Thread(RoutingLoop)
            {
                Name = "PixelHub-Routing",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.AboveNormal
            };
            _routingThread.Start();
            Debug.Log($"[RoutingEngine] Thread de routage démarré à {_targetFps} Hz cible.");
        }

        /// <summary>
        /// Arrête le thread de routage.
        /// </summary>
        public void StopRoutingThread()
        {
            _running = false;
            _routingThread?.Join(500);
            _routingThread = null;
        }

        // ── Main Thread Update ─────────────────────────────────

        private void Update()
        {
            if (_stateProvider == null) return;

            // Copier l'état courant dans le snapshot (opération rapide, protégée par lock)
            Color32[] state = _stateProvider.GetState();
            LyreState[] lyres = _stateProvider.GetLyreStates();

            lock (_lock)
            {
                // Resize du snapshot si nécessaire (ex: rechargement de config)
                if (_snapshot == null || _snapshot.Length != state.Length)
                    _snapshot = new Color32[state.Length];

                Array.Copy(state, _snapshot, state.Length);
                _lyreSnapshot = lyres;
                _dirty = true;
            }
        }

        // ── Thread de routage ──────────────────────────────────

        private void RoutingLoop()
        {
            float interval = 1f / _targetFps;
            var sw = new System.Diagnostics.Stopwatch();

            while (_running)
            {
                sw.Restart();

                bool hasWork;
                Color32[] snapshot;
                LyreState[] lyres;

                lock (_lock)
                {
                    hasWork  = _dirty;
                    snapshot = _snapshot;
                    lyres    = _lyreSnapshot;
                    _dirty   = false;
                }

                if (hasWork && snapshot != null && _pixelMapping != null)
                {
                    RouteState(snapshot, lyres);
                }

                sw.Stop();
                float elapsed = (float)sw.Elapsed.TotalSeconds;
                RoutingMs  = elapsed * 1000f;
                RoutingFps = hasWork ? 1f / Math.Max(elapsed, 0.0001f) : RoutingFps;
                _artNetSender?.UpdateStats(elapsed);

                // Attente précise pour respecter la fréquence cible
                float remaining = interval - elapsed;
                if (remaining > 0.001f)
                    Thread.Sleep((int)(remaining * 1000));
            }
        }

        /// <summary>
        /// Convertit le snapshot Color32[] en paquets DMX et les envoie.
        /// </summary>
        private void RouteState(Color32[] state, LyreState[] lyres)
        {
            var config = ConfigManager.Config;
            if (config == null || _pixelMapping.PixelMap == null) return;

            // ── 1. Effacer tous les buffers DMX ──────────────
            foreach (var buf in _dmxBuffers.Values)
                Array.Clear(buf, 0, buf.Length);

            // ── 2. Écrire l'écran LED dans les buffers DMX ─────
            // Mode A (actuel): state indexé par pixel (Color32[])
            // Mode B (aligné eHuB/Excel): state indexé par entité (id → couleur) via EntityMapping CSV
            if (_stateProvider is IEntityStateProvider entityProvider && ConfigManager.EntityMap != null && ConfigManager.EntityMap.Count > 0)
            {
                WriteEntitiesToDmx(entityProvider.GetEntityState(), config);
            }
            else
            {
                WritePixelsToDmx(state);
            }

            // ── 3. Écrire les lyres dans leurs buffers ───────
            if (lyres != null && config.mapping.lyres != null)
            {
                foreach (var lyreState in lyres)
                {
                    LyreConfig lyreCfg = FindLyreConfig(lyreState.lyreName, config);
                    if (lyreCfg == null) continue;

                    if (!_dmxBuffers.TryGetValue(lyreCfg.universe, out byte[] buf))
                    {
                        buf = new byte[512];
                        _dmxBuffers[lyreCfg.universe] = buf;
                    }

                    int ch = lyreCfg.startChannel - 1; // DMX 1-based → 0-based
                    buf[ch + 0] = (byte)Mathf.Clamp(lyreState.pan,    0, 255);
                    buf[ch + 1] = (byte)Mathf.Clamp(lyreState.tilt,   0, 255);
                    buf[ch + 2] = (byte)Mathf.Clamp(lyreState.dimmer, 0, 255);
                    buf[ch + 3] = lyreState.color.r;
                    buf[ch + 4] = lyreState.color.g;
                    buf[ch + 5] = lyreState.color.b;
                    buf[ch + 6] = (byte)Mathf.Clamp(lyreState.strobe, 0, 255);
                    buf[ch + 7] = (byte)Mathf.Clamp(lyreState.gobo,   0, 255);
                }
            }

            // ── 4. Envoyer les paquets ArtNet (uniquement les univers non vides) ─
            foreach (var kvp in _dmxBuffers)
            {
                int universe = kvp.Key;
                byte[] dmxData = kvp.Value;

                if (!HasNonZeroData(dmxData)) continue;

                // Trouver le contrôleur responsable de cet univers
                string ip = FindControllerIp(universe, config);
                if (string.IsNullOrEmpty(ip)) continue;

                _artNetSender.SendUniverse(ip, universe, dmxData);
            }
        }

        private void WritePixelsToDmx(Color32[] state)
        {
            int channels = _pixelMapping.ChannelsPerLed;

            for (int i = 0; i < state.Length && i < _pixelMapping.LedCount; i++)
            {
                ref LEDAddress addr = ref _pixelMapping.PixelMap[i];
                if (addr.controllerIndex < 0) continue;

                if (!_dmxBuffers.TryGetValue(addr.universe, out byte[] buf))
                {
                    buf = new byte[512];
                    _dmxBuffers[addr.universe] = buf;
                }

                Color32 c = state[i];
                buf[addr.channel]     = c.r;
                buf[addr.channel + 1] = c.g;
                buf[addr.channel + 2] = c.b;
                if (channels == 4)
                    buf[addr.channel + 3] = 0;
            }
        }

        private void WriteEntitiesToDmx(IReadOnlyList<EntityColor> entities, AppConfig config)
        {
            if (entities == null || entities.Count == 0) return;

            int channels = config.mapping.channelsPerLed > 0 ? config.mapping.channelsPerLed : 3;

            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (!ConfigManager.EntityMap.TryGet(e.id, out var addr)) continue;

                if (!_dmxBuffers.TryGetValue(addr.universe, out byte[] buf))
                {
                    buf = new byte[512];
                    _dmxBuffers[addr.universe] = buf;
                }

                // Sécurité
                if (addr.channel < 0 || addr.channel + 2 >= 512) continue;

                Color32 c = e.color;
                buf[addr.channel]     = c.r;
                buf[addr.channel + 1] = c.g;
                buf[addr.channel + 2] = c.b;
                if (channels == 4 && addr.channel + 3 < 512)
                    buf[addr.channel + 3] = 0;
            }
        }

        private static bool HasNonZeroData(byte[] dmxData)
        {
            for (int i = 0; i < dmxData.Length; i++)
                if (dmxData[i] != 0) return true;
            return false;
        }

        // ── Helpers ────────────────────────────────────────────

        private string FindControllerIp(int universe, AppConfig config)
        {
            foreach (var ctrl in config.network.controllers)
            {
                int count = ctrl.universeCount > 0 ? ctrl.universeCount : 32;
                if (universe >= ctrl.startUniverse && universe < ctrl.startUniverse + count)
                    return ctrl.ip;
            }
            return null;
        }

        private LyreConfig FindLyreConfig(string name, AppConfig config)
        {
            if (config.mapping.lyres == null) return null;
            foreach (var l in config.mapping.lyres)
                if (l.name == name) return l;
            return null;
        }

        private void OnConfigReloaded()
        {
            RebuildMapping();
        }

        private void RebuildMapping()
        {
            bool wasRunning = _running;
            if (wasRunning) StopRoutingThread();

            _pixelMapping.Build(ConfigManager.Config);
            _dmxBuffers.Clear();

            if (wasRunning) StartRouting();
            Debug.Log("[RoutingEngine] Mapping reconstruit après rechargement de config.");
        }
    }
}
