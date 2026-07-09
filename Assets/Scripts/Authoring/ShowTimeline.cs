using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Un keyframe de la timeline : déclenche un effet à un moment précis.
    /// </summary>
    [System.Serializable]
    public class Keyframe
    {
        public float timestamp;          // Temps de déclenchement (secondes)
        public float duration;           // Durée de l'effet (0 = jusqu'au prochain keyframe)
        public EffectType effectType;
        public EffectParameters parameters;
        public string label;             // Nom optionnel pour l'éditeur

        public Keyframe()
        {
            parameters = new EffectParameters();
        }
    }

    /// <summary>
    /// Une couche (layer) de la timeline : séquence de keyframes indépendante.
    /// Les couches sont mixées (blend) pour composer l'effet final.
    /// </summary>
    [System.Serializable]
    public class TimelineLayer
    {
        public string name;
        public bool enabled = true;
        public float opacity = 1f;             // 0-1 : transparence de la couche
        public List<Keyframe> keyframes = new List<Keyframe>();
    }

    /// <summary>
    /// Données complètes d'un show (sérialisables en JSON).
    /// </summary>
    [System.Serializable]
    public class ShowData
    {
        public string showName;
        public float totalDuration;          // Durée totale en secondes
        public string musicPath;             // Chemin relatif vers le fichier audio
        public List<TimelineLayer> layers = new List<TimelineLayer>();
    }

    /// <summary>
    /// Moteur de timeline : lit les keyframes, interpole les effets,
    /// et expose l'état via IStateProvider.
    ///
    /// Satisfait P3 : outil de programmation créative.
    /// Satisfait P4 : n'accède qu'à Color32[] — ne connaît pas le routage.
    /// Satisfait P5 : synchronisation avec l'audio via AudioSource.time.
    /// </summary>
    public class ShowTimeline : MonoBehaviour, IStateProvider
    {
        // ── Config ──────────────────────────────────────────────
        [Header("Show")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private string _showFileName = "show.json";

        // ── État interne ────────────────────────────────────────
        private ShowData _showData;
        private Color32[] _state;
        private Color32[] _layerBuffer;
        private LyreState[] _lyreStates;

        private int _screenWidth;
        private int _screenHeight;
        private int _ledCount;

        // ── Lecture ─────────────────────────────────────────────
        public bool  IsPlaying { get; private set; }
        public float CurrentTime => _audioSource != null ? _audioSource.time : _manualTime;
        private float _manualTime;

        // ── Unity Lifecycle ─────────────────────────────────────

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += InitBuffers;
            if (ConfigManager.Config != null) InitBuffers();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= InitBuffers;
        }

        private void Update()
        {
            if (!IsPlaying) return;

            // Si pas d'AudioSource, avancer manuellement
            if (_audioSource == null)
                _manualTime += Time.deltaTime;

            EvaluateTimeline(CurrentTime);
        }

        // ── IStateProvider ──────────────────────────────────────

        public Color32[] GetState()   => _state;
        public LyreState[] GetLyreStates() => _lyreStates;
        public bool IsEntityBased => false;

        // ── API publique ────────────────────────────────────────

        public void Play()
        {
            IsPlaying = true;
            if (_audioSource != null) _audioSource.Play();
            Debug.Log("[ShowTimeline] Lecture démarrée.");
        }

        public void Pause()
        {
            IsPlaying = false;
            if (_audioSource != null) _audioSource.Pause();
        }

        public void Stop()
        {
            IsPlaying = false;
            if (_audioSource != null) _audioSource.Stop();
            _manualTime = 0f;
            FillBlack();
        }

        public void Seek(float time)
        {
            _manualTime = Mathf.Clamp(time, 0f, _showData?.totalDuration ?? 0f);
            if (_audioSource != null)
                _audioSource.time = _manualTime;
        }

        // ── Chargement / Sauvegarde ─────────────────────────────

        public void LoadShow(string fileName = null)
        {
            fileName = fileName ?? _showFileName;
            string path = Path.Combine(Application.streamingAssetsPath, fileName);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ShowTimeline] Show introuvable : {path}. Création d'un show vide.");
                NewShow();
                return;
            }

            string json = File.ReadAllText(path);
            _showData = JsonUtility.FromJson<ShowData>(json);
            Debug.Log($"[ShowTimeline] Show chargé : '{_showData.showName}', {_showData.layers.Count} couche(s), durée {_showData.totalDuration}s");
        }

        public void SaveShow(string fileName = null)
        {
            if (_showData == null) return;
            fileName = fileName ?? _showFileName;
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            File.WriteAllText(path, JsonUtility.ToJson(_showData, true));
            Debug.Log($"[ShowTimeline] Show sauvegardé : {path}");
        }

        public void NewShow()
        {
            _showData = new ShowData
            {
                showName     = "le continent",
                totalDuration = 120f,
                layers        = new List<TimelineLayer>
                {
                    new TimelineLayer
                    {
                        name = "Couche 1",
                        keyframes = new List<Keyframe>
                        {
                            new Keyframe { 
                                timestamp = 0f, 
                                effectType = EffectType.TextDisplay,
                                parameters = new EffectParameters {
                                    text = "le continent",
                                    textScale = 5,
                                    colorA = new Color(0f, 0.47f, 1f) // bleu comme test.js
                                }
                            }
                        }
                    }
                }
            };
        }

        // ── Accès aux données pour l'éditeur ───────────────────

        public ShowData GetShowData()  => _showData;
        public void     SetShowData(ShowData data) { _showData = data; }

        public TimelineLayer AddLayer(string name = "Nouvelle couche")
        {
            var layer = new TimelineLayer { name = name };
            _showData?.layers.Add(layer);
            return layer;
        }

        public void AddKeyframe(int layerIndex, float time, EffectType type, EffectParameters p = null)
        {
            if (_showData == null || layerIndex >= _showData.layers.Count) return;
            var kf = new Keyframe { timestamp = time, effectType = type, parameters = p ?? new EffectParameters() };
            _showData.layers[layerIndex].keyframes.Add(kf);
            _showData.layers[layerIndex].keyframes.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
        }

        // ── Évaluation de la timeline ───────────────────────────

        private void EvaluateTimeline(float time)
        {
            if (_showData == null || _state == null) return;

            FillBlack();

            foreach (var layer in _showData.layers)
            {
                if (!layer.enabled || layer.keyframes.Count == 0) continue;

                // Trouver le keyframe actif (le dernier dont timestamp ≤ time)
                Keyframe active = FindActiveKeyframe(layer.keyframes, time);
                if (active == null) continue;

                // Calculer le temps relatif dans l'effet
                float localT = time - active.timestamp;

                // Évaluer l'effet dans le buffer de couche
                EffectLibrary.Evaluate(
                    active.effectType, localT,
                    _screenWidth, _screenHeight,
                    active.parameters,
                    _layerBuffer);

                // Mélanger la couche dans l'état final
                BlendLayer(_layerBuffer, _state, layer.opacity);
            }
        }

        private static Keyframe FindActiveKeyframe(List<Keyframe> keyframes, float time)
        {
            Keyframe result = null;
            foreach (var kf in keyframes)
            {
                if (kf.timestamp <= time) result = kf;
                else break;
            }
            return result;
        }

        private static void BlendLayer(Color32[] src, Color32[] dst, float opacity)
        {
            int len = Mathf.Min(src.Length, dst.Length);
            for (int i = 0; i < len; i++)
            {
                Color32 s = src[i];
                Color32 d = dst[i];
                dst[i] = new Color32(
                    (byte)Mathf.Clamp(d.r + s.r * opacity, 0, 255),
                    (byte)Mathf.Clamp(d.g + s.g * opacity, 0, 255),
                    (byte)Mathf.Clamp(d.b + s.b * opacity, 0, 255),
                    255);
            }
        }

        private void FillBlack()
        {
            if (_state == null) return;
            for (int i = 0; i < _state.Length; i++)
                _state[i] = new Color32(0, 0, 0, 255);
        }

        private void InitBuffers()
        {
            var cfg = ConfigManager.Config;
            _ledCount     = cfg.mapping.ledCount;
            _screenWidth  = cfg.mapping.screenWidth  > 0 ? cfg.mapping.screenWidth  : 128;
            _screenHeight = cfg.mapping.screenHeight > 0 ? cfg.mapping.screenHeight : 128;
            _state       = new Color32[_ledCount];
            _layerBuffer = new Color32[_ledCount];
            _lyreStates  = new LyreState[cfg.mapping.lyres?.Length ?? 0];
            FillBlack();
        }
    }
}
