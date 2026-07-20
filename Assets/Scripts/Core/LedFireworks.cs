using System.Collections.Generic;
using UnityEngine;

namespace Laps.Core
{
    public enum FireworkStyle
    {
        ClassicNova,
        SparkleFountain,
        FlameThrowerLeft,
        FlameThrowerRight,
        LaserSweep,
        Shockwave
    }

    /// <summary>
    /// Feux d'artifice et effets 2D dessinés sur la grille LED (mur + aperçu).
    /// </summary>
    public static class LedFireworks
    {
        private struct Particle
        {
            public float X, Y;
            public float Vx, Vy;
            public float Age;
            public float Lifetime;
            public Color32 Color;
            public float Gravity;
            public bool IsFlame;
        }

        private struct Burst
        {
            public List<Particle> Particles;
        }

        private static readonly List<Burst> _bursts = new List<Burst>();

        private static readonly Color[] Palette = new Color[]
        {
            new Color(1f, 0.1f, 0.1f),   // Rouge vif
            new Color(0.1f, 1f, 0.1f),   // Vert vif
            new Color(0.1f, 0.5f, 1f),   // Bleu électrique
            new Color(1f, 0.85f, 0f),    // Or / Jaune
            new Color(1f, 0.45f, 0f),    // Orange
            new Color(0.85f, 0.1f, 1f),  // Violet/Magenta vif
            new Color(0f, 0.95f, 0.95f)  // Cyan
        };

        public static void Trigger(FireworkStyle style = FireworkStyle.ClassicNova, Color? customColor = null, bool forceMulticolor = false)
        {
            float cx = 0.5f;
            float cy = 0.5f;
            Color baseColor = Color.white;
            bool isMulticolor = forceMulticolor;
            bool isFlame = (style == FireworkStyle.FlameThrowerLeft || style == FireworkStyle.FlameThrowerRight);
            bool isLaser = (style == FireworkStyle.LaserSweep);
            bool isShockwave = (style == FireworkStyle.Shockwave);

            if (isFlame)
            {
                if (style == FireworkStyle.FlameThrowerLeft)
                {
                    cx = 0.08f; // En bas à gauche
                    cy = 0.98f;
                }
                else
                {
                    cx = 0.92f; // En bas à droite
                    cy = 0.98f;
                }
            }
            else if (isLaser)
            {
                cx = 0.01f;
                cy = 0.5f;
            }
            else if (isShockwave)
            {
                cx = 0.5f;
                cy = 0.5f;
            }
            else
            {
                cx = Random.Range(0.22f, 0.78f);
                cy = Random.Range(0.22f, 0.78f);
            }

            if (!isFlame && !isLaser && !isShockwave)
            {
                if (customColor.HasValue)
                {
                    baseColor = customColor.Value;
                }
                else
                {
                    // Par défaut, s'il n'y a pas de couleur personnalisée,
                    // on a 20% de chance d'être multicolore, sinon on prend une couleur de la palette
                    if (Random.value < 0.20f)
                    {
                        isMulticolor = true;
                    }
                    else
                    {
                        baseColor = Palette[Random.Range(0, Palette.Length)];
                    }
                }
            }

            int count = 130;
            if (style == FireworkStyle.SparkleFountain)
                count = 100;
            else if (isFlame)
                count = 70; // Particules pour la flamme
            else if (isLaser)
                count = 85; // Résolution verticale de la ligne de laser
            else if (isShockwave)
                count = 100; // Densité du cercle

            var particles = new List<Particle>(count);

            // Choisir une couleur de laser spécifique si c'est un laser
            Color laserColor = Color.green;
            if (isLaser)
            {
                Color[] laserColors = new Color[]
                {
                    new Color(0.1f, 1f, 0.2f),   // Vert fluo
                    new Color(0f, 0.95f, 1f),    // Cyan laser
                    new Color(1f, 0.05f, 0.85f), // Magenta vif
                    new Color(1f, 0.95f, 0f)     // Jaune laser
                };
                laserColor = laserColors[Random.Range(0, laserColors.Length)];
            }

            for (int i = 0; i < count; i++)
            {
                float speed = Random.Range(0.35f, 0.95f);
                float vx = 0f;
                float vy = 0f;
                float gravityVal = 0.55f;
                float lifetime = Random.Range(0.7f, 1.6f);
                float px = cx;
                float py = cy;
                Color pColor = baseColor;

                if (isFlame)
                {
                    // Projection vers le haut-droite ou haut-gauche
                    float baseAngle = (style == FireworkStyle.FlameThrowerLeft) ? 25f : -25f;
                    float angle = (baseAngle + Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
                    float flameSpeed = speed * 1.5f;

                    vx = Mathf.Sin(angle) * flameSpeed;
                    vy = -Mathf.Cos(angle) * flameSpeed;
                    gravityVal = -0.65f; // Flottabilité de la flamme (gravité négative pour qu'elle monte)
                    lifetime = Random.Range(0.5f, 0.9f); // Plus courte vie
                }
                else if (isLaser)
                {
                    // Ligne verticale balayant l'écran horizontalement
                    float yNorm = (float)i / (count - 1);
                    px = 0.01f;
                    py = yNorm;
                    vx = 1.35f; // Vitesse de balayage rapide de gauche à droite
                    vy = 0f;
                    gravityVal = 0f;
                    lifetime = 0.75f; // Finit un peu après avoir traversé la grille (0.75 * 1.35 = 1.01)
                    pColor = laserColor;
                }
                else if (isShockwave)
                {
                    // Onde de choc circulaire
                    float angle = (i * Mathf.PI * 2f) / count;
                    float shockwaveSpeed = 0.9f;
                    vx = Mathf.Cos(angle) * shockwaveSpeed;
                    vy = Mathf.Sin(angle) * shockwaveSpeed;
                    gravityVal = 0f;
                    lifetime = 0.6f; // S'estompe quand elle atteint les bords
                    
                    // Couleur néon cyan qui tire vers le bleu électrique
                    pColor = Color.Lerp(new Color(0.1f, 0.7f, 1f), new Color(0f, 0.2f, 1f), Random.value);
                }
                else if (style == FireworkStyle.SparkleFountain)
                {
                    float angle = Random.Range(-35f, 35f) * Mathf.Deg2Rad;
                    vx = Mathf.Sin(angle) * speed * 0.5f;
                    vy = -Mathf.Cos(angle) * speed;
                    pColor = isMulticolor ? Palette[Random.Range(0, Palette.Length)] : baseColor;
                }
                else
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    vx = Mathf.Cos(angle) * speed;
                    vy = Mathf.Sin(angle) * speed;
                    pColor = isMulticolor ? Palette[Random.Range(0, Palette.Length)] : baseColor;
                }

                particles.Add(new Particle
                {
                    X = px,
                    Y = py,
                    Vx = vx,
                    Vy = vy,
                    Age = 0f,
                    Lifetime = lifetime,
                    Color = (Color32)Color.Lerp(pColor, Color.white, isLaser || isShockwave ? Random.Range(0f, 0.15f) : Random.Range(0f, 0.45f)), // Moins de blanc pour les effets purs
                    Gravity = gravityVal,
                    IsFlame = isFlame
                });
            }

            _bursts.Add(new Burst { Particles = particles });
        }

