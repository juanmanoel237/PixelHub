using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Gère le VideoPlayer pour la vidéo de confettis overlay et expose la RenderTexture.
    /// Alimente ConfettiOverlayCompositor pour envoyer les confettis au mur LED.
    /// </summary>
    public class ConfettiOverlayRenderer : MonoBehaviour
    {
        public static ConfettiOverlayRenderer Instance { get; private set; }

        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRT;
        private bool _videoReady;
        private bool _paused;
        private bool _readbackPending;
        private Color32[] _pixelBuffer;
        private int _pixelWidth;
        private int _pixelHeight;
        private int _frameCount = 0;

        /// <summary>La RenderTexture de la vidéo, null tant qu'elle n'est pas prête.</summary>
        public RenderTexture VideoTexture => _videoReady ? _videoRT : null;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            InitVideoPlayer();
        }

        private void OnEnable()
        {
            // L'Activation Track active l'objet → on (re)lance la vidéo
            if (_videoPlayer != null && _videoReady && !_videoPlayer.isPlaying)
                _videoPlayer.Play();
        }

        private void OnDisable()
        {
            // L'Activation Track désactive l'objet → on efface le buffer
            ConfettiOverlayCompositor.ClearFrame();
            if (_videoPlayer != null && _videoPlayer.isPlaying)
                _videoPlayer.Pause();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ConfettiOverlayCompositor.ClearFrame();
            CleanupVideo();
        }

        private void Update()
        {
            if (!_videoReady || _paused || _videoRT == null || _readbackPending) return;

            _readbackPending = true;
            AsyncGPUReadback.Request(_videoRT, 0, TextureFormat.RGBA32, OnReadbackComplete);
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            if (request.hasError || !_videoReady) return;

            var data = request.GetData<Color32>();
            if (_pixelBuffer == null || _pixelBuffer.Length != data.Length)
            {
                _pixelWidth  = _videoRT != null ? _videoRT.width  : 0;
                _pixelHeight = _videoRT != null ? _videoRT.height : 0;
                _pixelBuffer = new Color32[data.Length];
            }

            data.CopyTo(_pixelBuffer);
            
            // Debug : vérifier s'il y a des pixels non-noirs
            if (_frameCount % 60 == 0)
            {
                float maxLuma = 0f;
                for (int i = 0; i < _pixelBuffer.Length; i += 100) // sample every 100 pixels for speed
                {
                    Color32 src = _pixelBuffer[i];
                    float luma = (0.2126f * src.r + 0.7152f * src.g + 0.0722f * src.b) / 255f;
                    if (luma > maxLuma) maxLuma = luma;
                }
                Debug.Log($"[ConfettiOverlayRenderer] Frame {_frameCount}, Max Luma: {maxLuma}");
            }
            _frameCount++;

            ConfettiOverlayCompositor.SetFrame(_pixelBuffer, _pixelWidth, _pixelHeight);
        }

        /// <summary>Pause / reprise synchronisée avec Espace (et eHub).</summary>
        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (_videoPlayer == null || !_videoReady) return;

            if (paused)
            {
                if (_videoPlayer.isPlaying)
                    _videoPlayer.Pause();
            }
            else if (!_videoPlayer.isPlaying)
            {
                _videoPlayer.Play();
            }
        }

        private void InitVideoPlayer()
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = System.IO.Path.Combine(Application.streamingAssetsPath, "confetti_overlay.mov");
            _videoPlayer.isLooping = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.skipOnDrop = true;

            _videoPlayer.prepareCompleted += OnPrepared;
            _videoPlayer.errorReceived    += OnError;
            _videoPlayer.Prepare();

            Debug.Log("[ConfettiOverlayRenderer] Préparation de la vidéo confetti…");
        }

        private void OnPrepared(VideoPlayer vp)
        {
            int w = (int)vp.width;
            int h = (int)vp.height;
            if (w == 0 || h == 0) { w = 640; h = 360; }

            _videoRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            _videoRT.Create();

            vp.targetTexture = _videoRT;
            if (!_paused)
                vp.Play();
            _videoReady = true;
            Debug.Log($"[ConfettiOverlayRenderer] Vidéo confetti prête ({w}×{h}), lecture en boucle.");
        }

        private void OnError(VideoPlayer vp, string message)
        {
            Debug.LogError($"[ConfettiOverlayRenderer] Erreur vidéo : {message}");
        }

        private void CleanupVideo()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnPrepared;
                _videoPlayer.errorReceived    -= OnError;
                _videoPlayer.Stop();
                Destroy(_videoPlayer);
            }
            if (_videoRT != null)
            {
                _videoRT.Release();
                Destroy(_videoRT);
            }
        }
    }
}
