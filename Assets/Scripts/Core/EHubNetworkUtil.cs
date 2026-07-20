using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Laps.Core
{
    /// <summary>Utilitaires réseau eHub : résolution IP LAN fiable et comparaison d'adresses.</summary>
    public static class EHubNetworkUtil
    {
        /// <summary>
        /// IP IPv4 la plus probable pour le LAN (Wi-Fi / Ethernet).
        /// Évite les mauvaises interfaces (VPN, virtualbox, etc.).
        /// </summary>
        public static string ResolveBestLocalIpv4()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint ep)
                        return ep.Address.ToString();
                }
            }
            catch { /* pas de route internet — fallback ci-dessous */ }

            var candidates = CollectLanCandidates();
            if (candidates.Count > 0)
                return candidates[0];

            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch { /* ignore */ }

            return "127.0.0.1";
        }

        /// <summary>Toutes les IPv4 LAN utilisables (affichage hôte).</summary>
        public static IReadOnlyList<string> CollectLanCandidates()
        {
            var scored = new List<(int score, string ip)>();

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    int typeScore = ni.NetworkInterfaceType switch
                    {
                        NetworkInterfaceType.Wireless80211 => 100,
                        NetworkInterfaceType.Ethernet => 90,
                        _ => 10
                    };

                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = ua.Address.ToString();
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        if (ip.StartsWith("169.254.")) continue;

                        int prefixScore = ip.StartsWith("192.168.") ? 30
                            : ip.StartsWith("10.") ? 20
                            : ip.StartsWith("172.") ? 15 : 5;

                        scored.Add((typeScore + prefixScore, ip));
                    }
                }
            }
            catch { /* ignore */ }

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in scored)
            {
                if (seen.Add(entry.ip))
                    result.Add(entry.ip);
            }
            return result;
        }

        public static string NormalizeIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return "";
            ip = ip.Trim();

            if (ip.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
                ip = ip.Substring(7);

            return ip;
        }

        public static bool IpEquals(string a, string b) =>
            string.Equals(NormalizeIp(a), NormalizeIp(b), StringComparison.OrdinalIgnoreCase);

        public static bool IsLoopbackOrSelf(string localIp, string otherIp) =>
            IpEquals(localIp, otherIp) || IpEquals(otherIp, "127.0.0.1");
    }
}
