using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Effet DANMARK conçu pour être déclenché depuis la Timeline Unity 
    /// (via des Signals ou des Animation Events).
    /// </summary>
    public class DanmarkKeyEffect : MonoBehaviour
    {
        /// <summary>
        /// Lance une lettre volante sur la grille LED.
        /// À appeler depuis la Timeline avec un paramètre String ("D", "A", "N", etc.).
        /// </summary>
        public void SpawnLetter(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return;

            char ch = letter[0];
            LedTextOverlay.SpawnLetter(ch);
            Debug.Log($"[Danmark Timeline] Lettre '{ch}' lancée !");
        }

        /// <summary>
        /// Affiche le mot complet "DANEMARK" en bas de l'écran.
        /// À appeler depuis la Timeline (Signal sans paramètre).
        /// </summary>
        public void ShowDanmarkComplete()
        {
            LedTextOverlay.SetDanmarkComplete(true);
            Debug.Log("[Danmark Timeline] ★ DANEMARK complet déclenché !");
        }

        /// <summary>
        /// Désactive l'affichage du mot complet.
        /// </summary>
        public void HideDanmarkComplete()
        {
            LedTextOverlay.SetDanmarkComplete(false);
        }
    }
}
