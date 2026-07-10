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

        // Buffers DMX par (contrôleur, univers) — évite les allocations en boucle
        // Key = (controllerIndex<<16) | (universe&0xFFFF), Value = tableau de 512 octets
        private Dictionary<int, byte[]> _dmxBuffers = new Dictionary<int, byte[]>();
        private readonly List<int> _universesToSend = new List<int>(256);

        // Double buffer : le main thread écrit dans _writeBuffer, le thread routage lit _readBuffer
        private Color32[] _readBuffer;
        private Color32[] _writeBuffer;
        private Color32[] _routingCopy;
        private LyreState[] _lyreSnapshot;
        private IReadOnlyList<EntityColor> _entitySnapshot;
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

            Color32[] state = _stateProvider.GetState();
            LyreState[] lyres = _stateProvider.GetLyreStates();
            IReadOnlyList<EntityColor> entities = null;
            if (_stateProvider is IEntityStateProvider entityProvider)
                entities = entityProvider.GetEntityState();

            lock (_lock)
            {
                if (state != null)
                {
                    if (_writeBuffer == null || _writeBuffer.Length != state.Length)
                        _writeBuffer = new Color32[state.Length];

                    Array.Copy(state, _writeBuffer, state.Length);

                    Color32[] tmp = _readBuffer;
                    _readBuffer = _writeBuffer;
                    _writeBuffer = tmp;
                }

                _lyreSnapshot = lyres;
                _entitySnapshot = entities;
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

                Color32[] snapshot;
                LyreState[] lyres;
                IReadOnlyList<EntityColor> entities;

                lock (_lock)
                {
                    entities = _entitySnapshot;
                    lyres = _lyreSnapshot;

                    if (_readBuffer == null)
                    {
                        snapshot = null;
                    }
                    else
                    {
                        if (_routingCopy == null || _routingCopy.Length != _readBuffer.Length)
                            _routingCopy = new Color32[_readBuffer.Length];
                        Array.Copy(_readBuffer, _routingCopy, _readBuffer.Length);
                        snapshot = _routingCopy;
                    }
                }

                if (snapshot != null && _pixelMapping != null)
                    RouteState(snapshot, lyres, entities);
                else if (entities != null && entities.Count > 0 && ConfigManager.EntityMap?.Count > 0)
                    RouteState(null, lyres, entities);

                sw.Stop();
                float elapsed = (float)sw.Elapsed.TotalSeconds;
                RoutingMs  = elapsed * 1000f;
                RoutingFps = 1f / Math.Max(elapsed, 0.0001f);
                _artNetSender?.UpdateStats(elapsed);

                float remaining = interval - elapsed;
                if (remaining > 0.001f)
                    Thread.Sleep((int)(remaining * 1000));
            }
        }

        /// <summary>
        /// Convertit le snapshot Color32[] en paquets DMX et les envoie.
        /// </summary>
        private void RouteState(Color32[] state, LyreState[] lyres, IReadOnlyList<EntityColor> entities)
        {
            var config = ConfigManager.Config;
            if (config == null || _pixelMapping.PixelMap == null) return;

            foreach (var buf in _dmxBuffers.Values)
                Array.Clear(buf, 0, buf.Length);

            // Mode entité (eHuB + CSV) ou mode pixel (authoring/debug)
            if (entities != null && entities.Count > 0 && ConfigManager.EntityMap != null && ConfigManager.EntityMap.Count > 0)
                WriteEntitiesToDmx(entities, config);
            else if (state != null)
                WritePixelsToDmx(state);

            if (lyres != null && config.mapping.lyres != null)
            {
                foreach (var lyreState in lyres)
                {
                    LyreConfig lyreCfg = FindLyreConfig(lyreState.lyreName, config);
                    if (lyreCfg == null) continue;

                    int ctrlIndex = FindControllerIndexByIp(config, lyreCfg.controllerIp);
                    if (ctrlIndex < 0) continue;

                    int key = DmxBufferKey(ctrlIndex, lyreCfg.universe);
                    if (!_dmxBuffers.TryGetValue(key, out byte[] buf))
                    {
                        buf = new byte[512];
                        _dmxBuffers[key] = buf;
                    }

                    int ch = lyreCfg.startChannel - 1;
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

            _artNetSender.BeginFrame();

            _universesToSend.Clear();
            foreach (var kvp in _dmxBuffers)
            {
                if (HasNonZeroData(kvp.Value))
                    _universesToSend.Add(kvp.Key);
            }
            _universesToSend.Sort();

            for (int i = 0; i < _universesToSend.Count; i++)
            {
                int key = _universesToSend[i];
                int controllerIndex = key >> 16;
                int universe = key & 0xFFFF;
                byte[] dmxData = _dmxBuffers[key];

                if (config.network.controllers == null ||
                    controllerIndex < 0 ||
                    controllerIndex >= config.network.controllers.Length)
                    continue;

                string ip = config.network.controllers[controllerIndex].ip;
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

                int key = DmxBufferKey(addr.controllerIndex, addr.universe);
                if (!_dmxBuffers.TryGetValue(key, out byte[] buf))
                {
                    buf = new byte[512];
                    _dmxBuffers[key] = buf;
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
                if (addr.controllerIndex < 0) continue;

                int key = DmxBufferKey(addr.controllerIndex, addr.universe);
                if (!_dmxBuffers.TryGetValue(key, out byte[] buf))
                {
                    buf = new byte[512];
                    _dmxBuffers[key] = buf;
                }

                if (addr.channel < 0 || addr.channel + 2 >= 512) continue;

                Color32 c = e.color;
                buf[addr.channel]     = c.r;
                buf[addr.channel + 1] = c.g;
                buf[addr.channel + 2] = c.b;
                if (channels == 4 && addr.channel + 3 < 512)
                    buf[addr.channel + 3] = 0;
            }
        }

        private static int DmxBufferKey(int controllerIndex, int universe) =>
            (controllerIndex << 16) | (universe & 0xFFFF);

        private static bool HasNonZeroData(byte[] dmxData)
        {
            for (int i = 0; i < dmxData.Length; i++)
                if (dmxData[i] != 0) return true;
            return false;
        }

        private static int FindControllerIndexByIp(AppConfig config, string ip)
        {
            if (config?.network?.controllers == null || string.IsNullOrEmpty(ip)) return -1;
            for (int i = 0; i < config.network.controllers.Length; i++)
            {
                if (config.network.controllers[i].ip == ip)
                    return i;
            }
            return -1;
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
