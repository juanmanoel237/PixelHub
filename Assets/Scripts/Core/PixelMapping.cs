using System.Collections.Generic;
using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Représente le mapping d'une LED individuelle vers son contrôleur/univers/canal DMX.
    /// Structure légère pour ne pas gaspiller de mémoire sur 16k+ LEDs.
    /// </summary>
    public struct LEDAddress
    {
        public int controllerIndex; // Index dans ConfigManager.Config.network.controllers
        public int universe;        // Univers DMX absolu (0-127)
        public int channel;         // Canal dans l'univers (0-511), multiple de channelsPerLed
    }

    /// <summary>
    /// Calcule et expose le mapping complet : index LED → adresse DMX.
    /// Satisfait P4 (architecture flexible) : l'authoring n'a besoin que d'un index de pixel,
    /// le mapping physique est entièrement géré ici et rechargé à la volée si la config change.
    /// </summary>
    public class PixelMapping
    {
        // Tableau principal : pixelMap[ledIndex] = LEDAddress
        public LEDAddress[] PixelMap { get; private set; }

        // Nombre total de LEDs mappées
        public int LedCount { get; private set; }

        // Largeur de l'écran (nécessaire pour le calcul serpentin)
        public int ScreenWidth { get; private set; }

        // Mapping serpéntin actif ?
        public bool IsSerpentine { get; private set; }

        // Mapping inversé : controllerIndex → liste des univers qu'il gère
        public Dictionary<int, List<int>> ControllerUniverses { get; private set; }

        // Nombre de canaux par LED (3=RGB, 4=RGBW)
        public int ChannelsPerLed { get; private set; }

        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Construit le mapping à partir de la config courante.
        /// À appeler après chaque rechargement de config (ConfigManager.OnConfigReloaded).
        /// </summary>
        public void Build(AppConfig config)
        {
            var mapping = config.mapping;
            var network = config.network;

            LedCount      = mapping.ledCount;
            ScreenWidth   = mapping.screenWidth > 0 ? mapping.screenWidth : 128;
            IsSerpentine  = mapping.serpentine;
            ChannelsPerLed = mapping.channelsPerLed > 0 ? mapping.channelsPerLed : 3;
            PixelMap      = new LEDAddress[LedCount];
            ControllerUniverses = new Dictionary<int, List<int>>();

            // Nombre de LEDs par univers DMX
            // Un univers = 512 canaux. Ex: RGB → 170 LEDs/univers ; RGBW → 128 LEDs/univers
            int ledsPerUniverse = 512 / ChannelsPerLed;

            for (int ledIndex = 0; ledIndex < LedCount; ledIndex++)
            {
                // ── Calcul de l'adresse physique (avec serpentin si actif) ──
                int physicalIndex;
                if (IsSerpentine)
                {
                    int y = ledIndex / ScreenWidth;
                    int x = ledIndex % ScreenWidth;
                    // Lignes impaires : câblage de droite à gauche
                    int physX = (y % 2 == 1) ? (ScreenWidth - 1 - x) : x;
                    physicalIndex = y * ScreenWidth + physX;
                }
                else
                {
                    physicalIndex = ledIndex;
                }

                int universeSlot = physicalIndex / ledsPerUniverse; // 0, 1, 2… dans l'installation
                int channel      = (physicalIndex % ledsPerUniverse) * ChannelsPerLed;

                // Univers Art-Net absolu = startUniverse + slot (comme send-artnet.js UNIVERSE=1)
                int ctrlIndex = FindControllerForUniverseSlot(network.controllers, universeSlot);
                int absoluteUniverse = ctrlIndex >= 0
                    ? network.controllers[ctrlIndex].startUniverse + universeSlot
                    : -1;

                PixelMap[ledIndex] = new LEDAddress
                {
                    controllerIndex = ctrlIndex,
                    universe = absoluteUniverse,
                    channel = channel
                };

                // Enregistrer dans le mapping inversé
                if (ctrlIndex >= 0 && absoluteUniverse >= 0)
                {
                    if (!ControllerUniverses.ContainsKey(ctrlIndex))
                        ControllerUniverses[ctrlIndex] = new List<int>();

                    if (!ControllerUniverses[ctrlIndex].Contains(absoluteUniverse))
                        ControllerUniverses[ctrlIndex].Add(absoluteUniverse);
                }
            }

            if (LedCount > 0 && PixelMap[0].controllerIndex >= 0)
            {
                Debug.Log($"[PixelMapping] 1ère LED → univers {PixelMap[0].universe}, canal DMX {PixelMap[0].channel + 1} " +
                          $"(startUniverse={network.controllers[0].startUniverse})");
            }

            Debug.Log($"[PixelMapping] Mapping construit : {LedCount} LEDs, " +
                      $"{ledsPerUniverse} LEDs/univers, {ChannelsPerLed} canaux/LED, " +
                      $"serpentin={IsSerpentine}");
        }

        /// <summary>
        /// Trouve le contrôleur qui gère le N-ième univers de l'installation (slot 0-based).
        /// </summary>
        private int FindControllerForUniverseSlot(ControllerConfig[] controllers, int universeSlot)
        {
            if (controllers == null || universeSlot < 0) return -1;
            for (int i = 0; i < controllers.Length; i++)
            {
                var c = controllers[i];
                int count = c.universeCount > 0 ? c.universeCount : 32;
                if (universeSlot < count)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Retourne l'adresse DMX d'une LED par ses coordonnées (x, y) sur l'écran.
        /// </summary>
        public LEDAddress GetAddressByXY(int x, int y, int screenWidth)
        {
            int index = y * screenWidth + x;
            if (index < 0 || index >= LedCount)
                return new LEDAddress { controllerIndex = -1 };
            return PixelMap[index];
        }

        /// <summary>
        /// Nombre total d'univers utilisés par ce mapping.
        /// </summary>
        public int TotalUniverses => LedCount > 0 ? (LedCount / (512 / ChannelsPerLed)) + 1 : 0;
    }
}
