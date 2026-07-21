using System;
using System.Collections.Generic;

namespace Laps.Core
{
    /// <summary>Suit les postes eHub actifs via messages Hello / trafic.</summary>
    public class EHubPeerTracker
    {
        private readonly Dictionary<string, long> _lastSeenMs = new Dictionary<string, long>();
        private readonly Dictionary<string, string> _peerIps = new Dictionary<string, string>();

        public void Note(string senderId, string ip)
        {
            if (string.IsNullOrEmpty(senderId)) return;
            _lastSeenMs[senderId] = NowMs();
            if (!string.IsNullOrEmpty(ip))
                _peerIps[senderId] = ip;
        }

        public int ActivePeerCount(float timeoutSeconds = 6f)
        {
            long now = NowMs();
            long timeoutMs = (long)(timeoutSeconds * 1000f);
            int count = 0;
            foreach (var kv in _lastSeenMs)
                if (now - kv.Value <= timeoutMs)
                    count++;
            return count;
        }

        public int TotalPostes(float timeoutSeconds = 6f) => ActivePeerCount(timeoutSeconds) + 1;

        public string FormatActivePeers(float timeoutSeconds = 6f)
        {
            long now = NowMs();
            long timeoutMs = (long)(timeoutSeconds * 1000f);
            var labels = new List<string>();
            foreach (var kv in _lastSeenMs)
            {
                if (now - kv.Value > timeoutMs) continue;
                string ip = _peerIps.TryGetValue(kv.Key, out string found) ? found : "?";
                labels.Add($"{kv.Key}@{ip}");
            }
            return labels.Count == 0 ? "—" : string.Join(", ", labels);
        }

        private static long NowMs() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
    }
}
