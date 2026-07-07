using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Interface commune pour fournir l'état des LEDs.
    /// Garantit le découplage entre le routage réseau et la génération visuelle.
    /// L'authoring remplit l'état ; le routage l'achemine — ils ne se connaissent pas. (P4)
    /// </summary>
    public interface IStateProvider
    {
        /// <summary>
        /// Récupère l'état actuel de toutes les LEDs (écran principal).
        /// L'index correspond à l'index linéaire du pixel (row-major, gauche→droite, haut→bas).
        /// </summary>
        /// <returns>Un tableau de Color32 de longueur <c>AppConfig.mapping.ledCount</c>.</returns>
        Color32[] GetState();

        /// <summary>
        /// Récupère l'état des lyres/spots (dispositifs spéciaux).
        /// Chaque LyreState contient les valeurs DMX brutes à envoyer.
        /// Peut retourner null si aucun dispositif spécial n'est présent.
        /// </summary>
        LyreState[] GetLyreStates();
    }

    /// <summary>
    /// État DMX d'une lyre ou d'un projecteur mobile.
    /// Chaque valeur correspond à un canal DMX (0-255).
    /// </summary>
    [System.Serializable]
    public class LyreState
    {
        public string lyreName;     // Référence à LyreConfig.name
        public float pan;           // Panoramique 0-255
        public float tilt;          // Inclinaison 0-255
        public float dimmer;        // Intensité 0-255
        public Color32 color;       // Couleur RGB
        public float strobe;        // Stroboscope 0-255 (0 = off)
        public float gobo;          // Gobo 0-255
    }
}
