using UnityEngine;

namespace Laps.Core
{
    public interface ILyreStateProvider
    {
        LyreState[] GetLyreStates();
    }

    /// <summary>
    /// Combine un provider LEDs (effets) avec un provider lyres/projecteurs.
    /// Permet de piloter des appareils DMX via UI sans casser les effets LED.
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
            var lyres = _lyreProvider?.GetLyreStates();
            return lyres ?? _baseProvider?.GetLyreStates();
        }
    }
}

