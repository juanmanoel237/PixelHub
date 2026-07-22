using System.Collections.Generic;
using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Overlay de lettres volantes sur la grille LED.
    /// Chaque lettre traverse l'écran avec une trajectoire unique (horizontal, vertical, diagonal).
    /// Utilisé pour l'effet DANMARK (Shift+D/A/N/E/M/R/K).
    /// Architecture identique à LedFireworks : classe statique, Tick + CompositeOnto.
    /// </summary>
    public static class LedTextOverlay
    {
        /// <summary>Direction de vol d'une lettre sur la grille.</summary>
        public enum FlyDirection
        {
            LeftToRight,
            RightToLeft,
            TopToBottom,
            BottomToTop,
            DiagTopLeftToBottomRight,
            DiagBottomLeftToTopRight,
            DiagTopRightToBottomLeft,
        }

        private struct FlyingLetter
        {
            public char Character;
            public float StartX, StartY;
            public float EndX, EndY;
            public float Age;
            public float Lifetime;
            public Color32 Color;
            public int Scale;
            public bool IsPulse;
        }

        private static readonly List<FlyingLetter> _letters = new List<FlyingLetter>();

        public static void ClearAll()
        {
            _letters.Clear();
            _danmarkComplete = false;
        }

        // État "DANMARK complet" — le mot reste affiché au centre
        private static bool _danmarkComplete;
        private static float _danmarkCompleteTime;
        private static float _danmarkDisplayDuration = 12f;

        // Couleurs par lettre (palette inspirée du drapeau danois : rouge/blanc + néon)
        private static readonly Color[] LetterColors = new Color[]
        {
            new Color(1f, 0.15f, 0.15f),   // D — Rouge danois
            new Color(1f, 1f, 0.95f),       // A — Blanc chaud
            new Color(0.1f, 0.85f, 1f),     // N — Cyan néon
            new Color(1f, 0.85f, 0f),       // E — Or
            new Color(0.85f, 0.1f, 1f),     // M — Violet néon
            new Color(1f, 0.15f, 0.15f),    // R — Rouge danois (2e)
            new Color(0.1f, 1f, 0.45f),     // K — Vert néon
        };

        // Trajectoire prédéfinie par lettre (pour celles qui défilent)
        private static readonly FlyDirection[] LetterDirections = new FlyDirection[]
        {
            FlyDirection.LeftToRight,                   // D — défile
            FlyDirection.DiagTopLeftToBottomRight,       // A — (non utilisé, pulse)
            FlyDirection.TopToBottom,                    // N — défile
            FlyDirection.RightToLeft,                    // E — (non utilisé, pulse)
            FlyDirection.DiagBottomLeftToTopRight,       // M — défile
            FlyDirection.DiagTopRightToBottomLeft,       // R — (non utilisé, pulse)
            FlyDirection.BottomToTop,                    // K — défile
        };

        // true = pulse au centre, false = défile à travers l'écran
        private static readonly bool[] LetterIsPulse = new bool[]
        {
            false,  // D — défile
            true,   // A — pulse
            false,  // N — défile
            true,   // E — pulse
            false,  // M — défile
            true,   // R — pulse
            false,  // K — défile
        };

        private const float FLY_DURATION = 1.8f;
        private const float PULSE_DURATION = 2.0f;
        private const int LETTER_SCALE = 4;

        /// <summary>Lance une lettre (pulse au centre ou défilement selon la lettre).</summary>
        public static void SpawnLetter(char ch)
        {
            ch = char.ToUpperInvariant(ch);

            GetLetterPreset(ch, out Color color, out FlyDirection dir, out bool isPulse);

            float sx, sy, ex, ey;
            float lifetime;

            if (isPulse)
            {
                sx = 0.5f; sy = 0.5f; ex = 0.5f; ey = 0.5f;
                lifetime = PULSE_DURATION;
            }
            else
            {
                GetStartEnd(dir, out sx, out sy, out ex, out ey);
                lifetime = FLY_DURATION;
            }

            _letters.Add(new FlyingLetter
            {
                Character = ch,
                StartX = sx, StartY = sy,
                EndX = ex, EndY = ey,
                Age = 0f,
                Lifetime = lifetime,
                Color = (Color32)color,
                Scale = LETTER_SCALE,
                IsPulse = isPulse
            });
        }

        /// <summary>Signale que le mot complet doit s'afficher au centre.</summary>
        public static void SetDanmarkComplete(bool complete)
        {
            if (complete && !_danmarkComplete)
                _danmarkCompleteTime = Time.time;
            _danmarkComplete = complete;
        }

        public static bool IsDanmarkComplete => _danmarkComplete;

        /// <summary>Met à jour les lettres (appelé depuis le main thread).</summary>
        public static void Tick(float deltaTime)
        {
            float dt = Mathf.Max(deltaTime, 0.0001f);

            for (int i = _letters.Count - 1; i >= 0; i--)
            {
                var l = _letters[i];
                l.Age += dt;
                if (l.Age >= l.Lifetime)
                {
                    _letters.RemoveAt(i);
                    continue;
                }
                _letters[i] = l;
            }

            // Auto-reset du mot complet après la durée d'affichage
            if (_danmarkComplete && (Time.time - _danmarkCompleteTime) > _danmarkDisplayDuration)
                _danmarkComplete = false;
        }

        /// <summary>Superpose les lettres actives sur le buffer LED.</summary>
        public static void CompositeOnto(Color32[] buffer, int width, int height)
        {
            if (buffer == null || width <= 0 || height <= 0) return;

            // 1. Lettres (pulse au centre ou défilement)
            foreach (var l in _letters)
            {
                if (l.Age >= l.Lifetime) continue;

                float t = l.Age / l.Lifetime;
                float alpha;
                int px, py;
                int glyphW = 5 * l.Scale;
                int glyphH = 7 * l.Scale;

                if (l.IsPulse)
                {
                    // Pulse : fade in doux → maintien → fade out doux
                    if (t < 0.3f)
                        alpha = t / 0.3f;
                    else if (t < 0.7f)
                        alpha = 1f;
                    else
                        alpha = (1f - t) / 0.3f;
                    alpha = Mathf.Clamp01(alpha);
                    alpha = alpha * alpha * (3f - 2f * alpha); // courbe douce

                    px = (width - glyphW) / 2;
                    py = (height - glyphH) / 2;
                }
                else
                {
                    // Fly : traverse l'écran avec fade in/out rapide
                    if (t < 0.12f)
                        alpha = t / 0.12f;
                    else if (t > 0.85f)
                        alpha = (1f - t) / 0.15f;
                    else
                        alpha = 1f;
                    alpha = Mathf.Clamp01(alpha);

                    float nx = Mathf.Lerp(l.StartX, l.EndX, t);
                    float ny = Mathf.Lerp(l.StartY, l.EndY, t);
                    px = Mathf.RoundToInt(nx * width) - glyphW / 2;
                    py = Mathf.RoundToInt(ny * height) - glyphH / 2;
                }

                Color col = l.Color;
                col.a = alpha;
                Color32 fg = (Color32)(col * alpha);

                RenderCharAt(buffer, width, height, l.Character, l.Scale, fg, px, py);
            }

            // 2. Mot "DANEMARK" complet au centre
            if (_danmarkComplete)
            {
                float elapsed = Time.time - _danmarkCompleteTime;
                float fadeIn = Mathf.Clamp01(elapsed / 0.5f);
                float shimmer = 0.85f + 0.15f * Mathf.Sin(Time.time * 4f);

                // Couleur dorée qui brille
                Color red = new Color(1f, 0.1f, 0.1f) * shimmer * fadeIn;

                int scale = Mathf.Max(1, Mathf.Min(width / (8 * 6), height / (7 * 4)));
                int glyphH = 7 * scale;
                int originY = (height - glyphH) / 2;

                RenderTextAtY(buffer, width, height, "danemark", scale, (Color32)red, originY);
            }
        }

        /// <summary>Rend un caractère à une position pixel donnée.</summary>
        private static void RenderCharAt(Color32[] buffer, int w, int h, char ch, int scale, Color32 fg, int ox, int oy)
        {
            ch = char.ToLowerInvariant(ch);

            // Accès à la font via EffectLibrary.RenderSingleCharCentered n'est pas possible
            // car il centre toujours. On refait le rendu ici avec la même font.
            byte[] glyph = GetGlyph(ch);
            if (glyph == null) return;

            for (int row = 0; row < 7; row++)
            {
                byte bits = glyph[row];
                for (int col = 0; col < 5; col++)
                {
                    if ((bits & (1 << (4 - col))) == 0) continue;

                    for (int sy = 0; sy < scale; sy++)
                    {
                        int py = oy + row * scale + sy;
                        if (py < 0 || py >= h) continue;

                        for (int sx = 0; sx < scale; sx++)
                        {
                            int px = ox + col * scale + sx;
                            if (px < 0 || px >= w) continue;

                            int idx = py * w + px;
                            if (idx < 0 || idx >= buffer.Length) continue;
                            buffer[idx] = AdditiveBlend(buffer[idx], fg);
                        }
                    }
                }
            }
        }

        /// <summary>Rend une ligne de texte centrée horizontalement à une hauteur Y donnée.</summary>
        private static void RenderTextAtY(Color32[] buffer, int w, int h, string text, int scale, Color32 fg, int originY)
        {
            if (string.IsNullOrEmpty(text)) return;

            int glyphW = 5 * scale;
            int gap = 1 * scale;
            int totalW = text.Length * (glyphW + gap) - gap;
            int originX = Mathf.Max(0, (w - totalW) / 2);

            for (int i = 0; i < text.Length; i++)
            {
                int cx = originX + i * (glyphW + gap);
                RenderCharAt(buffer, w, h, text[i], scale, fg, cx, originY);
            }
        }

        // ── Font bitmap 5×7 (copie de EffectLibrary pour accès statique) ──

        private static readonly Dictionary<char, byte[]> Font5x7
            = new Dictionary<char, byte[]>
        {
            {' ', new byte[]{0b00000,0b00000,0b00000,0b00000,0b00000,0b00000,0b00000}},
            {'a', new byte[]{0b01110,0b10001,0b10001,0b11111,0b10001,0b10001,0b10001}},
            {'b', new byte[]{0b11110,0b10001,0b10001,0b11110,0b10001,0b10001,0b11110}},
            {'c', new byte[]{0b01110,0b10001,0b10000,0b10000,0b10000,0b10001,0b01110}},
            {'d', new byte[]{0b11100,0b10010,0b10001,0b10001,0b10001,0b10010,0b11100}},
            {'e', new byte[]{0b11111,0b10000,0b10000,0b11110,0b10000,0b10000,0b11111}},
            {'f', new byte[]{0b11111,0b10000,0b10000,0b11110,0b10000,0b10000,0b10000}},
            {'g', new byte[]{0b01110,0b10001,0b10000,0b10111,0b10001,0b10001,0b01110}},
            {'h', new byte[]{0b10001,0b10001,0b10001,0b11111,0b10001,0b10001,0b10001}},
            {'i', new byte[]{0b11111,0b00100,0b00100,0b00100,0b00100,0b00100,0b11111}},
            {'j', new byte[]{0b11111,0b00010,0b00010,0b00010,0b00010,0b10010,0b01100}},
            {'k', new byte[]{0b10001,0b10010,0b10100,0b11000,0b10100,0b10010,0b10001}},
            {'l', new byte[]{0b10000,0b10000,0b10000,0b10000,0b10000,0b10000,0b11111}},
            {'m', new byte[]{0b10001,0b11011,0b10101,0b10101,0b10001,0b10001,0b10001}},
            {'n', new byte[]{0b10001,0b11001,0b10101,0b10011,0b10001,0b10001,0b10001}},
            {'o', new byte[]{0b01110,0b10001,0b10001,0b10001,0b10001,0b10001,0b01110}},
            {'p', new byte[]{0b11110,0b10001,0b10001,0b11110,0b10000,0b10000,0b10000}},
            {'q', new byte[]{0b01110,0b10001,0b10001,0b10001,0b10101,0b10010,0b01101}},
            {'r', new byte[]{0b11110,0b10001,0b10001,0b11110,0b10100,0b10010,0b10001}},
            {'s', new byte[]{0b01111,0b10000,0b10000,0b01110,0b00001,0b00001,0b11110}},
            {'t', new byte[]{0b11111,0b00100,0b00100,0b00100,0b00100,0b00100,0b00100}},
            {'u', new byte[]{0b10001,0b10001,0b10001,0b10001,0b10001,0b10001,0b01110}},
            {'v', new byte[]{0b10001,0b10001,0b10001,0b10001,0b10001,0b01010,0b00100}},
            {'w', new byte[]{0b10001,0b10001,0b10001,0b10101,0b10101,0b11011,0b10001}},
            {'x', new byte[]{0b10001,0b01010,0b00100,0b00100,0b00100,0b01010,0b10001}},
            {'y', new byte[]{0b10001,0b10001,0b01010,0b00100,0b00100,0b00100,0b00100}},
            {'z', new byte[]{0b11111,0b00001,0b00010,0b00100,0b01000,0b10000,0b11111}},
        };

        private static byte[] GetGlyph(char ch)
        {
            ch = char.ToLowerInvariant(ch);
            if (Font5x7.TryGetValue(ch, out byte[] glyph))
                return glyph;
            if (Font5x7.TryGetValue(' ', out byte[] space))
                return space;
            return null;
        }

        private static void GetLetterPreset(char ch, out Color color, out FlyDirection dir, out bool isPulse)
        {
            // Mapping : touches D, A, N, E, M, R, K → preset index 0..6
            ch = char.ToUpperInvariant(ch);
            int idx;
            switch (ch)
            {
                case 'D': idx = 0; break;
                case 'A': idx = 1; break;
                case 'N': idx = 2; break;
                case 'E': idx = 3; break;
                case 'M': idx = 4; break;
                case 'R': idx = 5; break;
                case 'K': idx = 6; break;
                default:  idx = 0; break;
            }

            color = LetterColors[idx];
            dir = LetterDirections[idx];
            isPulse = LetterIsPulse[idx];
        }

        private static void GetStartEnd(FlyDirection dir, out float sx, out float sy, out float ex, out float ey)
        {
            switch (dir)
            {
                case FlyDirection.LeftToRight:
                    sx = -0.15f; sy = 0.5f; ex = 1.15f; ey = 0.5f; break;
                case FlyDirection.RightToLeft:
                    sx = 1.15f; sy = 0.5f; ex = -0.15f; ey = 0.5f; break;
                case FlyDirection.TopToBottom:
                    sx = 0.5f; sy = -0.15f; ex = 0.5f; ey = 1.15f; break;
                case FlyDirection.BottomToTop:
                    sx = 0.5f; sy = 1.15f; ex = 0.5f; ey = -0.15f; break;
                case FlyDirection.DiagTopLeftToBottomRight:
                    sx = -0.15f; sy = -0.15f; ex = 1.15f; ey = 1.15f; break;
                case FlyDirection.DiagBottomLeftToTopRight:
                    sx = -0.15f; sy = 1.15f; ex = 1.15f; ey = -0.15f; break;
                case FlyDirection.DiagTopRightToBottomLeft:
                    sx = 1.15f; sy = -0.15f; ex = -0.15f; ey = 1.15f; break;
                default:
                    sx = -0.15f; sy = 0.5f; ex = 1.15f; ey = 0.5f; break;
            }
        }

        private static Color32 AdditiveBlend(Color32 dst, Color32 src)
        {
            float a = src.a / 255f;
            return new Color32(
                (byte)Mathf.Min(255, dst.r + src.r * a),
                (byte)Mathf.Min(255, dst.g + src.g * a),
                (byte)Mathf.Min(255, dst.b + src.b * a),
                255);
        }
    }
}
