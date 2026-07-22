using System;

namespace Laps.Core
{
    /// <summary>Permet à l'UI Authoring d'ouvrir les panneaux routeur (Bootstrap) sans référence circulaire.</summary>
    public static class RouterPanelBus
    {
        public static event Action ToggleConfigRequested;
        public static event Action ToggleDebugRequested;

        public static void RequestToggleConfig() => ToggleConfigRequested?.Invoke();
        public static void RequestToggleDebug() => ToggleDebugRequested?.Invoke();
    }
}