        public static void Tick(float deltaTime)
        {
            if (_bursts.Count == 0) return;

            float dt = Mathf.Max(deltaTime, 0.0001f);

            for (int b = _bursts.Count - 1; b >= 0; b--)
            {
                var burst = _bursts[b];
                bool anyAlive = false;

                for (int i = 0; i < burst.Particles.Count; i++)
                {
                    var p = burst.Particles[i];
                    p.Age += dt;
                    if (p.Age >= p.Lifetime) continue;

                    anyAlive = true;
                    p.Vy += p.Gravity * dt;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    burst.Particles[i] = p;
                }

                if (!anyAlive)
                    _bursts.RemoveAt(b);
                else
                    _bursts[b] = burst;
            }
        }

        /// <summary>Superpose les effets actifs sur le buffer LED.</summary>
        public static void CompositeOnto(Color32[] buffer, int width, int height)
        {
            if (buffer == null || width <= 0 || height <= 0 || _bursts.Count == 0) return;

            foreach (var burst in _bursts)
            {
                foreach (var p in burst.Particles)
                {
                    if (p.Age >= p.Lifetime) continue;

                    float lifeT = 1f - p.Age / p.Lifetime;
                    float fade = lifeT * lifeT;
                    float intensity = fade * 1.2f;

                    Color32 col = p.Color;
                    if (p.IsFlame)
                    {
                        col = GetFlameColor(lifeT);
                    }

                    PaintGlow(buffer, width, height, p.X, p.Y, col, intensity);
                }
            }
        }

        private static Color32 GetFlameColor(float lifeT)
        {
            Color c;
            if (lifeT > 0.65f)
            {
                float t = (lifeT - 0.65f) / 0.35f;
                c = Color.Lerp(new Color(1f, 0.5f, 0f), new Color(1f, 1f, 0.7f), t); // Fades de jaune-blanc à orange
            }
            else if (lifeT > 0.3f)
            {
                float t = (lifeT - 0.3f) / 0.35f;
                c = Color.Lerp(new Color(0.9f, 0.1f, 0f), new Color(1f, 0.5f, 0f), t); // Fades d'orange à rouge
            }
            else
            {
                float t = lifeT / 0.3f;
                c = Color.Lerp(new Color(0.25f, 0.02f, 0f), new Color(0.9f, 0.1f, 0f), t); // Fades de rouge à rouge sombre
            }
            return (Color32)c;
        }

        private static void PaintGlow(Color32[] buffer, int w, int h, float nx, float ny, Color32 col, float intensity)
        {
            int cx = Mathf.RoundToInt(nx * (w - 1));
            int cy = Mathf.RoundToInt(ny * (h - 1));

            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= w || y < 0 || y >= h) continue;

                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.Clamp01(1f - dist / 2.5f);
                    float a = intensity * falloff;
                    if (a <= 0.01f) continue;

                    int idx = y * w + x;
                    buffer[idx] = AdditiveBlend(buffer[idx], col, a);
                }
            }
        }

        private static Color32 AdditiveBlend(Color32 dst, Color32 src, float amount)
        {
            float a = amount * (src.a / 255f);
            return new Color32(
                (byte)Mathf.Min(255, dst.r + src.r * a),
                (byte)Mathf.Min(255, dst.g + src.g * a),
                (byte)Mathf.Min(255, dst.b + src.b * a),
                255);
        }
    }
}
