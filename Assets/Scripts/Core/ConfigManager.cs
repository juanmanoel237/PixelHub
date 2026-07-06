using System.IO;
using UnityEngine;

namespace Laps.Core
{
    [System.Serializable]
    public class ControllerConfig
    {
        public string ip;
        public int startUniverse;
    }

    [System.Serializable]
    public class NetworkConfig
    {
        public ControllerConfig[] controllers;
        public int eHubPort;
    }

    [System.Serializable]
    public class MappingConfig
    {
        public int ledCount;
    }

    [System.Serializable]
    public class AppConfig
    {
        public NetworkConfig network;
        public MappingConfig mapping;
    }

    /// <summary>
    /// Charge dynamiquement la configuration depuis le dossier StreamingAssets.
    /// (Satisfera l'exigence P1 de Configuration dynamique)
    /// </summary>
    public class ConfigManager : MonoBehaviour
    {
        public static AppConfig Instance { get; private set; }

        private string ConfigPath => Path.Combine(Application.streamingAssetsPath, "config.json");

        private void Awake()
        {
            LoadConfig();
        }

        public void LoadConfig()
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                // Unity's built-in JsonUtility can handle this structure
                Instance = JsonUtility.FromJson<AppConfig>(json);
                Debug.Log($"Configuration chargée avec succès ! Port eHub : {Instance.network.eHubPort}, Nombre de LEDs : {Instance.mapping.ledCount}");
            }
            else
            {
                Debug.LogError($"Fichier de configuration introuvable à l'emplacement : {ConfigPath}");
            }
        }
    }
}
