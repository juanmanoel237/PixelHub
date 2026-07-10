using UnityEngine;
using UnityEngine.UI;
using System.Text;
using Laps.Core;
using Laps.Routing;

namespace Laps.Authoring
{
    [DefaultExecutionOrder(-50)]
    /// <summary>
    /// Panneau de débogage en temps réel (P8).
    ///
    /// Fonctionnalités :
    ///   - Stats réseau (paquets/s, FPS routage, latence)
    ///   - Fake state generator : envoyer une couleur unie pour tester les contrôleurs
    ///   - Visualisation d'un univers DMX (grille 2D de 512 canaux)
    ///   - Moniteur de l'état LED courant (mini-écran)
    /// </summary>
    public class DebugPanel : MonoBehaviour, IStateProvider
    {
        // ── Références UI ────────────────────────────────────────
        [Header("UI References")]
        [SerializeField] private Text  _statsText;
        [SerializeField] private Text  _universeText;
        [SerializeField] private RawImage _miniScreen;    // Texture de prévisualisation LED
        [SerializeField] private Slider   _universeSlider;

        // ── Fake State ───────────────────────────────────────────
        [Header("Fake State Generator")]
        [SerializeField] private bool  _fakeStateActive;
        [SerializeField] private Color _fakeColor = Color.red;
        [SerializeField] private EffectType _fakeEffect = EffectType.SolidColor;
        [SerializeField] private bool _firstLedOnly = false; // true = comme send-artnet.js (1ère LED seulement)

        // ── Références internes ──────────────────────────────────
        private RoutingEngine _routingEngine;
        private Color32[] _fakeState;
        private LyreState[] _emptyLyres = new LyreState[0];
        private Texture2D _previewTexture;

        private Color _lastFakeColor;
        private EffectType _lastFakeEffect;
        private bool _lastFirstLedOnly;
        private int _screenWidth;
        private int _screenHeight;
        private float _statsTimer;

        // ── IStateProvider (mode Fake) ───────────────────────────
        public Color32[] GetState()      => _fakeState;
        public LyreState[] GetLyreStates() => _emptyLyres;

        // ── Unity Lifecycle ──────────────────────────────────────

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += OnConfigReloaded;
            if (ConfigManager.Config != null) OnConfigReloaded();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= OnConfigReloaded;
        }

        private void Update()
        {
            UpdateFakeState();
            UpdateStats();
            UpdateMiniScreen();
        }

        // ── Configuration ─────────────────────────────────────────

        public void SetRoutingEngine(RoutingEngine engine)
        {
            _routingEngine = engine;
        }

        /// <summary>Active/désactive le fake state (override l'authoring pour les tests).</summary>
        public void SetFakeStateActive(bool active)
        {
            _fakeStateActive = active;
            if (_routingEngine == null) return;
            // Le RoutingEngine aura besoin d'un SetStateProvider externe
        }

        // ── Fake State Generator ──────────────────────────────────

        private void UpdateFakeState()
        {
            if (!_fakeStateActive || _fakeState == null) return;

            // Mode "comme le prof" : une seule LED (canaux DMX 1-3)
            if (_firstLedOnly)
            {
                if (_lastFirstLedOnly && _lastFakeColor == _fakeColor) return;
                for (int i = 0; i < _fakeState.Length; i++)
                    _fakeState[i] = Color.black;
                _fakeState[0] = _fakeColor;
                _lastFirstLedOnly = true;
                _lastFakeColor = _fakeColor;
                return;
            }

            if (_fakeEffect == EffectType.SolidColor || _fakeEffect == EffectType.BlackOut)
            {
                if (!_lastFirstLedOnly && _lastFakeEffect == _fakeEffect && _lastFakeColor == _fakeColor)
                    return;
                var fill = _fakeEffect == EffectType.BlackOut ? Color.black : _fakeColor;
                for (int i = 0; i < _fakeState.Length; i++)
                    _fakeState[i] = fill;
                _lastFakeEffect = _fakeEffect;
                _lastFakeColor = _fakeColor;
                _lastFirstLedOnly = false;
                return;
            }

            float t = Time.time;
            var p = new EffectParameters
            {
                colorA = _fakeColor,
                intensity = 1f
            };
            EffectLibrary.Evaluate(_fakeEffect, t, _screenWidth, _screenHeight, p, _fakeState);
            _lastFakeEffect = _fakeEffect;
            _lastFakeColor = _fakeColor;
            _lastFirstLedOnly = false;
        }

