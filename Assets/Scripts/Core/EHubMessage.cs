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
        public int intArg2;
        public float floatArg;
        public float floatArg2;
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
        public const string StateSync   = "StateSync";
        public const string DeviceAction = "DeviceAction";
        public const string LyreControl = "LyreControl";
        public const string VolumeSet = "VolumeSet";
        public const string TimelineControl = "TimelineControl";
        public const string DanmarkLetter = "DanmarkLetter";
    }

    /// <summary>Sous-actions LyreControl (intArg).</summary>
    public static class EHubLyreAction
    {
        public const int SelectHead = 1;
        public const int PresetColor = 2;
        public const int PanTiltDelta = 3;
        public const int NightclubToggle = 4;
        public const int SetAllPreset = 5;
        public const int SetRgb = 6;
        public const int BeatColorCycle = 7;
        public const int SyncSnapshot = 8;
    }

    public static class EHubTimelineAction
    {
        public const int Play = 0;
        public const int Pause = 1;
        public const int Stop = 2;
    }

    public static class EHubDanmarkAction
    {
        public const string Complete = "__COMPLETE__";
        public const string Hide = "__HIDE__";
    }

    /// <summary>Codes pour DeviceAction.intArg</summary>
    public static class EHubDeviceAction
    {
        public const int MovingHead1 = 1;
        public const int MovingHead2 = 2;
        public const int MovingHead3 = 3;
        public const int MovingHead4 = 4;
        public const int StaticProjector = 5;
        public const int BlackOutLyres = 6;
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
