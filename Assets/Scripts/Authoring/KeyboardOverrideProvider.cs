using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Intercepteur (Middleware) qui surcharge le signal visuel normal
    /// avec un flash blanc stroboscopique lorsque la touche Entrée (Return) est pressée (Carte 11).
    /// </summary>
    public class KeyboardOverrideProvider : MonoBehaviour, IStateProvider
    {
        [Header("Source Principale")]
        [Tooltip("Glissez ici le composant normal (ex: FakerStateProvider ou GPUCapture)")]
        public MonoBehaviour baseProviderComponent;
        
        private IStateProvider _baseProvider;
        private Color32[] _overrideState;
        
        // Couleur de l'effet "flash" (blanc opaque par défaut)
        private readonly Color32 _flashColor = new Color32(255, 255, 255, 255);

        void Awake()
        {
            // On vérifie que le composant glissé dans l'inspecteur Unity implémente bien l'interface IStateProvider.
            // On utilise "is" pour vérifier le type et faire le cast en une seule ligne.
            if (baseProviderComponent is IStateProvider provider)
            {
                _baseProvider = provider;
            }
            else if (baseProviderComponent != null)
            {
                Debug.LogError("[KeyboardOverride] Le composant glissé n'implémente pas IStateProvider !");
            }
        }

        public Color32[] GetState()
        {
            // Sécurité : si aucune source visuelle n'est connectée, on s'arrête.
            if (_baseProvider == null) return null;

            // 1. On demande l'image normale à la source d'origine (la caméra, le faker, etc.)
            Color32[] normalState = _baseProvider.GetState();
            if (normalState == null) return null;

            // 2. Interception : On regarde si la touche Entrée (Return) est enfoncée sur le clavier.
            if (Input.GetKey(KeyCode.Return))
            {
                // Optimisation : On alloue le tableau "flash" une seule fois pour éviter de saturer la RAM.
                if (_overrideState == null || _overrideState.Length != normalState.Length)
                {
                    _overrideState = new Color32[normalState.Length];
                }

                // On remplit notre tableau avec du blanc pur.
                // Cela écrase complètement les effets visuels de la Timeline.
                for (int i = 0; i < _overrideState.Length; i++)
                {
                    _overrideState[i] = _flashColor;
                }
                
                // On retourne notre faux tableau blanc à la place du vrai tableau.
                // Le RoutingManager réseau recevra ça et allumera tous les LEDs en blanc.
                return _overrideState; 
            }

            // 3. Mode normal : si la touche n'est pas pressée, le script est complètement "transparent".
            // Il laisse passer le tableau d'origine sans le modifier.
            return normalState;
        }

        public LyreState[] GetLyreStates()
        {
            // Pour l'instant on laisse passer les données des projecteurs DMX sans les intercepter.
            return _baseProvider?.GetLyreStates();
        }
    }
}
