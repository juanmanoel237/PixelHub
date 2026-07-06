using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Interface commune pour fournir l'état des LEDs.
    /// Garantit le découplage entre le routage réseau et la génération visuelle.
    /// </summary>
    public interface IStateProvider
    {
        /// <summary>
        /// Récupère l'état actuel de toutes les LEDs.
        /// </summary>
        /// <returns>Un tableau de couleurs représentant l'état du système.</returns>
        Color32[] GetState();
    }
}
