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
    }

    [Serializable]
    public class StripConfig
    {
        public string name;
        public int ledCount;
        public string controllerIp;
        public int universe;
        public int startChannel;
        public int channelsPerLed;
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
        public int ehubProtocolPort;  // Port UDP protocole eHuB externe (LED entités), distinct du sync équipe
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

    /// <summary>
    /// Charge et sauvegarde la configuration depuis StreamingAssets/config.json.
    /// Recharge le mapping entités depuis CSV (section router.files).
    /// </summary>
    public class ConfigManager : MonoBehaviour
    {
        public static ConfigManager Instance { get; private set; }
        public static ExtendedAppConfig Config { get; private set; }

        /// <summary>Mapping entité → (contrôleur, univers, canal) chargé depuis CSV.</summary>
        public static EntityMapping EntityMap { get; private set; } = new EntityMapping();

        /// <summary>Chemin absolu du CSV de mapping (null si non configuré).</summary>
        public static string EntityMappingCsvPath { get; private set; }

        public static event Action OnConfigReloaded;

        private string ConfigPath => Path.Combine(Application.streamingAssetsPath, "config.json");

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadConfig();
        }

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
                Config = JsonUtility.FromJson<ExtendedAppConfig>(json);
                LoadEntityMapping();

                Debug.Log($"[ConfigManager] Config chargée — {Config.network.controllers.Length} contrôleurs, " +
                          $"{Config.mapping.ledCount} LEDs ({Config.mapping.screenWidth}×{Config.mapping.screenHeight}), " +
                          $"mapping CSV: {EntityMappingCsvPath ?? "aucun"} ({EntityMap.Count} entités)");
                OnConfigReloaded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ConfigManager] Erreur de lecture : {e.Message}");
            }
        }

        public void SaveConfig()
        {
            if (Config == null) return;

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
        /// Modifie l'IP d'un contrôleur, sauvegarde et notifie le routeur (P1).
        /// </summary>
        public void SetControllerIp(int controllerIndex, string ip)
        {
            if (Config?.network?.controllers == null ||
                controllerIndex < 0 ||
                controllerIndex >= Config.network.controllers.Length)
            {
                Debug.LogWarning($"[ConfigManager] Index contrôleur invalide : {controllerIndex}");
                return;
            }

            Config.network.controllers[controllerIndex].ip = ip;
            SaveConfig();
            Debug.Log($"[ConfigManager] IP contrôleur {controllerIndex} → {ip}");
            OnConfigReloaded?.Invoke();
        }

        /// <summary>
        /// Met à jour toutes les IP contrôleurs, sauvegarde une fois et notifie le routeur (P1 UI).
        /// </summary>
        public bool SetAllControllerIps(string[] ips)
        {
            if (Config?.network?.controllers == null || ips == null)
                return false;

            int n = Config.network.controllers.Length;
            if (ips.Length < n)
            {
                Debug.LogWarning("[ConfigManager] Tableau IP trop court.");
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                string ip = ips[i]?.Trim();
                if (string.IsNullOrEmpty(ip))
                {
                    Debug.LogWarning($"[ConfigManager] IP vide pour contrôleur {i}.");
                    return false;
                }
                Config.network.controllers[i].ip = ip;
            }

            SaveConfig();
            Debug.Log("[ConfigManager] IPs contrôleurs mises à jour.");
            OnConfigReloaded?.Invoke();
            return true;
        }

        private void LoadEntityMapping()
        {
            EntityMappingCsvPath = null;
            EntityMap.Clear();

            string relativePath = Config?.router?.files?.entityMappingCsv;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                Debug.LogWarning("[ConfigManager] Aucun router.files.entityMappingCsv défini dans config.json");
                return;
            }

            EntityMappingCsvPath = Path.Combine(Application.streamingAssetsPath, relativePath);

            if (!File.Exists(EntityMappingCsvPath))
            {
                Debug.LogError($"[ConfigManager] Fichier mapping introuvable : {EntityMappingCsvPath}");
                return;
            }

            EntityMap.LoadFromCsv(EntityMappingCsvPath);
        }

        private void CreateDefaultConfig()
        {
            Config = new ExtendedAppConfig
            {
                network = new NetworkConfig
                {
                    controllers = new[]
                    {
                        new ControllerConfig { ip = "192.168.1.45", startUniverse = 0, universeCount = 32 },
                        new ControllerConfig { ip = "192.168.1.46", startUniverse = 0, universeCount = 32 },
                        new ControllerConfig { ip = "192.168.1.47", startUniverse = 0, universeCount = 32 },
                        new ControllerConfig { ip = "192.168.1.48", startUniverse = 0, universeCount = 32 }
                    },
                    eHubEnabled = true,
                    eHubPort = 9000,
                    eHubSessionId = "mon-equipe",
                    eHubPeers = new string[0],
                    artNetPort = 6454,
                    ehubProtocolPort = 9001
                },
                mapping = new MappingConfig
                {
                    layout         = "LapsWall128",
                    ledCount       = 16384,
                    screenWidth    = 128,
                    screenHeight   = 128,
                    pixelOrder     = "RGB",
                    channelsPerLed = 3,
                    serpentine     = false,
                    strips         = new StripConfig[0],
                    lyres          = new LyreConfig[0]
                },
                router = new RouterConfig
                {
                    files = new RouterFilesConfig
                    {
                        entityMappingCsv = "mapping/led_wall_mapping.csv"
                    }
                }
            };

            SaveConfig();
            LoadEntityMapping();
            Debug.Log("[ConfigManager] Configuration par défaut créée.");
            OnConfigReloaded?.Invoke();
        }
    }
}
