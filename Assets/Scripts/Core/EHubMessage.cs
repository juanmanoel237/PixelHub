using System;

namespace Laps.Core
{
    /// <summary>
    /// Message eHub échangé en UDP entre machines PixelHub (sync multi-postes).
    /// Sérialisable via JsonUtility.
    /// </summary>
    [Serializable]
    public class EHubMessage
    {
        public string type;
        public string sessionId;
        public string senderId;
        public int intArg;
        public float floatArg;
        public string stringArg;
    }

    public static class EHubMessageTypes
    {
        public const string Hello       = "Hello";
        public const string HelloAck    = "HelloAck";
        public const string SwitchMode  = "SwitchMode";
        public const string DebugColor  = "DebugColor";
        public const string SfxTrigger  = "SfxTrigger";
        public const string PauseState  = "PauseState";
    }

    /// <summary>Codes pour DebugColor.intArg</summary>
    public static class EHubDebugColor
    {
        public const int Red      = 0;
        public const int Green    = 1;
        public const int Blue     = 2;
        public const int BlackOut = 3;
        public const int FirstLed = 4;
    }
}
