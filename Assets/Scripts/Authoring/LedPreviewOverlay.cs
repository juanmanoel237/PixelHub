using UnityEngine;
using UnityEngine.Video;
using Laps.Core;
using Laps.Routing;

namespace Laps.Authoring
{
    /// <summary>
    /// Prévisualisation à l'écran (onglet Game) de ce que Unity envoie aux LEDs.
    /// Unity n'allume pas le mur LED dans la Scene 3D : cette overlay montre l'état DMX simulé.
    /// Affiche aussi la vidéo personnages_fond_transparent.webm en premier plan.
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

        // ── Vidéo en premier plan ──────────────────────────────
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRenderTexture;
        private bool _videoReady;

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

        private void Start()
        {
            InitVideoPlayer();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= InitBuffers;
            if (_previewTexture != null) Destroy(_previewTexture);
            CleanupVideo();
        }

        // ── Vidéo ──────────────────────────────────────────────

        private void InitVideoPlayer()
        {
            string videoPath = System.IO.Path.Combine(
                Application.streamingAssetsPath, "combat_fond_noir.mp4");

            if (!System.IO.File.Exists(videoPath))
            {
                Debug.LogWarning($"[LedPreviewOverlay] Vidéo introuvable : {videoPath}");
                return;
            }

            // Créer la RenderTexture avec canal alpha (ARGB32)
            _videoRenderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            _videoRenderTexture.Create();

            // Configurer le VideoPlayer
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = videoPath;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoRenderTexture;
            _videoPlayer.isLooping = true;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

            // Activer la transparence (alpha) si supporté
            _videoPlayer.targetCameraAlpha = 1f;

            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.Prepare();

            Debug.Log("[LedPreviewOverlay] Préparation de la vidéo en premier plan...");
        }

        private void OnVideoPrepared(VideoPlayer vp)
        {
            _videoReady = true;

            // Adapter la RenderTexture à la taille réelle de la vidéo
            if (_videoRenderTexture.width != (int)vp.width || _videoRenderTexture.height != (int)vp.height)
            {
                _videoRenderTexture.Release();
                _videoRenderTexture.width = (int)vp.width;
                _videoRenderTexture.height = (int)vp.height;
                _videoRenderTexture.Create();
            }

            vp.Play();
            Debug.Log($"[LedPreviewOverlay] Vidéo prête ({vp.width}×{vp.height}), lecture en boucle.");
        }

        private void OnVideoError(VideoPlayer vp, string message)
        {
            Debug.LogError($"[LedPreviewOverlay] Erreur vidéo : {message}");
        }

        private void CleanupVideo()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnVideoPrepared;
                _videoPlayer.errorReceived -= OnVideoError;
                _videoPlayer.Stop();
                Destroy(_videoPlayer);
            }
            if (_videoRenderTexture != null)
            {
                _videoRenderTexture.Release();
                Destroy(_videoRenderTexture);
            }
        }

        // ── LED Preview ────────────────────────────────────────

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
                // Couche 1 : texture LED (arrière-plan)
                GUI.DrawTexture(new Rect(x, margin + 18, previewW, previewH), _previewTexture, ScaleMode.ScaleToFit);

                // Couche 2 : vidéo personnages en premier plan (par-dessus les LEDs)
                if (_videoReady && _videoRenderTexture != null)
                {
                    GUI.DrawTexture(
                        new Rect(x, margin + 18, previewW, previewH),
                        _videoRenderTexture,
                        ScaleMode.ScaleToFit,
                        true  // alphaBlend = true pour conserver la transparence
                    );
                }
            }

            GUI.Label(new Rect(margin, Screen.height - 28, Screen.width - margin * 2, 22),
                "Onglet GAME (pas Scene). Touches : 1 / R / G / B / 0 / T");
        }
    }
}
