using System.Collections.Generic;
using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Effet DANMARK : Shift+D/A/N/E/M/A/R/K déclenche une lettre volante sur le mur LED.
    /// Chaque lettre traverse l'écran avec une trajectoire unique.
    /// Quand toutes les lettres distinctes ont été tapées, le mot "DANEMARK" s'affiche en bas.
    /// </summary>
    public class DanmarkKeyEffect : MonoBehaviour
    {
        // Les 7 touches distinctes à surveiller (le mot DANEMARK a 8 lettres mais 7 uniques)
        private static readonly KeyCode[] DanmarkKeys = new KeyCode[]
        {
            KeyCode.D,
            KeyCode.A,
            KeyCode.N,
            KeyCode.E,
            KeyCode.M,
            KeyCode.R,
            KeyCode.K,
        };

        // Caractère correspondant à chaque KeyCode
        private static readonly char[] DanmarkChars = new char[]
        {
            'D', 'A', 'N', 'E', 'M', 'R', 'K',
        };

        private readonly HashSet<char> _seenLetters = new HashSet<char>();

        void Update()
        {
            // Shift requis pour éviter les conflits avec les raccourcis existants
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shiftHeld) return;

            for (int i = 0; i < DanmarkKeys.Length; i++)
            {
                if (!Input.GetKeyDown(DanmarkKeys[i])) continue;

                char ch = DanmarkChars[i];

                // Lancer la lettre volante (réappui = relance)
                LedTextOverlay.SpawnLetter(ch);

                // Marquer comme vue pour la complétion du mot
                _seenLetters.Add(ch);

                // Sync réseau eHub
                EHubSyncBus.PublishLocal(new EHubMessage
                {
                    type = EHubMessageTypes.DanmarkLetter,
                    stringArg = ch.ToString()
                });

                Debug.Log($"[Danmark] Lettre '{ch}' lancée ! ({_seenLetters.Count}/7)");

                // Vérifier si toutes les lettres ont été tapées
                if (_seenLetters.Count >= 7)
                {
                    LedTextOverlay.SetDanmarkComplete(true);
                    Debug.Log("[Danmark] ★ DANEMARK complet ! Mot affiché en bas.");
                    _seenLetters.Clear(); // Reset pour pouvoir refaire
                }
            }
        }

        /// <summary>Appelé depuis le réseau eHub (autre poste).</summary>
        public void TriggerLetterFromNetwork(char ch)
        {
            LedTextOverlay.SpawnLetter(ch);
            _seenLetters.Add(char.ToUpperInvariant(ch));

            if (_seenLetters.Count >= 7)
            {
                LedTextOverlay.SetDanmarkComplete(true);
                _seenLetters.Clear();
            }
        }
    }
}
