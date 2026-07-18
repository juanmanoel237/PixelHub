using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
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
        [Header("Effet Visuel LED (Optionnel)")]
        [Tooltip("Prefab avec ProceduralFirework pour choisir le style (dessiné sur la grille LED).")]
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
            // Cas particuliers pour les lance-flammes et autres effets rapides (sans prefab/SFX obligatoires)
            if (mappingIndex == -99)
            {
                LedFireworks.Trigger(FireworkStyle.FlameThrowerLeft);
                if (!fromNetwork)
                {
                    EHubSyncBus.PublishLocal(new EHubMessage
                    {
                        type = EHubMessageTypes.SfxTrigger,
                        intArg = -99
                    });
                }
                return;
            }
            if (mappingIndex == -98)
            {
                LedFireworks.Trigger(FireworkStyle.FlameThrowerRight);
                if (!fromNetwork)
                {
                    EHubSyncBus.PublishLocal(new EHubMessage
                    {
                        type = EHubMessageTypes.SfxTrigger,
                        intArg = -98
                    });
                }
                return;
            }
            if (mappingIndex == -97)
            {
                LedFireworks.Trigger(FireworkStyle.LaserSweep);
                if (!fromNetwork)
                {
                    EHubSyncBus.PublishLocal(new EHubMessage
                    {
                        type = EHubMessageTypes.SfxTrigger,
                        intArg = -97
                    });
                }
                return;
            }
            if (mappingIndex == -96)
            {
                LedFireworks.Trigger(FireworkStyle.Shockwave);
                if (!fromNetwork)
                {
                    EHubSyncBus.PublishLocal(new EHubMessage
                    {
                        type = EHubMessageTypes.SfxTrigger,
                        intArg = -96
                    });
                }
                return;
            }

            if (mappingIndex < 0 || mappingIndex >= soundMappings.Count) return;

            var mapping = soundMappings[mappingIndex];

            if (mapping.clip != null)
                _sfxSource.PlayOneShot(mapping.clip, mapping.volume);

            // 2. Feu d'artifice sur la grille LED (mur + aperçu), pas en 3D dans la scène
            FireworkStyle style = FireworkStyle.ClassicNova;
            Color? customColor = null;
            bool forceMulticolor = false;

            if (mapping.visualPrefab != null)
            {
                var procedural = mapping.visualPrefab.GetComponent<ProceduralFirework>();
                if (procedural != null)
                {
                    style = procedural.style;
                    if (!procedural.useRandomColor)
                    {
                        customColor = procedural.fireworkColor;
                    }
                    else
                    {
                        forceMulticolor = procedural.multicolor;
                    }
                }
            }
            LedFireworks.Trigger(style, customColor, forceMulticolor);

            if (!fromNetwork)
                EHubSyncBus.PublishLocal(new EHubMessage
                {
                    type = EHubMessageTypes.SfxTrigger,
                    intArg = mappingIndex
                });
        }

        private void HandleSoundEffects()
        {
            // Déclenchement normal via mapping Inspector
            for (int i = 0; i < soundMappings.Count; i++)
            {
                if (Input.GetKeyDown(soundMappings[i].key))
                    TriggerEffect(i, fromNetwork: false);
            }

            // Raccourcis clavier directs pour tester les lance-flammes (gauche/droite)
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.F))
            {
                TriggerEffect(-99, fromNetwork: false);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.H))
            {
                TriggerEffect(-98, fromNetwork: false);
            }

            // Raccourcis clavier directs pour tester le balayage laser (L)
            if (Input.GetKeyDown(KeyCode.L))
            {
                TriggerEffect(-97, fromNetwork: false);
            }

            // Raccourcis clavier directs pour tester l'onde de choc (S)
            if (Input.GetKeyDown(KeyCode.S))
            {
                TriggerEffect(-96, fromNetwork: false);
            }
        }

        private void HandleVolumeControl()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                AudioListener.volume = Mathf.Clamp01(AudioListener.volume + volumeStep);
                Debug.Log($"[Audio] Volume : {Mathf.RoundToInt(AudioListener.volume * 100)}%");
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
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
            GlobalPause.SetPaused(paused);

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

                var video = FindObjectOfType<VideoOverlayRenderer>();
                video?.SetPaused(true);

                Debug.Log("[Pause] Tout en pause (audio + timeline + stickmen)");
            }
            else
            {
                Time.timeScale = 1f;
                AudioListener.pause = false;

                if (_director != null && _directorWasPlaying)
                    _director.Resume();

                var video = FindObjectOfType<VideoOverlayRenderer>();
                video?.SetPaused(false);

                Debug.Log("[Pause] Lecture reprise");
            }
        }
    }
}
