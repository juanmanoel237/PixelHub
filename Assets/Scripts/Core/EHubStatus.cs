namespace Laps.Core
{
    /// <summary>État eHub lisible depuis n'importe quel assembly (ex. LedPreviewOverlay).</summary>
    public static class EHubStatus
    {
        public static bool Enabled;
        public static bool Connected;
        public static EHubRole Role;
        public static string HostIp = "";
        public static int TotalPostes = 1;

        public static void Update(bool enabled, bool connected, EHubRole role, string hostIp, int totalPostes)
        {
            Enabled = enabled;
            Connected = connected;
            Role = role;
            HostIp = hostIp ?? "";
            TotalPostes = totalPostes;
        }
    }
}
