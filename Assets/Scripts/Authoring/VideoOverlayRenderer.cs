using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Gère le VideoPlayer pour la vidéo de combat overlay et expose la RenderTexture
    /// + un Material luma-key pour que LedPreviewOverlay puisse la dessiner
    /// dans le cadre Aperçu LEDs avec fond transparent.
    /// Alimente VideoOverlayCompositor pour envoyer les stickmen au mur LED.
    /// </summary>
    public class VideoOverlayRenderer : MonoBehaviour
    {
        public static VideoOverlayRenderer Instance { get; private set; }

        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRT;
        private Material _lumaKeyMat;
        private bool _videoReady;
        private bool _paused;
        private bool _readbackPending;
        private Color32[] _pixelBuffer;
        private int _pixelWidth;
        private int _pixelHeight;
        private bool _errorLogged;

        /// <summary>La RenderTexture de la vidéo, null tant qu'elle n'est pas prête.</summary>
        public RenderTexture VideoTexture => _videoReady ? _videoRT : null;

        /// <summary>Matériau luma-key pour rendre le fond noir transparent.</summary>
        public Material LumaKeyMaterial => _lumaKeyMat;

        private void Awake()
        {
            Instance = this;

            var shader = Shader.Find("Hidden/VideoLumaKey");
            if (shader != null)
            {
                _lumaKeyMat = new Material(shader);
                _lumaKeyMat.SetFloat("_Threshold", 0.10f);
                _lumaKeyMat.SetFloat("_Softness", 0.08f);
            }
            else
            {
                Debug.LogWarning("[VideoOverlayRenderer] Shader 'Hidden/VideoLumaKey' non trouvé !");
            }
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
            VideoOverlayCompositor.ClearFrame();
            if (_videoPlayer != null && _videoPlayer.isPlaying)
                _videoPlayer.Pause();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            VideoOverlayCompositor.ClearFrame();
            CleanupVideo();
            if (_lumaKeyMat != null) Destroy(_lumaKeyMat);
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
            // Si l'objet a été désactivé pendant le readback GPU, on ignore le résultat
            // pour éviter que les pixels "collent" après la fin de l'Activation Track.
            if (request.hasError || !_videoReady || !gameObject.activeInHierarchy) return;

            var data = request.GetData<Color32>();
            if (_pixelBuffer == null || _pixelBuffer.Length != data.Length)
            {
                _pixelWidth = _videoRT != null ? _videoRT.width : 0;
                _pixelHeight = _videoRT != null ? _videoRT.height : 0;
                _pixelBuffer = new Color32[data.Length];
            }

            data.CopyTo(_pixelBuffer);
            VideoOverlayCompositor.SetFrame(_pixelBuffer, _pixelWidth, _pixelHeight);
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
            _videoPlayer.url = ResolveVideoUrl("combat_overlay");
            _videoPlayer.isLooping = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _videoPlayer.skipOnDrop = true;

            _videoPlayer.prepareCompleted += OnPrepared;
            _videoPlayer.errorReceived += OnError;
            _videoPlayer.Prepare();

            Debug.Log("[VideoOverlayRenderer] Préparation de la vidéo…");
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
            Debug.Log($"[VideoOverlayRenderer] Vidéo prête ({w}×{h}), lecture en boucle avec luma-key.");
        }

        private void OnError(VideoPlayer vp, string message)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            Debug.LogWarning(
                "[VideoOverlayRenderer] Vidéo combat illisible (souvent codec HEVC manquant). " +
                "Installez « HEVC Video Extensions » (Microsoft Store) ou placez combat_overlay.mp4 (H.264) dans StreamingAssets. " +
                $"Détail : {message}");
        }

        private static string ResolveVideoUrl(string baseName)
        {
            string dir = Application.streamingAssetsPath;
            string mp4 = System.IO.Path.Combine(dir, baseName + ".mp4");
            if (System.IO.File.Exists(mp4)) return mp4;
            return System.IO.Path.Combine(dir, baseName + ".mov");
        }

        private void CleanupVideo()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnPrepared;
                _videoPlayer.errorReceived -= OnError;
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
