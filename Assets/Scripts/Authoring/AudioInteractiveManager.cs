using System.Collections.Generic;
using UnityEngine;

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
        
        [Header("Effet Visuel (Optionnel)")]
        [Tooltip("Prefab VFX (ex: Système de particules de feu d'artifice) à faire apparaître lors de l'appui.")]
        public GameObject visualPrefab;
    }

    /// <summary>
    /// Gère les interactions audio via le clavier : SFX, Volume Global et Pause.
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
        private bool _isPaused = false;

        void Awake()
        {
            // Récupère ou ajoute automatiquement un AudioSource pour jouer les SFX
            _sfxSource = GetComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
        }

        void Update()
        {
            HandleSoundEffects();
            HandleVolumeControl();
            HandlePauseControl();
        }

        /// <summary>
        /// Joue les effets sonores assignés aux touches.
        /// </summary>
        private void HandleSoundEffects()
        {
            foreach (var mapping in soundMappings)
            {
                if (Input.GetKeyDown(mapping.key))
                {
                    // 1. Jouer le son
                    if (mapping.clip != null)
                    {
                        _sfxSource.PlayOneShot(mapping.clip, mapping.volume);
                    }
                    
                    // 2. Faire apparaître l'effet visuel (si configuré)
                    if (mapping.visualPrefab != null)
                    {
                        // On le fait apparaître à une position aléatoire devant la caméra (ex: Z = 10)
                        Vector3 spawnPos = new Vector3(Random.Range(-5f, 5f), Random.Range(-5f, 5f), 10f);
                        GameObject vfx = Instantiate(mapping.visualPrefab, spawnPos, Quaternion.identity);
                        
                        // Nettoyage automatique après 3 secondes (durée maximale de l'effet)
                        Destroy(vfx, 3f);
                    }
                }
            }
        }

        /// <summary>
        /// Modifie le volume global avec les flèches Haut et Bas.
        /// </summary>
        private void HandleVolumeControl()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                AudioListener.volume = Mathf.Clamp01(AudioListener.volume + volumeStep);
                Debug.Log($"[Audio] Volume augmenté : {Mathf.RoundToInt(AudioListener.volume * 100)}%");
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                AudioListener.volume = Mathf.Clamp01(AudioListener.volume - volumeStep);
                Debug.Log($"[Audio] Volume baissé : {Mathf.RoundToInt(AudioListener.volume * 100)}%");
            }
        }

        /// <summary>
        /// Met en pause le jeu (Audio et Visuel) avec la touche Espace.
        /// </summary>
        private void HandlePauseControl()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _isPaused = !_isPaused;

                if (_isPaused)
                {
                    // Met en pause la musique globale et fige les animations (Timeline, etc.)
                    AudioListener.pause = true;
                    Time.timeScale = 0f;
                    Debug.Log("[Audio] Pause ACTIVÉE");
                }
                else
                {
                    // Relance la musique et le temps
                    AudioListener.pause = false;
                    Time.timeScale = 1f;
                    Debug.Log("[Audio] Pause DÉSACTIVÉE");
                }
            }
        }
    }
}
