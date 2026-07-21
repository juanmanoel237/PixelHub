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
        /// <summary>Lance une lettre volante sur la grille LED (local + sync eHub).</summary>
        public void RequestSpawnLetter(string letter)
        {
            ApplySpawnLetter(letter);
            if (!string.IsNullOrEmpty(letter))
            {
                EHubSyncBus.PublishLocal(new EHubMessage
                {
                    type = EHubMessageTypes.DanmarkLetter,
                    stringArg = letter.Substring(0, 1)
                });
            }
        }

        /// <summary>À appeler depuis la Timeline avec un paramètre String ("D", "A", "N", etc.).</summary>
        public void SpawnLetter(string letter)
        {
            ApplySpawnLetter(letter);
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.DanmarkLetter,
                stringArg = string.IsNullOrEmpty(letter) ? "" : letter.Substring(0, 1)
            });
        }

        public void RequestShowDanmarkComplete()
        {
            ApplyShowDanmarkComplete();
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.DanmarkLetter,
                stringArg = EHubDanmarkAction.Complete
            });
        }

        public void ShowDanmarkComplete()
        {
            ApplyShowDanmarkComplete();
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.DanmarkLetter,
                stringArg = EHubDanmarkAction.Complete
            });
        }

        public void RequestHideDanmarkComplete()
        {
            ApplyHideDanmarkComplete();
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.DanmarkLetter,
                stringArg = EHubDanmarkAction.Hide
            });
        }

        public void HideDanmarkComplete()
        {
            ApplyHideDanmarkComplete();
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.DanmarkLetter,
                stringArg = EHubDanmarkAction.Hide
            });
        }

        public void ApplyDanmarkFromNetwork(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;
            if (arg == EHubDanmarkAction.Complete) { ApplyShowDanmarkComplete(); return; }
            if (arg == EHubDanmarkAction.Hide) { ApplyHideDanmarkComplete(); return; }
            ApplySpawnLetter(arg);
        }

        private void ApplySpawnLetter(string letter)
        {
            if (string.IsNullOrEmpty(letter)) return;
            char ch = letter[0];
            LedTextOverlay.SpawnLetter(ch);
            Debug.Log($"[Danmark] Lettre '{ch}' lancée !");
        }

        private void ApplyShowDanmarkComplete()
        {
            LedTextOverlay.SetDanmarkComplete(true);
            Debug.Log("[Danmark] ★ DANEMARK complet déclenché !");
        }

        private void ApplyHideDanmarkComplete()
        {
            LedTextOverlay.SetDanmarkComplete(false);
        }
    }
}
