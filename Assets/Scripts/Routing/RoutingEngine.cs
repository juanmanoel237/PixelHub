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
        private readonly Dictionary<int, int> _controllerPatch = new Dictionary<int, int>(8);

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

        public PixelMapping Mapping => _pixelMapping;

        private readonly object _debugLock = new object();
        private RoutingDebugSnapshot _debugSnapshot = new RoutingDebugSnapshot();

        /// <summary>Dernier instantané DMX (panneau debug F7).</summary>
        public bool TryGetDebugSnapshot(out RoutingDebugSnapshot snapshot)
        {
            lock (_debugLock)
            {
                snapshot = CloneDebugSnapshot(_debugSnapshot);
                return snapshot != null;
            }
        }

        /// <summary>État LED final (animation + feux d'artifice) tel qu'envoyé en Art-Net.</summary>
        public bool TryGetDisplaySnapshot(out Color32[] snapshot)
        {
            lock (_lock)
            {
                if (_readBuffer == null || _readBuffer.Length == 0)
                {
                    snapshot = null;
                    return false;
                }

                snapshot = new Color32[_readBuffer.Length];
                Array.Copy(_readBuffer, snapshot, _readBuffer.Length);
                return true;
            }
        }

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

            LedFireworks.Tick(Time.deltaTime);
            LedTextOverlay.Tick(Time.deltaTime);

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

                    var map = ConfigManager.Config?.mapping;
                    if (map != null)
                    {
                        int w = map.screenWidth > 0 ? map.screenWidth : 128;
                        int h = map.screenHeight > 0 ? map.screenHeight : 128;
                        LedFireworks.CompositeOnto(_writeBuffer, w, h);
                        LedTextOverlay.CompositeOnto(_writeBuffer, w, h);
                        VideoOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        ConfettiOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        NeigeOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        MaisonsOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        FlashOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        EclatOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        EclaireDroiteOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        EclaireGaucheOverlayCompositor.CompositeOnto(_writeBuffer, w, h);
                        if (map.flipY) LedBufferTransforms.FlipBufferY(_writeBuffer, w, h);
                        if (map.flipX) LedBufferTransforms.FlipBufferX(_writeBuffer, w, h);
                    }

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
                    if (lyreState == null) continue;

                    LyreConfig lyreCfg = FindLyreConfig(lyreState.lyreName, config);
                    if (lyreCfg == null) continue;

                    int lyreControllerIndex = RemapController(FindControllerIndexByIp(lyreCfg.controllerIp, config));
                    if (lyreControllerIndex < 0) continue;

                    int lyreKey = (lyreControllerIndex << 16) | (lyreCfg.universe & 0xFFFF);
                    if (!_dmxBuffers.TryGetValue(lyreKey, out byte[] buf))
                    {
                        buf = new byte[512];
                        _dmxBuffers[lyreKey] = buf;
                    }

                    int ch = lyreCfg.startChannel - 1;
                    byte dim = (byte)Mathf.Clamp(lyreState.dimmer, 0, 255);

                    if (lyreState.lyreName == "StaticProjector")
                    {
                        if (dim == 0) continue;

                        buf[ch + 0] = lyreState.color.r;
                        buf[ch + 1] = lyreState.color.g;
                        buf[ch + 2] = lyreState.color.b;
                        buf[ch + 3] = dim;
                    }
                    else
                    {
                        buf[ch + 0] = (byte)Mathf.Clamp(lyreState.pan, 0, 255);
                        buf[ch + 1] = (byte)Mathf.Clamp(lyreState.tilt, 0, 255);
                        if (dim == 0) continue;

                        byte stro = (byte)Mathf.Clamp(lyreState.strobe, 0, 255);
                        byte gobo = (byte)Mathf.Clamp(lyreState.gobo, 0, 255);

                        WriteIfInRange(buf, ch + 2, dim);
                        WriteIfInRange(buf, ch + 5, dim);
                        WriteIfInRange(buf, ch + 3, lyreState.color.r);
                        WriteIfInRange(buf, ch + 4, lyreState.color.g);
                        WriteIfInRange(buf, ch + 5, lyreState.color.b);
                        WriteIfInRange(buf, ch + 7, lyreState.color.r);
                        WriteIfInRange(buf, ch + 8, lyreState.color.g);
                        WriteIfInRange(buf, ch + 9, lyreState.color.b);
                        WriteIfInRange(buf, ch + 6, stro);
                        WriteIfInRange(buf, ch + 10, stro);
                        WriteIfInRange(buf, ch + 7, gobo);
                        WriteIfInRange(buf, ch + 11, gobo);
                    }
                }
            }

            CaptureDebugSnapshot(state, entities, config);

            if (!EHubStatus.ShouldOutputToHardware)
                return;

            _artNetSender.BeginFrame();

            _universesToSend.Clear();
            foreach (var kvp in _dmxBuffers)
                _universesToSend.Add(kvp.Key);
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

                int ctrl = RemapController(addr.controllerIndex);
                int key = DmxBufferKey(ctrl, addr.universe);
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

                int ctrl = RemapController(addr.controllerIndex);
                int key = DmxBufferKey(ctrl, addr.universe);
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

        private void CaptureDebugSnapshot(Color32[] state, IReadOnlyList<EntityColor> entities, AppConfig config)
        {
            var snap = new RoutingDebugSnapshot
            {
                HardwareOutputEnabled = EHubStatus.ShouldOutputToHardware,
                PacketsPerSecond = PacketsPerSecond,
                PacketsSentTotal = PacketsSentTotal,
                RoutingFps = RoutingFps
            };

            bool entityMode = entities != null && entities.Count > 0 &&
                              ConfigManager.EntityMap != null && ConfigManager.EntityMap.Count > 0;
            if (entityMode)
            {
                snap.Mode = RoutingDebugMode.Entity;
                snap.EntityReceived = entities.Count;
                var unmapped = new List<int>(8);
                for (int i = 0; i < entities.Count; i++)
                {
                    if (ConfigManager.EntityMap.TryGet(entities[i].id, out _))
                        snap.EntityMapped++;
                    else if (unmapped.Count < 8)
                        unmapped.Add(entities[i].id);
                }
                snap.EntityUnmapped = snap.EntityReceived - snap.EntityMapped;
                snap.UnmappedEntityIds = unmapped.ToArray();
            }
            else if (state != null)
            {
                snap.Mode = RoutingDebugMode.Pixel;
            }

            if (_pixelMapping?.PixelMap != null && _pixelMapping.PixelMap.Length > 0 &&
                _pixelMapping.PixelMap[0].controllerIndex >= 0)
            {
                snap.HasFirstPixelAddress = true;
                snap.FirstPixelAddress = _pixelMapping.PixelMap[0];
            }

            if (config?.network?.controllers != null)
            {
                foreach (var kvp in _dmxBuffers)
                {
                    byte[] buf = kvp.Value;
                    if (buf == null || !HasNonZeroData(buf)) continue;

                    int ctrl = kvp.Key >> 16;
                    int universe = kvp.Key & 0xFFFF;
                    int active = 0;
                    int first = -1;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (buf[i] == 0) continue;
                        active++;
                        if (first < 0) first = i;
                    }

                    string ip = ctrl >= 0 && ctrl < config.network.controllers.Length
                        ? config.network.controllers[ctrl].ip ?? "?"
                        : "?";

                    snap.Universes.Add(new DmxUniverseSummary
                    {
                        key = kvp.Key,
                        controllerIndex = ctrl,
                        universe = universe,
                        controllerIp = ip,
                        activeChannelCount = active,
                        firstActiveChannel = first
                    });

                    var copy = new byte[512];
                    Array.Copy(buf, copy, 512);
                    snap.DmxBuffers[kvp.Key] = copy;
                }
            }

            snap.Universes.Sort((a, b) =>
            {
                int c = a.controllerIndex.CompareTo(b.controllerIndex);
                return c != 0 ? c : a.universe.CompareTo(b.universe);
            });
            snap.ActiveUniverseCount = snap.Universes.Count;

            lock (_debugLock)
            {
                _debugSnapshot = snap;
            }
        }

        private static RoutingDebugSnapshot CloneDebugSnapshot(RoutingDebugSnapshot src)
        {
            if (src == null) return new RoutingDebugSnapshot();

            var clone = new RoutingDebugSnapshot
            {
                Mode = src.Mode,
                HardwareOutputEnabled = src.HardwareOutputEnabled,
                PacketsPerSecond = src.PacketsPerSecond,
                PacketsSentTotal = src.PacketsSentTotal,
                RoutingFps = src.RoutingFps,
                ActiveUniverseCount = src.ActiveUniverseCount,
                EntityReceived = src.EntityReceived,
                EntityMapped = src.EntityMapped,
                EntityUnmapped = src.EntityUnmapped,
                UnmappedEntityIds = src.UnmappedEntityIds != null
                    ? (int[])src.UnmappedEntityIds.Clone()
                    : Array.Empty<int>(),
                FirstPixelAddress = src.FirstPixelAddress,
                HasFirstPixelAddress = src.HasFirstPixelAddress
            };

            foreach (var u in src.Universes)
                clone.Universes.Add(u);

            foreach (var kvp in src.DmxBuffers)
            {
                var copy = new byte[512];
                Array.Copy(kvp.Value, copy, 512);
                clone.DmxBuffers[kvp.Key] = copy;
            }

            return clone;
        }

        private LyreConfig FindLyreConfig(string name, AppConfig config)
        {
            if (config.mapping.lyres == null) return null;
            foreach (var l in config.mapping.lyres)
                if (l.name == name) return l;
            return null;
        }

        private static int FindControllerIndexByIp(string ip, AppConfig config)
        {
            if (string.IsNullOrEmpty(ip) || config?.network?.controllers == null) return -1;
            for (int i = 0; i < config.network.controllers.Length; i++)
            {
                if (config.network.controllers[i].ip == ip) return i;
            }
            return -1;
        }

        private static void WriteIfInRange(byte[] buf, int index, byte value)
        {
            if (buf == null) return;
            if (index < 0 || index >= buf.Length) return;
            buf[index] = value;
        }

        private void OnConfigReloaded()
        {
            RebuildMapping();
        }

        private void RebuildMapping()
        {
            bool wasRunning = _running;
            if (wasRunning) StopRoutingThread();

            LoadControllerPatch();
            _pixelMapping.Build(ConfigManager.Config);
            _dmxBuffers.Clear();

            if (wasRunning) StartRouting();
            Debug.Log("[RoutingEngine] Mapping reconstruit après rechargement de config.");
        }

        private void LoadControllerPatch()
        {
            _controllerPatch.Clear();
            var entries = ConfigManager.Config?.router?.controllerPatch;
            if (entries == null) return;

            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.toController < 0) continue;
                _controllerPatch[e.fromController] = e.toController;
            }

            if (_controllerPatch.Count > 0)
                Debug.Log($"[RoutingEngine] Patch map actif : {_controllerPatch.Count} reroutage(s) contrôleur.");
        }

        private int RemapController(int controllerIndex)
        {
            if (_controllerPatch.TryGetValue(controllerIndex, out int mapped))
                return mapped;
            return controllerIndex;
        }
    }
}
