using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Référence de style pour les feux d'artifice LED (le rendu est 2D sur la grille, pas en 3D).
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class ProceduralFirework : MonoBehaviour
    {
        [Header("Apparence du feu d'artifice")]
        [Tooltip("Style utilisé quand ce prefab est assigné à un SFX (dessiné sur les LEDs).")]
        public FireworkStyle style = FireworkStyle.ClassicNova;

        [Header("Couleurs")]
        [Tooltip("Si coché, le feu d'artifice utilisera les couleurs par défaut (aléatoire de la palette).")]
        public bool useRandomColor = true;

        [Tooltip("Si coché, l'explosion sera multicolore (effet arc-en-ciel). Ne s'applique que si 'useRandomColor' est coché.")]
        public bool multicolor = false;

        [Tooltip("Couleur personnalisée du feu d'artifice si 'useRandomColor' est décoché.")]
        public Color fireworkColor = Color.cyan;

        // Le prefab sert de configuration ; le rendu réel est géré par LedFireworks (2D).
        void Awake() { }
    }
}
