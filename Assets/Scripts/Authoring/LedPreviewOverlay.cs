using UnityEngine;
using Laps.Core;
using Laps.Routing;

namespace Laps.Authoring
{
    /// <summary>
    /// Prévisualisation à l'écran (onglet Game) de ce que Unity envoie aux LEDs.
    /// Unity n'allume pas le mur LED dans la Scene 3D : cette overlay montre l'état DMX simulé.
    /// </summary>
    public class LedPreviewOverlay : MonoBehaviour
    {
        private IStateProvider _provider;
        private RoutingEngine _routingEngine;
        private Texture2D _previewTexture;
        private int _screenWidth;
        private int _screenHeight;
        private string _modeLabel = "—";
        private float _refreshTimer;

        public void Init(RoutingEngine routing)
        {
            _routingEngine = routing;
        }

        public void SetProvider(IStateProvider provider, string modeLabel)
        {
            _provider = provider;
            _modeLabel = modeLabel;
        }

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += InitBuffers;
            if (ConfigManager.Config != null) InitBuffers();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= InitBuffers;
            if (_previewTexture != null) Destroy(_previewTexture);
        }

        private void InitBuffers()
        {
            if (ConfigManager.Config == null) return;
            _screenWidth = ConfigManager.Config.mapping.screenWidth;
            _screenHeight = ConfigManager.Config.mapping.screenHeight;
            if (_previewTexture != null) Destroy(_previewTexture);
            _previewTexture = new Texture2D(_screenWidth, _screenHeight, TextureFormat.RGB24, false);
            _previewTexture.filterMode = FilterMode.Point;
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < 0.05f) return;
            _refreshTimer = 0f;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_provider == null || _previewTexture == null) return;
            Color32[] state = _provider.GetState();
            if (state == null || state.Length == 0) return;

            int len = Mathf.Min(state.Length, _screenWidth * _screenHeight);
            var pixels = new Color[len];
            for (int i = 0; i < len; i++)
                pixels[i] = state[i];

            _previewTexture.SetPixels(pixels);
            _previewTexture.Apply();
        }

        private void OnGUI()
        {
            const int margin = 10;
            const int previewMax = 280;
            float aspect = _screenHeight > 0 ? (float)_screenWidth / _screenHeight : 1f;
            int previewW = previewMax;
            int previewH = Mathf.RoundToInt(previewMax / aspect);

            GUI.Box(new Rect(margin, margin, 260, 118), "PixelHub — envoi Art-Net");

            float y = margin + 22;
            GUI.Label(new Rect(margin + 8, y, 244, 18), $"Mode : {_modeLabel}");
            y += 18;

            if (_routingEngine != null)
            {
                GUI.Label(new Rect(margin + 8, y, 244, 18), $"Paquets/s : {_routingEngine.PacketsPerSecond:F1}");
                y += 18;
                GUI.Label(new Rect(margin + 8, y, 244, 18), $"Total envoyés : {_routingEngine.PacketsSentTotal}");
                y += 18;
            }

            if (ConfigManager.Config?.network.controllers?.Length > 0)
            {
                var c = ConfigManager.Config.network.controllers[0];
                GUI.Label(new Rect(margin + 8, y, 244, 18), $"→ {c.ip}:6454  startUniv.{c.startUniverse}");
            }

            if (_previewTexture != null)
            {
                int x = Screen.width - previewW - margin;
                GUI.Box(new Rect(x - 4, margin - 4, previewW + 8, previewH + 28), "Aperçu LEDs (simulation)");
                GUI.DrawTexture(new Rect(x, margin + 18, previewW, previewH), _previewTexture, ScaleMode.ScaleToFit);
            }

            GUI.Label(new Rect(margin, Screen.height - 28, Screen.width - margin * 2, 22),
                "Onglet GAME (pas Scene). Touches : 1 / R / G / B / 0 / T");
        }
    }
}
