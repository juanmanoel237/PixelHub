using System.IO;
using UnityEngine;
using UnityEngine.Video;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Lit une vidéo, la rééchantillonne à la résolution du mur LED (128×128),
    /// et expose les couleurs en tant que IStateProvider pour le RoutingEngine.
    ///
    /// Chaque frame, la vidéo est rendue dans une RenderTexture native,
    /// puis lue dans un Texture2D à la résolution exacte de l'écran LED.
    /// Ce tableau Color32[] est ensuite routé vers les contrôleurs physiques.
    /// </summary>
    public class VideoStateProvider : MonoBehaviour, IStateProvider
    {
        [Header("Vidéo")]
        [SerializeField] private string _videoFileName = "combat_fond_noir.mp4";
        [SerializeField] private bool _loop = true;

        // ── IStateProvider ───────────────────────────────────────
        public bool IsEntityBased => false;
        public LyreState[] GetLyreStates() => _emptyLyres;

        public Color32[] GetState()
        {
            // Si l'état est sale (frame vidéo a changé), relire la texture
            if (_stateDirty && _videoReady)
                SampleVideoToState();
            return _state;
        }

        // ── État interne ─────────────────────────────────────────
        private Color32[] _state;
        private LyreState[] _emptyLyres = new LyreState[0];

        private int _screenWidth;
        private int _screenHeight;

        private VideoPlayer  _videoPlayer;
        private RenderTexture _nativeRT;     // RenderTexture à la résolution native de la vidéo
        private RenderTexture _ledRT;        // RenderTexture à la résolution du mur LED (128×128)
        private Texture2D    _readBackTex;  // Texture CPU pour le readback GPU→CPU

        private bool _videoReady;
        private bool _stateDirty;

        // ── Unity Lifecycle ──────────────────────────────────────

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += OnConfigReloaded;
            if (ConfigManager.Config != null) OnConfigReloaded();
        }

        private void Start()
        {
            InitVideoPlayer();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= OnConfigReloaded;
            Cleanup();
        }

        private void OnConfigReloaded()
        {
            var cfg = ConfigManager.Config;
            _screenWidth  = cfg.mapping.screenWidth  > 0 ? cfg.mapping.screenWidth  : 128;
            _screenHeight = cfg.mapping.screenHeight > 0 ? cfg.mapping.screenHeight : 128;
            _state = new Color32[_screenWidth * _screenHeight];

            // Recréer la LED RenderTexture et la texture de readback
            if (_ledRT != null) { _ledRT.Release(); Destroy(_ledRT); }
            if (_readBackTex != null) Destroy(_readBackTex);

            _ledRT = new RenderTexture(_screenWidth, _screenHeight, 0, RenderTextureFormat.ARGB32);
            _ledRT.filterMode = FilterMode.Bilinear;
            _ledRT.Create();

            _readBackTex = new Texture2D(_screenWidth, _screenHeight, TextureFormat.RGBA32, false);
            _readBackTex.filterMode = FilterMode.Point;
        }

        // ── Vidéo ────────────────────────────────────────────────

        private void InitVideoPlayer()
        {
            string videoPath = Path.Combine(Application.streamingAssetsPath, _videoFileName);

            if (!File.Exists(videoPath))
            {
                Debug.LogWarning($"[VideoStateProvider] Vidéo introuvable : {videoPath}");
                return;
            }

            // RenderTexture native (sera redimensionnée après prepare)
            _nativeRT = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            _nativeRT.Create();

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = videoPath;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _nativeRT;
            _videoPlayer.isLooping = _loop;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;

            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived    += OnVideoError;
            _videoPlayer.frameReady       += OnFrameReady;
            _videoPlayer.sendFrameReadyEvents = true;
            _videoPlayer.Prepare();

            Debug.Log($"[VideoStateProvider] Préparation de la vidéo : {_videoFileName}");
        }

        private void OnVideoPrepared(VideoPlayer vp)
        {
            // Adapter la RenderTexture native à la vraie résolution de la vidéo
            if (_nativeRT.width != (int)vp.width || _nativeRT.height != (int)vp.height)
            {
                _nativeRT.Release();
                _nativeRT.width  = (int)vp.width;
                _nativeRT.height = (int)vp.height;
                _nativeRT.Create();
                vp.targetTexture = _nativeRT;
            }

            _videoReady = true;
            vp.Play();
            Debug.Log($"[VideoStateProvider] Vidéo prête ({vp.width}×{vp.height}), lecture en boucle → routage LED.");
        }

        private void OnFrameReady(VideoPlayer vp, long frameIdx)
        {
            // Marquer l'état comme "à relire" à la prochaine demande de GetState()
            _stateDirty = true;
        }

        private void OnVideoError(VideoPlayer vp, string message)
        {
            Debug.LogError($"[VideoStateProvider] Erreur vidéo : {message}");
        }

        // ── Rééchantillonnage Vidéo → État LED ──────────────────

        private void SampleVideoToState()
        {
            if (_nativeRT == null || _ledRT == null || _readBackTex == null) return;

            // Blit vidéo native → RenderTexture LED (réduit à 128×128 avec bilinear)
            Graphics.Blit(_nativeRT, _ledRT);

            // Readback GPU → CPU
            var prevRT = RenderTexture.active;
            RenderTexture.active = _ledRT;
            _readBackTex.ReadPixels(new Rect(0, 0, _screenWidth, _screenHeight), 0, 0, false);
            _readBackTex.Apply();
            RenderTexture.active = prevRT;

            // Copier les pixels dans l'état LED
            // Unity origin = bas-gauche ; LED origin = haut-gauche → inverser Y
            for (int y = 0; y < _screenHeight; y++)
            {
                int srcY = _screenHeight - 1 - y;
                for (int x = 0; x < _screenWidth; x++)
                {
                    Color32 c = _readBackTex.GetPixel(x, srcY);
                    _state[y * _screenWidth + x] = c;
                }
            }

            _stateDirty = false;
        }

        // ── Helpers ──────────────────────────────────────────────

        public void Play()  => _videoPlayer?.Play();
        public void Pause() => _videoPlayer?.Pause();
        public void Stop()  { _videoPlayer?.Stop(); if (_state != null) System.Array.Clear(_state, 0, _state.Length); }

        public bool IsReady => _videoReady;

        private void Cleanup()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnVideoPrepared;
                _videoPlayer.errorReceived    -= OnVideoError;
                _videoPlayer.frameReady       -= OnFrameReady;
                _videoPlayer.Stop();
                Destroy(_videoPlayer);
            }
            if (_nativeRT != null)   { _nativeRT.Release();  Destroy(_nativeRT); }
            if (_ledRT != null)      { _ledRT.Release();      Destroy(_ledRT); }
            if (_readBackTex != null) Destroy(_readBackTex);
        }
    }
}
