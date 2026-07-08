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
        public int universe;        // Univers DMX (0-31 par contrôleur)
        public int channel;         // Canal dans l'univers (0-511), multiple de channelsPerLed
    }

    /// <summary>
    /// Calcule et expose le mapping complet : pixel visible (x, y) → adresse DMX.
    ///
    /// Layout physique du mur LED Glassworks :
    /// - 64 bandes verticales, chacune avec 259 LEDs (montée + descente)
    /// - Chaque bande crée 2 colonnes visibles de 128 LEDs
    /// - 4 contrôleurs BC216 (16 bandes chacun = 32 univers)
    ///
    /// Structure d'une bande (259 LEDs, 0-indexed) :
    ///   LED  0     : invisible (fixation bas)
    ///   LEDs 1-128 : 128 visibles (montée, bas → haut)
    ///   LED  129   : invisible (fixation haut)
    ///   LEDs 130-257: 128 visibles (descente, haut → bas)
    ///   LED  258   : invisible (fixation bas)
    ///
    /// DMX : 170 LEDs RGB par univers → 2 univers par bande de 259 LEDs
    /// </summary>
    public class PixelMapping
    {
        // Tableau principal : pixelMap[ledIndex] = LEDAddress
        // ledIndex = y * screenWidth + x (pixel visible sur l'écran 128×128)
        public LEDAddress[] PixelMap { get; private set; }

        // Nombre total de LEDs visibles
        public int LedCount { get; private set; }

        // Dimensions de l'écran visible
        public int ScreenWidth { get; private set; }
        public int ScreenHeight { get; private set; }

        // Mapping serpéntin actif ?
        public bool IsSerpentine { get; private set; }

        // Mapping inversé : controllerIndex → liste des univers qu'il gère
        public Dictionary<int, List<int>> ControllerUniverses { get; private set; }

        // Nombre de canaux par LED (3=RGB, 4=RGBW)
        public int ChannelsPerLed { get; private set; }

        // Configuration des bandes
        private int _totalStrips;
        private int _ledsPerStrip;
        private int _visibleLedsPerColumn;
        private int _stripsPerController;

        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Construit le mapping à partir de la config courante.
        /// À appeler après chaque rechargement de config (ConfigManager.OnConfigReloaded).
        /// </summary>
        public void Build(AppConfig config)
        {
            var mapping = config.mapping;
            var network = config.network;

            ScreenWidth  = mapping.screenWidth  > 0 ? mapping.screenWidth  : 128;
            ScreenHeight = mapping.screenHeight > 0 ? mapping.screenHeight : 128;
            LedCount     = ScreenWidth * ScreenHeight;
            IsSerpentine = mapping.serpentine;
            ChannelsPerLed = mapping.channelsPerLed > 0 ? mapping.channelsPerLed : 3;

            _totalStrips          = mapping.totalStrips          > 0 ? mapping.totalStrips          : 64;
            _ledsPerStrip         = mapping.ledsPerStrip         > 0 ? mapping.ledsPerStrip         : 259;
            _visibleLedsPerColumn = mapping.visibleLedsPerColumn > 0 ? mapping.visibleLedsPerColumn : 128;
            _stripsPerController  = mapping.stripsPerController  > 0 ? mapping.stripsPerController  : 16;

            PixelMap = new LEDAddress[LedCount];
            ControllerUniverses = new Dictionary<int, List<int>>();

            // Nombre de LEDs par univers DMX
            int ledsPerUniverse = 512 / ChannelsPerLed; // 170 pour RGB

            for (int y = 0; y < ScreenHeight; y++)
            {
                for (int x = 0; x < ScreenWidth; x++)
                {
                    int pixelIndex = y * ScreenWidth + x;

                    // ── Trouver la bande physique et la LED physique ──

                    // Chaque bande crée 2 colonnes visibles
                    int stripIndex = x / 2;
                    bool isUpColumn = (x % 2 == 0); // Colonne paire = montée

                    // LED physique dans la bande (0-indexed, sur 259)
                    int physicalLed;
                    if (isUpColumn)
                    {
                        // Montée : LED 1 (bas) à LED 128 (haut)
                        // Screen y=0 (haut) → LED physique 128
                        // Screen y=127 (bas) → LED physique 1
                        physicalLed = _visibleLedsPerColumn - y; // 128, 127, ..., 1
                    }
                    else
                    {
                        // Descente : LED 130 (haut) à LED 257 (bas)
                        // Screen y=0 (haut) → LED physique 130
                        // Screen y=127 (bas) → LED physique 257
                        physicalLed = (_visibleLedsPerColumn + 1) + 1 + y; // 130, 131, ..., 257
                    }

                    // ── Calculer l'univers et le canal DMX ──

                    // Chaque bande a 2 univers consécutifs au sein de son contrôleur
                    int stripLocalIndex = stripIndex % _stripsPerController; // 0-15 dans le contrôleur
                    int universeBase = stripLocalIndex * 2; // Univers de base (0, 2, 4, ...)

                    int universeOffset = physicalLed / ledsPerUniverse; // 0 ou 1
                    int universe = universeBase + universeOffset;
                    int channel = (physicalLed % ledsPerUniverse) * ChannelsPerLed;

                    // ── Trouver le contrôleur ──

                    int controllerIndex = stripIndex / _stripsPerController; // 0, 1, 2, 3
                    if (controllerIndex >= network.controllers.Length)
                        controllerIndex = network.controllers.Length - 1;

                    PixelMap[pixelIndex] = new LEDAddress
                    {
                        controllerIndex = controllerIndex,
                        universe = universe,
                        channel = channel
                    };

                    // Enregistrer dans le mapping inversé
                    if (!ControllerUniverses.ContainsKey(controllerIndex))
                        ControllerUniverses[controllerIndex] = new List<int>();

                    if (!ControllerUniverses[controllerIndex].Contains(universe))
                        ControllerUniverses[controllerIndex].Add(universe);
                }
            }

            // Logs de diagnostic
            if (LedCount > 0)
            {
                var first = PixelMap[0];
                var last  = PixelMap[LedCount - 1];
                Debug.Log($"[PixelMapping] 1ère LED (0,0) → ctrl[{first.controllerIndex}] " +
                          $"univ {first.universe}, canal {first.channel}");
                Debug.Log($"[PixelMapping] Dernière LED ({ScreenWidth-1},{ScreenHeight-1}) → " +
                          $"ctrl[{last.controllerIndex}] univ {last.universe}, canal {last.channel}");
            }

            Debug.Log($"[PixelMapping] Mapping construit : {LedCount} LEDs visibles ({ScreenWidth}×{ScreenHeight}), " +
                      $"{_totalStrips} bandes de {_ledsPerStrip} LEDs, " +
                      $"{ledsPerUniverse} LEDs/univers, {ChannelsPerLed} canaux/LED, " +
                      $"{network.controllers.Length} contrôleurs");
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
        public int TotalUniverses => _totalStrips * 2; // 2 univers par bande
    }
}
