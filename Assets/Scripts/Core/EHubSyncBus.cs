using System;

namespace Laps.Core
{
    /// <summary>
    /// Bus léger pour publier des actions eHub sans dépendance circulaire entre assemblies.
    /// EHubNetworkBridge enregistre l'envoi UDP ; les autres modules appellent PublishLocal.
    /// </summary>
    public static class EHubSyncBus
    {
        private static Action<EHubMessage> _sendHandler;

        public static void RegisterSendHandler(Action<EHubMessage> handler) => _sendHandler = handler;

        public static void PublishLocal(EHubMessage msg) => _sendHandler?.Invoke(msg);
    }
}
