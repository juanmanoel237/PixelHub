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
        public int universe;        // Univers local ArtNet (0-31 par contrôleur)
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

        // Mapping serpéntin actif ?
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

            bool isLapsWall128 = !string.IsNullOrEmpty(mapping.layout) &&
                                 mapping.layout.Trim().ToLowerInvariant() == "lapswall128";

            // Nombre de LEDs par univers DMX
            // Un univers = 512 canaux. Ex: RGB → 170 LEDs/univers ; RGBW → 128 LEDs/univers
            int ledsPerUniverse = 512 / ChannelsPerLed;

            for (int ledIndex = 0; ledIndex < LedCount; ledIndex++)
            {
                int ctrlIndex;
                int universe;
                int channel;

                if (isLapsWall128)
                {
                    // Mur LAPS : 128×128 visibles.
                    // 64 bandes (strips) de 259 LEDs : 1 invisible (bas), 128 visibles vers le haut,
                    // 1 invisible (haut), 128 visibles vers le bas, 1 invisible (bas).
                    // Chaque bande couvre 2 colonnes (montée/descente), donc 64 bandes → 128 colonnes.
                    //
                    // Univers Art-Net : 0..31 par contrôleur (32 univers), 2 univers par bande (car 259 > 170).
                    // Contrôleurs : 4 quarts de 32 colonnes → 16 bandes → 32 univers, IPs 45..48.

                    int x = ledIndex % 128;
                    int y = ledIndex / 128;

                    ctrlIndex = x / 32;               // 0..3 (4 contrôleurs)
                    int xInQuarter = x % 32;          // 0..31
                    int stripInQuarter = xInQuarter / 2; // 0..15 (16 bandes par quart)
                    int side = xInQuarter % 2;        // 0 = colonne "montée", 1 = colonne "descente"

                    // Index LED dans la bande (0..258), en comptant les LEDs invisibles.
                    int stripLedIndex;
                    if (side == 0)
                    {
                        // Montée : visibles (y=0 en haut → LED 128, y=127 en bas → LED 1)
                        stripLedIndex = 1 + (127 - y);
                    }
                    else
                    {
                        // Descente : visibles (y=0 en haut → LED 130, y=127 en bas → LED 257)
                        stripLedIndex = 130 + y;
                    }

                    int universeInStrip = stripLedIndex >= 170 ? 1 : 0; // 0 ou 1
                    universe = stripInQuarter * 2 + universeInStrip;    // 0..31 (par contrôleur)

                    int ledIndexInUniverse = stripLedIndex % 170;        // 0..169
                    channel = ledIndexInUniverse * ChannelsPerLed;       // 0..509
                }
                else
                {
                    // ── Mapping "Matrix2D" (avec serpentin si actif) ──
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

                    int universeSlot = physicalIndex / ledsPerUniverse;
                    channel = (physicalIndex % ledsPerUniverse) * ChannelsPerLed;

                    ctrlIndex = -1;
                    universe = -1;
                    if (TryResolveUniverseSlot(network.controllers, universeSlot, out ctrlIndex, out int absoluteUniverse))
                    {
                        // Convertir univers absolu interne → univers local ArtNet par contrôleur
                        universe = absoluteUniverse - network.controllers[ctrlIndex].startUniverse;
                    }
                }

                PixelMap[ledIndex] = new LEDAddress
                {
                    controllerIndex = ctrlIndex,
                    universe = universe,
                    channel = channel
                };

                // Enregistrer dans le mapping inversé
                if (ctrlIndex >= 0 && universe >= 0)
                {
                    if (!ControllerUniverses.ContainsKey(ctrlIndex))
                        ControllerUniverses[ctrlIndex] = new List<int>();

                    if (!ControllerUniverses[ctrlIndex].Contains(universe))
                        ControllerUniverses[ctrlIndex].Add(universe);
                }
            }

            if (LedCount > 0 && PixelMap[0].controllerIndex >= 0)
            {
                Debug.Log($"[PixelMapping] 1ère LED → contrôleur {PixelMap[0].controllerIndex}, " +
                          $"univers {PixelMap[0].universe}, canal DMX {PixelMap[0].channel + 1}");
            }

            Debug.Log($"[PixelMapping] Mapping construit : {LedCount} LEDs, " +
                      $"{ledsPerUniverse} LEDs/univers, {ChannelsPerLed} canaux/LED, " +
                      $"serpentin={IsSerpentine}, layout={mapping.layout ?? "Matrix2D"}");
        }

        /// <summary>
        /// Résout un slot d'univers global vers (contrôleur, univers absolu interne).
        /// </summary>
        private static bool TryResolveUniverseSlot(
            ControllerConfig[] controllers,
            int universeSlot,
            out int controllerIndex,
            out int absoluteUniverse)
        {
            controllerIndex = -1;
            absoluteUniverse = -1;

            if (controllers == null || universeSlot < 0) return false;

            int remaining = universeSlot;
            for (int i = 0; i < controllers.Length; i++)
            {
                var c = controllers[i];
                int count = c.universeCount > 0 ? c.universeCount : 32;
                if (remaining < count)
                {
                    controllerIndex = i;
                    absoluteUniverse = c.startUniverse + remaining;
                    return true;
                }
                remaining -= count;
            }
            return false;
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
