using UnityEngine;

namespace Laps.Core
{
    public interface ILyreStateProvider
    {
        LyreState[] GetLyreStates();
    }

    /// <summary>
    /// Combine un provider LEDs (effets) avec un provider lyres/projecteurs.
    /// La timeline peut piloter une lyre ; le panneau manuel garde le contrôle sinon.
    /// </summary>
    public class CompositeStateProvider : IStateProvider
    {
        private readonly IStateProvider _baseProvider;
        private readonly ILyreStateProvider _lyreProvider;

        public CompositeStateProvider(IStateProvider baseProvider, ILyreStateProvider lyreProvider)
        {
            _baseProvider = baseProvider;
            _lyreProvider = lyreProvider;
        }

        public Color32[] GetState() => _baseProvider?.GetState();

        public LyreState[] GetLyreStates()
        {
            var panel = _lyreProvider?.GetLyreStates();
            var timeline = _baseProvider?.GetLyreStates();
            return MergeLyres(timeline, panel);
        }

        /// <summary>Timeline prioritaire si dimmer &gt; 0, sinon panneau manuel.</summary>
        private static LyreState[] MergeLyres(LyreState[] timeline, LyreState[] panel)
        {
            if (panel == null || panel.Length == 0) return timeline;
            if (timeline == null || timeline.Length == 0) return panel;

            var merged = new LyreState[panel.Length];
            for (int i = 0; i < panel.Length; i++)
                merged[i] = panel[i];

            foreach (var t in timeline)
            {
                if (t == null || t.dimmer <= 0) continue;

                int idx = FindLyreIndex(merged, t.lyreName);
                if (idx >= 0)
                    merged[idx] = t;
            }

            return merged;
        }

        private static int FindLyreIndex(LyreState[] states, string name)
        {
            if (states == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] != null && states[i].lyreName == name)
                    return i;
            }
            return -1;
        }
    }
}