        /// <summary>Test minimal : allume uniquement la 1ère LED (comme send-artnet.js).</summary>
        public void SendFirstLedTest(Color color)
        {
            _fakeColor = color;
            _firstLedOnly = true;
            _fakeStateActive = true;
            UpdateFakeState();
            Debug.Log($"[DebugPanel] Test 1ère LED : {color}");
        }

        /// <summary>Remplit tout l'écran d'une couleur de test.</summary>
        public void SendTestColor(Color color)
        {
            _fakeColor = color;
            _fakeEffect = EffectType.SolidColor;
            _firstLedOnly = false;
            _fakeStateActive = true;
            UpdateFakeState();
            Debug.Log($"[DebugPanel] Couleur test envoyée : {color}");
        }

        /// <summary>Éteint toutes les LEDs (blackout de test).</summary>
        public void SendBlackOut()
        {
            _fakeEffect = EffectType.BlackOut;
            _firstLedOnly = false;
            _fakeStateActive = true;
            UpdateFakeState();
        }

        // ── Stats ─────────────────────────────────────────────────

        private void UpdateStats()
        {
            _statsTimer += Time.deltaTime;
            if (_statsTimer < 0.5f) return; // Mise à jour 2x/s
            _statsTimer = 0f;

            if (_statsText == null || _routingEngine == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== PixelHub Debug ===");
            sb.AppendLine($"Unity FPS     : {Mathf.RoundToInt(1f / Time.smoothDeltaTime)}");
            sb.AppendLine($"Routing FPS   : {_routingEngine.RoutingFps:F1}");
            sb.AppendLine($"Routing ms    : {_routingEngine.RoutingMs:F2} ms");
            sb.AppendLine($"Packets/s     : {_routingEngine.PacketsPerSecond:F1}");
            sb.AppendLine($"Total packets : {_routingEngine.PacketsSentTotal}");

            if (ConfigManager.Config != null)
            {
                sb.AppendLine($"--- Config ---");
                sb.AppendLine($"LEDs          : {ConfigManager.Config.mapping.ledCount}");
                sb.AppendLine($"Ecran         : {ConfigManager.Config.mapping.screenWidth}×{ConfigManager.Config.mapping.screenHeight}");
                sb.AppendLine($"Contrôleurs   : {ConfigManager.Config.network.controllers?.Length ?? 0}");
                sb.AppendLine($"Fake state    : {(_fakeStateActive ? "ACTIF" : "inactif")}");
            }

            _statsText.text = sb.ToString();
        }

        // ── Mini-screen preview ───────────────────────────────────

        private void UpdateMiniScreen()
        {
            if (_miniScreen == null || _previewTexture == null || _fakeState == null) return;
            if (!_fakeStateActive) return;

            // Copier l'état dans la texture de prévisualisation
            for (int y = 0; y < _screenHeight; y++)
            {
                for (int x = 0; x < _screenWidth; x++)
                {
                    int idx = y * _screenWidth + x;
                    if (idx < _fakeState.Length)
                        _previewTexture.SetPixel(x, _screenHeight - 1 - y, _fakeState[idx]);
                }
            }
            _previewTexture.Apply();
        }

        // ── Helpers ────────────────────────────────────────────────

        private void OnConfigReloaded()
        {
            var cfg = ConfigManager.Config;
            _screenWidth  = cfg.mapping.screenWidth  > 0 ? cfg.mapping.screenWidth  : 128;
            _screenHeight = cfg.mapping.screenHeight > 0 ? cfg.mapping.screenHeight : 130;
            _fakeState = new Color32[cfg.mapping.ledCount];

            // Recréer la texture de prévisualisation
            if (_previewTexture != null) Destroy(_previewTexture);
            _previewTexture = new Texture2D(_screenWidth, _screenHeight, TextureFormat.RGB24, false);
            _previewTexture.filterMode = FilterMode.Point;
            if (_miniScreen != null) _miniScreen.texture = _previewTexture;
        }
    }
}
