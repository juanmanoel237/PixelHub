using System;
using System.Collections.Generic;

namespace Laps.Core
{
    public enum RoutingDebugMode
    {
        Idle,
        Pixel,
        Entity
    }

    [Serializable]
    public struct DmxUniverseSummary
    {
        public int key;
        public int controllerIndex;
        public int universe;
        public string controllerIp;
        public int activeChannelCount;
        public int firstActiveChannel;
    }

    /// <summary>Instantané DMX pour le panneau debug routage (F7).</summary>
    public class RoutingDebugSnapshot
    {
        public RoutingDebugMode Mode = RoutingDebugMode.Idle;
        public bool HardwareOutputEnabled;
        public float PacketsPerSecond;
        public int PacketsSentTotal;
        public float RoutingFps;
        public int ActiveUniverseCount;
        public int EntityReceived;
        public int EntityMapped;
        public int EntityUnmapped;
        public int[] UnmappedEntityIds = Array.Empty<int>();
        public LEDAddress FirstPixelAddress;
        public bool HasFirstPixelAddress;
        public List<DmxUniverseSummary> Universes = new List<DmxUniverseSummary>(64);
        public Dictionary<int, byte[]> DmxBuffers = new Dictionary<int, byte[]>(64);
    }
}
