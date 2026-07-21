namespace Laps.Authoring
{
    /// <summary>État pause global (Espace) partagé entre audio, lyres et timeline.</summary>
    public static class GlobalPause
    {
        public static bool IsPaused { get; private set; }

        public static void SetPaused(bool paused) => IsPaused = paused;
    }
}
