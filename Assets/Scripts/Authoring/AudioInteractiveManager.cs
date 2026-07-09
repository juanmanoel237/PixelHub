using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

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
    }

    /// <summary>
    /// Gère les interactions audio via le clavier : SFX, Volume Global et Pause (Espace).
    /// Les flèches sont réservées au pilotage des projecteurs (OtherDevicesPanel).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioInteractiveManager : MonoBehaviour
    {
        [Header("Effets Sonores (SFX)")]
        [Tooltip("Liste des sons à jouer selon la touche pressée.")]
        public List<KeySoundMapping> soundMappings = new List<KeySoundMapping>();

        [Header("Paramètres Volume")]
        [Tooltip("Le pas de changement du volume (ex: 0.1 pour 10%)")]
        public float volumeStep = 0.1f;

        private AudioSource _sfxSource;
        private PlayableDirector _director;
        private bool _directorWasPlaying;

        void Awake()
        {
            _sfxSource = GetComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _director = FindObjectOfType<PlayableDirector>();
        }

        void Update()
        {
            if (GlobalPause.IsPaused) return;

            HandleSoundEffects();
            HandleVolumeControl();
        }

        void LateUpdate()
        {
            HandlePauseControl();
        }

        private void HandleSoundEffects()
        {
            foreach (var mapping in soundMappings)
            {
                if (Input.GetKeyDown(mapping.key) && mapping.clip != null)
                    _sfxSource.PlayOneShot(mapping.clip, mapping.volume);
            }
        }

        /// <summary>Volume avec Page Up / Page Down (les flèches pilotent les lyres).</summary>
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

        /// <summary>Espace = pause globale / reprise (musique, timeline, lyres, LEDs).</summary>
        private void HandlePauseControl()
        {
            if (!Input.GetKeyDown(KeyCode.Space)) return;

            bool pause = !GlobalPause.IsPaused;
            GlobalPause.SetPaused(pause);

            if (pause)
            {
                AudioListener.pause = true;
                Time.timeScale = 0f;

                if (_director != null)
                {
                    _directorWasPlaying = _director.state == PlayState.Playing;
                    if (_directorWasPlaying)
                        _director.Pause();
                }

                Debug.Log("[Pause] Tout en pause (Espace pour reprendre)");
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
