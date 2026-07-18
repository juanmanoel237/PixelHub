using UnityEngine;
using UnityEngine.Video;

namespace Laps.Authoring
{
    /// <summary>
    /// Gère le VideoPlayer pour la vidéo de combat overlay et expose la RenderTexture
    /// + un Material luma-key pour que LedPreviewOverlay puisse la dessiner
    /// dans le cadre Aperçu LEDs avec fond transparent.
    /// </summary>
    public class VideoOverlayRenderer : MonoBehaviour
    {
        private VideoPlayer _videoPlayer;
        private RenderTexture _videoRT;
        private Material _lumaKeyMat;
        private bool _videoReady;
        private bool _paused;

        /// <summary>La RenderTexture de la vidéo, null tant qu'elle n'est pas prête.</summary>
        public RenderTexture VideoTexture => _videoReady ? _videoRT : null;

        /// <summary>Matériau luma-key pour rendre le fond noir transparent.</summary>
        public Material LumaKeyMaterial => _lumaKeyMat;

        private void Awake()
        {
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

            InitVideoPlayer();
        }

        private void OnDestroy()
        {
            CleanupVideo();
            if (_lumaKeyMat != null) Destroy(_lumaKeyMat);
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
            _videoPlayer.url = System.IO.Path.Combine(Application.streamingAssetsPath, "combat_overlay.mov");
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
            Debug.LogError($"[VideoOverlayRenderer] Erreur vidéo : {message}");
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
