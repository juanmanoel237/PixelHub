using System;
using System.IO;
using UnityEngine;

namespace Laps.Core
{
    // ────────────────────────────────────────────────
    //  Structures de configuration sérialisables (P1)
    // ────────────────────────────────────────────────

    [Serializable]
    public class ControllerConfig
    {
        public string ip;
        public int startUniverse;   // Premier univers géré par ce contrôleur
        public int universeCount;   // Nombre d'univers pris en charge
    }

    [Serializable]
    public class LyreConfig
    {
        public string name;
        public string controllerIp;
        public int universe;
        public int startChannel;    // Canal DMX de départ (1-512)
        // Canaux DMX standard pour lyre : pan, tilt, dimmer, r, g, b, strobe...
    }

    [Serializable]
    public class StripConfig
    {
        public string name;
        public int ledCount;
        public string controllerIp;
        public int universe;
        public int startChannel;    // Canal DMX de départ dans l'univers
        public int channelsPerLed;  // 3 = RGB, 4 = RGBW
    }

    [Serializable]
    public class NetworkConfig
    {
        public ControllerConfig[] controllers;
        public bool eHubEnabled;      // Active la sync multi-postes
        public int eHubPort;          // Port UDP eHub (défaut 9000)
        public string eHubSessionId;  // Code équipe — isole du reste de la classe
        public string[] eHubPeers;    // IPs des autres postes de VOTRE équipe (unicast)
        public int artNetPort;        // Port ArtNet standard = 6454
    }

    [Serializable]
    public class MappingConfig
    {
        public string layout;       // "Matrix2D" (défaut) ou "LapsWall128"
        public int ledCount;        // Nombre total de LEDs sur l'écran principal
        public int screenWidth;     // Largeur de l'écran en pixels
        public int screenHeight;    // Hauteur de l'écran en pixels
        public string pixelOrder;   // "RGB" ou "RGBW" ou "GRB" etc.
        public int channelsPerLed;  // 3 ou 4
        public bool serpentine;     // true = lignes impaires câblées de droite à gauche
        public StripConfig[] strips;
        public LyreConfig[] lyres;
    }

    [Serializable]
    public class AppConfig
    {
        public NetworkConfig network;
        public MappingConfig mapping;
    }

    // ────────────────────────────────────────────────
    //  ConfigManager — chargement dynamique (P1)
    // ────────────────────────────────────────────────

    /// <summary>
    /// Charge et sauvegarde la configuration depuis StreamingAssets/config.json.
    /// Satisfait l'exigence P1 : configuration dynamique de l'installation physique.
    /// </summary>
    public class ConfigManager : MonoBehaviour
    {
        public static ConfigManager Instance { get; private set; }
        public static AppConfig Config { get; private set; }

        /// <summary>Événement déclenché après chaque rechargement de la config.</summary>
        public static event Action OnConfigReloaded;

        private string ConfigPath => Path.Combine(Application.streamingAssetsPath, "config.json");

        private void Awake()
        {
            // Singleton
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadConfig();
        }

        /// <summary>
        /// Charge la configuration depuis le fichier JSON.
        /// Peut être appelé à nouveau pour recharger à chaud.
        /// </summary>
        public void LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.LogError($"[ConfigManager] Fichier introuvable : {ConfigPath}");
                CreateDefaultConfig();
                return;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                Config = JsonUtility.FromJson<AppConfig>(json);
                Debug.Log($"[ConfigManager] Config chargée — {Config.network.controllers.Length} contrôleurs, " +
                          $"{Config.mapping.ledCount} LEDs ({Config.mapping.screenWidth}×{Config.mapping.screenHeight})");
                OnConfigReloaded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigManager] Erreur de lecture : {e.Message}");
            }
        }

        /// <summary>
        /// Sauvegarde la configuration courante dans le fichier JSON.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                string json = JsonUtility.ToJson(Config, prettyPrint: true);
                File.WriteAllText(ConfigPath, json);
                Debug.Log($"[ConfigManager] Configuration sauvegardée dans {ConfigPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigManager] Erreur de sauvegarde : {e.Message}");
            }
        }

        /// <summary>
        /// Crée une configuration par défaut si le fichier est absent.
        /// </summary>
        private void CreateDefaultConfig()
        {
            Config = new AppConfig
            {
                network = new NetworkConfig
                {
                    controllers = new[]
                    {
                        // Un seul contrôleur couvre tous les univers 0-385
                        // (65 536 LEDs ÷ 170 LEDs/univers = 386 univers)
                        new ControllerConfig { ip = "192.168.1.45", startUniverse = 1, universeCount = 386 }
                    },
                    eHubEnabled = true,
                    eHubPort = 9000,
                    eHubSessionId = "mon-equipe",
                    eHubPeers = new string[0],
                    artNetPort = 6454
                },
                mapping = new MappingConfig
                {
                    layout        = "Matrix2D",
                    ledCount      = 65536,       // 256 × 256
                    screenWidth   = 256,
                    screenHeight  = 256,
                    pixelOrder    = "RGB",
                    channelsPerLed = 3,
                    serpentine    = true,         // Matrice câblée en zigzag
                    strips        = new StripConfig[0],
                    lyres         = new LyreConfig[0]
                }
            };

            SaveConfig();
            Debug.Log("[ConfigManager] Configuration par défaut créée.");
            OnConfigReloaded?.Invoke();
        }
    }
}
