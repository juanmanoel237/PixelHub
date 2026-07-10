using System.Collections.Generic;
using UnityEngine;

namespace Laps.Core
{
    public enum FireworkStyle
    {
        ClassicNova,
        SparkleFountain
    }

    /// <summary>
    /// Feux d'artifice 2D dessinés sur la grille LED (mur + aperçu), pas en 3D dans la scène.
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
        }

        private struct Burst
        {
            public List<Particle> Particles;
        }

        private static readonly List<Burst> _bursts = new List<Burst>();

        public static void Trigger(FireworkStyle style = FireworkStyle.ClassicNova)
        {
            float cx = Random.Range(0.22f, 0.78f);
            float cy = Random.Range(0.22f, 0.78f);
            Color baseColor = Random.ColorHSV(0f, 1f, 0.85f, 1f, 0.85f, 1f);

            var particles = new List<Particle>(140);
            int count = style == FireworkStyle.SparkleFountain ? 100 : 130;

            for (int i = 0; i < count; i++)
            {
                float speed = Random.Range(0.35f, 0.95f);
                float vx, vy;

                if (style == FireworkStyle.SparkleFountain)
                {
                    float angle = Random.Range(-35f, 35f) * Mathf.Deg2Rad;
                    vx = Mathf.Sin(angle) * speed * 0.5f;
                    vy = -Mathf.Cos(angle) * speed;
                }
                else
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    vx = Mathf.Cos(angle) * speed;
                    vy = Mathf.Sin(angle) * speed;
                }

                particles.Add(new Particle
                {
                    X = cx,
                    Y = cy,
                    Vx = vx,
                    Vy = vy,
                    Age = 0f,
                    Lifetime = Random.Range(0.7f, 1.6f),
                    Color = (Color32)Color.Lerp(baseColor, Color.white, Random.Range(0f, 0.45f))
                });
            }

            _bursts.Add(new Burst { Particles = particles });
        }

        public static void Tick(float deltaTime)
        {
            if (_bursts.Count == 0) return;

            float dt = Mathf.Max(deltaTime, 0.0001f);
            const float gravity = 0.55f;

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
                    p.Vy += gravity * dt;
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

        /// <summary>Superpose les feux d'artifice actifs sur le buffer LED.</summary>
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

                    PaintGlow(buffer, width, height, p.X, p.Y, p.Color, intensity);
                }
            }
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
