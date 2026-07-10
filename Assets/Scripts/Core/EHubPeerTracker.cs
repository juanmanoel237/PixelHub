using System.Collections.Generic;
using UnityEngine;

namespace Laps.Core
{
    /// <summary>Suit les postes eHub actifs via messages Hello / trafic.</summary>
    public class EHubPeerTracker
    {
        private readonly Dictionary<string, float> _lastSeen = new Dictionary<string, float>();
        private readonly Dictionary<string, string> _peerIps = new Dictionary<string, string>();

        public void Note(string senderId, string ip)
        {
            if (string.IsNullOrEmpty(senderId)) return;
            _lastSeen[senderId] = Time.realtimeSinceStartup;
            if (!string.IsNullOrEmpty(ip))
                _peerIps[senderId] = ip;
        }

        public int ActivePeerCount(float timeoutSeconds = 6f)
        {
            float now = Time.realtimeSinceStartup;
            int count = 0;
            foreach (var kv in _lastSeen)
                if (now - kv.Value <= timeoutSeconds)
                    count++;
            return count;
        }

        public int TotalPostes(float timeoutSeconds = 6f) => ActivePeerCount(timeoutSeconds) + 1;

        public string FormatActivePeers(float timeoutSeconds = 6f)
        {
            float now = Time.realtimeSinceStartup;
            var labels = new List<string>();
            foreach (var kv in _lastSeen)
            {
                if (now - kv.Value > timeoutSeconds) continue;
                string ip = _peerIps.TryGetValue(kv.Key, out string found) ? found : "?";
                labels.Add($"{kv.Key}@{ip}");
            }
            return labels.Count == 0 ? "—" : string.Join(", ", labels);
        }
    }
}
