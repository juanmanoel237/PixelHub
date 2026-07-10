using System.Collections.Generic;
using UnityEngine;
<<<<<<< HEAD
=======
using UnityEngine.Playables;
>>>>>>> origin/Feat/key-effect-synchronization
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Associe une touche du clavier à un effet sonore (SFX).
    /// </summary>
    [System.Serializable]
    public class KeySoundMapping
    {
        public KeyCode key;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
<<<<<<< HEAD
        
        [Header("Effet Visuel LED (Optionnel)")]
        [Tooltip("Prefab avec ProceduralFirework pour choisir le style (dessiné sur la grille LED).")]
=======

        [Header("Effet Visuel (Optionnel)")]
        [Tooltip("Prefab VFX (ex: Système de particules de feu d'artifice) à faire apparaître lors de l'appui.")]
>>>>>>> origin/Feat/key-effect-synchronization
        public GameObject visualPrefab;
    }

    /// <summary>
    /// Gère les interactions audio via le clavier : SFX, Volume Global et Pause.
    /// Les actions sont synchronisées entre postes via eHub (UDP).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioInteractiveManager : MonoBehaviour
    {
        [Header("Effets Sonores (SFX)")]
        public List<KeySoundMapping> soundMappings = new List<KeySoundMapping>();

        [Header("Paramètres Volume")]
        public float volumeStep = 0.1f;

        private AudioSource _sfxSource;
        private PlayableDirector _director;
        private bool _isPaused;
        private bool _directorWasPlaying;

        public bool IsPaused => _isPaused;

        void Awake()
        {
            _sfxSource = GetComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _director = FindObjectOfType<PlayableDirector>();
        }

        void Start() { }

        void Update()
        {
            if (!_isPaused)
            {
                HandleSoundEffects();
                HandleVolumeControl();
            }
            HandlePauseControl();
        }

        /// <summary>Local + sync eHub (clavier ou boutons UI).</summary>
        public void RequestTriggerEffect(int mappingIndex)
        {
            TriggerEffect(mappingIndex, fromNetwork: false);
        }

        /// <summary>Local + sync eHub (clavier ou boutons UI).</summary>
        public void RequestPauseToggle()
        {
            bool pause = !_isPaused;
            SetPaused(pause, fromNetwork: false);
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.PauseState,
                intArg = pause ? 1 : 0
            });
        }

        /// <summary>Déclenche un effet (son + VFX). Appelé localement ou depuis eHub.</summary>
        public void TriggerEffect(int mappingIndex, bool fromNetwork = false)
        {
            if (mappingIndex < 0 || mappingIndex >= soundMappings.Count) return;

            var mapping = soundMappings[mappingIndex];

            if (mapping.clip != null)
                _sfxSource.PlayOneShot(mapping.clip, mapping.volume);

            if (mapping.visualPrefab != null)
            {
                Vector3 spawnPos = new Vector3(Random.Range(-5f, 5f), Random.Range(-5f, 5f), 10f);
                GameObject vfx = Instantiate(mapping.visualPrefab, spawnPos, Quaternion.identity);
                Destroy(vfx, 3f);
            }

            if (!fromNetwork)
                EHubSyncBus.PublishLocal(new EHubMessage
                {
                    type = EHubMessageTypes.SfxTrigger,
                    intArg = mappingIndex
                });
        }

        private void HandleSoundEffects()
        {
            for (int i = 0; i < soundMappings.Count; i++)
            {
<<<<<<< HEAD
                if (Input.GetKeyDown(mapping.key))
                {
                    // 1. Jouer le son
                    if (mapping.clip != null)
                    {
                        Debug.Log($"[AudioInteractive] Key={mapping.key} clip={mapping.clip.name} vol={mapping.volume} listenerPause={AudioListener.pause} listenerVol={AudioListener.volume}");
                        _sfxSource.PlayOneShot(mapping.clip, mapping.volume);
                    }
                    
                    // 2. Feu d'artifice sur la grille LED (mur + aperçu), pas en 3D dans la scène
                    FireworkStyle style = FireworkStyle.ClassicNova;
                    if (mapping.visualPrefab != null)
                    {
                        var procedural = mapping.visualPrefab.GetComponent<ProceduralFirework>();
                        if (procedural != null)
                            style = procedural.style;
                    }
                    LedFireworks.Trigger(style);
                }
=======
                if (Input.GetKeyDown(soundMappings[i].key))
                    TriggerEffect(i, fromNetwork: false);
>>>>>>> origin/Feat/key-effect-synchronization
            }
        }

        private void HandleVolumeControl()
        {
            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                AudioListener.volume = Mathf.Clamp01(AudioListener.volume + volumeStep);
                Debug.Log($"[Audio] Volume : {Mathf.RoundToInt(AudioListener.volume * 100)}%");
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                AudioListener.volume = Mathf.Clamp01(AudioListener.volume - volumeStep);
                Debug.Log($"[Audio] Volume : {Mathf.RoundToInt(AudioListener.volume * 100)}%");
            }
        }

        private void HandlePauseControl()
        {
            if (!Input.GetKeyDown(KeyCode.Space)) return;
            RequestPauseToggle();
        }

        /// <summary>Pause/play global. Appelé localement ou depuis eHub.</summary>
        public void SetPaused(bool paused, bool fromNetwork = false)
        {
            if (_isPaused == paused) return;
            _isPaused = paused;

            if (paused)
            {
                AudioListener.pause = true;
                Time.timeScale = 0f;

                if (_director != null)
                {
                    _directorWasPlaying = _director.state == PlayState.Playing;
                    if (_directorWasPlaying)
                        _director.Pause();
                }

                Debug.Log("[Pause] Tout en pause");
            }
            else
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;

                if (_director != null && _directorWasPlaying)
                    _director.Resume();

                Debug.Log("[Pause] Lecture reprise");
            }
        }
    }
}
