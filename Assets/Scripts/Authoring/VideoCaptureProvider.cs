using UnityEngine;
using UnityEngine.Rendering;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Capture le rendu d'une caméra via une RenderTexture et 
    /// convertit les pixels en un tableau de Color32[] de manière asynchrone 
    /// pour ne pas bloquer le thread principal (GPU -> CPU).
    /// </summary>
    public class VideoCaptureProvider : MonoBehaviour, IStateProvider
    {
        [Header("Configuration Caméra")]
        public Camera captureCamera;
        
        [Header("Configuration Matrice")]
        public int width = 128;
        public int height = 128;

        private RenderTexture _renderTexture;
        private Color32[] _state;
        private bool _requestPending = false;

        void Start()
        {
            // Initialisation du tableau d'état pour le mur de LEDs
            _state = new Color32[width * height];

            // Création de la RenderTexture dynamique
            _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _renderTexture.filterMode = FilterMode.Point; // Pour garder des pixels nets
            _renderTexture.Create();

            // Assigne la RenderTexture à la caméra et force un fond noir
            if (captureCamera != null)
            {
                captureCamera.targetTexture = _renderTexture;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = Color.black;
                Debug.Log($"[VideoCaptureProvider] Caméra configurée pour rendre en {width}x{height} sur fond noir.");
            }
            else
            {
                Debug.LogWarning("[VideoCaptureProvider] Aucune caméra assignée !");
            }
        }

        void Update()
        {
            if (captureCamera == null || _renderTexture == null) return;

            // Si aucune requête asynchrone n'est en cours, on en lance une nouvelle
            if (!_requestPending)
            {
                _requestPending = true;
                AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGBA32, OnCompleteReadback);
            }
        }

        private void OnCompleteReadback(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                Debug.LogError("[VideoCaptureProvider] Erreur lors de la lecture GPU.");
                _requestPending = false;
                return;
            }

            // Récupère les données brutes depuis le GPU
            var nativeArray = request.GetData<Color32>();
            
            // Copie les couleurs dans le tableau d'état
            int length = Mathf.Min(nativeArray.Length, _state.Length);
            nativeArray.CopyTo(_state);
            
            _requestPending = false;
        }

        public Color32[] GetState()
        {
            return _state;
        }

        public LyreState[] GetLyreStates()
        {
            return null; // Pas géré par la capture vidéo
        }

        void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
        }
    }
}
