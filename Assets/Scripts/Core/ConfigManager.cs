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
        public int eHubPort;
        public int artNetPort;
    }

    [Serializable]
    public class MappingConfig
    {
        public int ledCount;
        public int screenWidth;
        public int screenHeight;
        public string pixelOrder;
        public int channelsPerLed;
        public bool serpentine;
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
                        new ControllerConfig { ip = "192.168.1.45", startUniverse = 1, universeCount = 386 }
                    },
                    eHubPort = 9000,
                    artNetPort = 6454
                },
                mapping = new MappingConfig
                {
                    ledCount       = 65536,
                    screenWidth    = 256,
                    screenHeight   = 256,
                    pixelOrder     = "RGB",
                    channelsPerLed = 3,
                    serpentine     = true,
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
