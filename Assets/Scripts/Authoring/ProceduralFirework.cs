using UnityEngine;

namespace Laps.Authoring
{
    /// <summary>
    /// Différents styles d'explosion pour les feux d'artifice.
    /// </summary>
    public enum FireworkStyle
    {
        ClassicNova,      // Explosion sphérique classique
        SparkleFountain   // Gerbe d'étincelles vers le haut avec forte gravité
    }

    /// <summary>
    /// Génère procéduralement un feu d'artifice 3D à l'aide du composant ParticleSystem.
    /// Élimine le besoin d'assets externes.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class ProceduralFirework : MonoBehaviour
    {
        [Header("Apparence du feu d'artifice")]
        [Tooltip("Choisissez le style d'explosion. Chaque type créera un motif différent.")]
        public FireworkStyle style = FireworkStyle.ClassicNova;

        void Awake()
        {
            ParticleSystem ps = GetComponent<ParticleSystem>();
            var main = ps.main;
            var emission = ps.emission;
            var shape = ps.shape;
            var colorOverLifetime = ps.colorOverLifetime;

            // ── Paramètres Communs ─────────────────────────────────
            main.playOnAwake = true;
            main.loop = false;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            
            // Émission : une seule grosse explosion
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 150) });

            // Couleur de départ aléatoire (vive et saturée)
            Color randomColor = Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);
            main.startColor = randomColor;

            // Disparition en fondu à la fin de la vie
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = grad;

            // Un rendu visuel "additif" permet de rendre les étincelles lumineuses
            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            // On s'assure qu'il y a un Material par défaut assigné automatiquement par Unity (Default-Particle)

            // ── Application des Styles ─────────────────────────────
            if (style == FireworkStyle.ClassicNova)
            {
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
                main.gravityModifier = 0.2f;

                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.1f;
            }
            else if (style == FireworkStyle.SparkleFountain)
            {
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
                main.gravityModifier = 1.2f; // Retombe très vite

                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 0.1f;
                
                // Oriente le cône vers le haut pour faire une fontaine
                transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            }
        }
    }
}
