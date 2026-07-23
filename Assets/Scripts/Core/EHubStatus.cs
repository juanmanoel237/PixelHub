namespace Laps.Core
{
    /// <summary>
    /// État eHub global, lisible depuis n'importe quel assembly sans couplage fort.
    /// Permet de savoir si on est Connecté, notre Rôle (Client/Host), et l'IP de l'Hôte.
    /// </summary>
    public static class EHubStatus
    {
        public static bool Enabled;
        public static bool Connected;
        public static EHubRole Role;
        public static string HostIp = "";
        public static int TotalPostes = 1;
        public static bool HostDetectedOnLan;

        public static void Update(bool enabled, bool connected, EHubRole role, string hostIp, int totalPostes)
        {
            Enabled = enabled;
            Connected = connected;
            Role = role;
            HostIp = hostIp ?? "";
            TotalPostes = totalPostes;
        }

        /// <summary>
        /// Seul l'hôte (ou un poste solo sans autre hôte sur le LAN) envoie Art-Net.
        /// Les clients gardent l'aperçu local mais ne pilotent pas le hardware.
        /// </summary>
        public static bool ShouldOutputToHardware
        {
            get
            {
                if (Role == EHubRole.Client) return false;
                if (Enabled && Role == EHubRole.Solo && HostDetectedOnLan) return false;
                return true;
            }
        }
    }
}
