using UnityEngine;
using System.Collections.Generic;

namespace Laps.Authoring
{
    /// <summary>
    /// Types d'effets disponibles dans la bibliothèque.
    /// Chaque effet peut être paramétré via EffectParameters.
    /// </summary>
    public enum EffectType
    {
        SolidColor,          // Couleur unie
        HorizontalGradient,  // Dégradé horizontal (gauche→droite)
        VerticalGradient,    // Dégradé vertical (haut→bas)
        HorizontalBar,       // Barre horizontale animée
        VerticalBar,         // Barre verticale animée
        Flash,               // Flash stroboscopique
        Pulse,               // Pulsation douce (sine wave)
        Rainbow,             // Arc-en-ciel animé
        Sparkle,             // Étincelles aléatoires
        Wipe,                // Balayage gauche→droite ou haut→bas
        RadialGradient,      // Dégradé radial depuis le centre
        Plasma,              // Effet plasma (ondulations colorées)
        ColorWave,           // Vague de couleur
        BlackOut,            // Tout éteindre
        TextDisplay,         // Affichage de texte bitmap (font 5×7)
    }

    /// <summary>
    /// Paramètres d'un effet (sérialisables pour la timeline).
    /// Tous les champs sont optionnels selon l'effet.
    /// </summary>
    [System.Serializable]
    public class EffectParameters
    {
        public Color colorA    = Color.red;
        public Color colorB    = Color.blue;
        public float speed     = 1f;     // Vitesse de l'animation (multiplicateur)
        public float width     = 0.1f;   // Largeur d'une barre (0-1, fraction de l'écran)
        public float intensity = 1f;     // Intensité globale (0-1)
        public float frequency = 1f;     // Fréquence (Hz) pour pulse/flash
        public int   direction = 0;      // 0 = avant, 1 = arrière
        public bool  loop      = true;

        // Paramètres TextDisplay
        public string text      = "HELLO"; // Texte à afficher
        public int    textScale = 5;        // Facteur d'échelle (5 = lettres 25×35px sur 256×256)
    }

    /// <summary>
    /// Bibliothèque d'effets LED.
    /// Chaque méthode prend le temps courant (t en secondes), les dimensions de l'écran,
    /// les paramètres, et remplit le tableau Color32[] fourni.
    ///
    /// Satisfait P3 : outil de programmation créative avec ensemble d'outils simples.
    /// </summary>
    public static class EffectLibrary
    {
        // ── Point d'entrée principal ───────────────────────────

        /// <summary>
        /// Évalue un effet et écrit le résultat dans <paramref name="output"/>.
        /// </summary>
        public static void Evaluate(
            EffectType type,
            float t,
            int screenWidth, int screenHeight,
            EffectParameters p,
            Color32[] output)
        {
            switch (type)
            {
                case EffectType.SolidColor:         FxSolidColor(p, output);                    break;
                case EffectType.HorizontalGradient: FxGradient(t, screenWidth, screenHeight, p, output, horizontal: true); break;
                case EffectType.VerticalGradient:   FxGradient(t, screenWidth, screenHeight, p, output, horizontal: false); break;
                case EffectType.HorizontalBar:      FxBar(t, screenWidth, screenHeight, p, output, horizontal: true);  break;
                case EffectType.VerticalBar:        FxBar(t, screenWidth, screenHeight, p, output, horizontal: false); break;
                case EffectType.Flash:              FxFlash(t, p, output);                      break;
                case EffectType.Pulse:              FxPulse(t, p, output);                      break;
                case EffectType.Rainbow:            FxRainbow(t, screenWidth, screenHeight, p, output); break;
                case EffectType.Sparkle:            FxSparkle(t, screenWidth, screenHeight, p, output); break;
                case EffectType.Wipe:               FxWipe(t, screenWidth, screenHeight, p, output);    break;
                case EffectType.RadialGradient:     FxRadial(t, screenWidth, screenHeight, p, output);  break;
                case EffectType.Plasma:             FxPlasma(t, screenWidth, screenHeight, p, output);  break;
                case EffectType.ColorWave:          FxColorWave(t, screenWidth, screenHeight, p, output); break;
                case EffectType.BlackOut:           FxBlackOut(output);                         break;
                case EffectType.TextDisplay:        FxTextDisplay(screenWidth, screenHeight, p, output); break;
            }
        }

        // ── Effets ────────────────────────────────────────────

        private static void FxSolidColor(EffectParameters p, Color32[] output)
        {
            Color32 c = ApplyIntensity(p.colorA, p.intensity);
            for (int i = 0; i < output.Length; i++)
                output[i] = c;
        }

        private static void FxBlackOut(Color32[] output)
        {
            for (int i = 0; i < output.Length; i++)
                output[i] = Color.black;
        }

        private static void FxGradient(float t, int w, int h, EffectParameters p, Color32[] output, bool horizontal)
        {
            float offset = t * p.speed * 0.1f;
            for (int i = 0; i < output.Length; i++)
            {
                float ratio = horizontal
                    ? (i % w) / (float)w
                    : i / w / (float)h;

                ratio = Mathf.Repeat(ratio + offset, 1f);
                if (p.direction == 1) ratio = 1f - ratio;
                output[i] = ApplyIntensity(Color.Lerp(p.colorA, p.colorB, ratio), p.intensity);
            }
        }

        private static void FxBar(float t, int w, int h, EffectParameters p, Color32[] output, bool horizontal)
        {
            float pos = Mathf.Repeat(t * p.speed, 1f);
            if (p.direction == 1) pos = 1f - pos;
            float half = p.width * 0.5f;

            for (int i = 0; i < output.Length; i++)
            {
                float ratio = horizontal
                    ? (i % w) / (float)w
                    : i / w / (float)h;

                float dist = Mathf.Abs(ratio - pos);
                dist = Mathf.Min(dist, 1f - dist); // Wrap around
                float blend = Mathf.Clamp01(1f - dist / Mathf.Max(half, 0.001f));
                output[i] = ApplyIntensity(Color.Lerp(Color.black, p.colorA, blend), p.intensity);
            }
        }

        private static void FxFlash(float t, EffectParameters p, Color32[] output)
        {
            float cycle = Mathf.Repeat(t * p.frequency, 1f);
            bool on = cycle < 0.5f;
            Color32 c = on ? ApplyIntensity(p.colorA, p.intensity) : (Color32)Color.black;
            for (int i = 0; i < output.Length; i++)
                output[i] = c;
        }

        private static void FxPulse(float t, EffectParameters p, Color32[] output)
        {
            float brightness = (Mathf.Sin(t * p.frequency * Mathf.PI * 2f) * 0.5f + 0.5f) * p.intensity;
            Color32 c = ApplyIntensity(p.colorA, brightness);
            for (int i = 0; i < output.Length; i++)
                output[i] = c;
        }

        private static void FxRainbow(float t, int w, int h, EffectParameters p, Color32[] output)
        {
            float offset = t * p.speed * 0.3f;
            for (int i = 0; i < output.Length; i++)
            {
                float hue = Mathf.Repeat((i % w) / (float)w + offset, 1f);
                output[i] = ApplyIntensity(Color.HSVToRGB(hue, 1f, 1f), p.intensity);
            }
        }

        private static readonly System.Random _rng = new System.Random(42);
        private static void FxSparkle(float t, int w, int h, EffectParameters p, Color32[] output)
        {
            // Base noire
            for (int i = 0; i < output.Length; i++) output[i] = Color.black;

            // Générer des étincelles basées sur le temps (déterministe)
            int seed = (int)(t * p.frequency * 100f);
            var rng = new System.Random(seed);
            int count = (int)(output.Length * p.width * 0.1f);
            for (int j = 0; j < count; j++)
            {
                int idx = rng.Next(output.Length);
                output[idx] = ApplyIntensity(p.colorA, p.intensity);
            }
        }

        private static void FxWipe(float t, int w, int h, EffectParameters p, Color32[] output)
        {
            float progress = Mathf.Repeat(t * p.speed * 0.5f, 1f);
            for (int i = 0; i < output.Length; i++)
            {
                float ratio = (i % w) / (float)w;
                bool lit = p.direction == 0 ? ratio < progress : ratio > (1f - progress);
                output[i] = lit ? ApplyIntensity(p.colorA, p.intensity) : (Color32)Color.black;
            }
        }

        private static void FxRadial(float t, int w, int h, EffectParameters p, Color32[] output)
        {
            float cx = w * 0.5f;
            float cy = h * 0.5f;
            float maxDist = Mathf.Sqrt(cx * cx + cy * cy);
            float offset = t * p.speed * 0.5f;

            for (int i = 0; i < output.Length; i++)
            {
                float x = i % w - cx;
                float y = i / w - cy;
                float dist = Mathf.Sqrt(x * x + y * y) / maxDist;
                float ratio = Mathf.Repeat(dist - offset, 1f);
                if (p.direction == 1) ratio = 1f - ratio;
                output[i] = ApplyIntensity(Color.Lerp(p.colorA, p.colorB, ratio), p.intensity);
            }
        }

        private static void FxPlasma(float t, int w, int h, EffectParameters p, Color32[] output)
        {
            for (int i = 0; i < output.Length; i++)
            {
                float x = (i % w) / (float)w;
                float y = (i / w) / (float)h;
                float v = Mathf.Sin(x * 10f + t * p.speed)
                        + Mathf.Sin(y * 10f + t * p.speed * 0.7f)
                        + Mathf.Sin((x + y) * 5f + t * p.speed * 1.3f)
                        + Mathf.Sin(Mathf.Sqrt(x * x + y * y) * 10f + t * p.speed);
                float hue = (v * 0.25f + 0.5f) % 1f;
                output[i] = ApplyIntensity(Color.HSVToRGB(hue, 1f, 1f), p.intensity);
            }
        }

        private static void FxColorWave(float t, int w, int h, EffectParameters p, Color32[] output)
        {
            for (int i = 0; i < output.Length; i++)
            {
                float x = (i % w) / (float)w;
                float wave = Mathf.Sin(x * Mathf.PI * 4f - t * p.speed * Mathf.PI * 2f) * 0.5f + 0.5f;
                output[i] = ApplyIntensity(Color.Lerp(p.colorA, p.colorB, wave), p.intensity);
            }
        }

        // ── Utilitaires ────────────────────────────────────────

        private static Color32 ApplyIntensity(Color c, float intensity)
        {
            return new Color32(
                (byte)(c.r * 255 * intensity),
                (byte)(c.g * 255 * intensity),
                (byte)(c.b * 255 * intensity),
                255);
        }

        private static Color32 ApplyIntensity(Color32 c, float intensity)
        {
            return new Color32(
                (byte)(c.r * intensity),
                (byte)(c.g * intensity),
                (byte)(c.b * intensity),
                255);
        }

        // ── TextDisplay ────────────────────────────────────────

        // Font bitmap 5×7 : chaque lettre = 7 lignes, chaque ligne = 5 bits
        // Bit le plus significatif = colonne gauche
        private static readonly System.Collections.Generic.Dictionary<char, byte[]> Font5x7
            = new System.Collections.Generic.Dictionary<char, byte[]>
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
            {'0', new byte[]{0b01110,0b10001,0b10011,0b10101,0b11001,0b10001,0b01110}},
            {'1', new byte[]{0b00100,0b01100,0b00100,0b00100,0b00100,0b00100,0b11111}},
            {'2', new byte[]{0b01110,0b10001,0b00001,0b00110,0b01000,0b10000,0b11111}},
            {'3', new byte[]{0b11111,0b00001,0b00010,0b00110,0b00001,0b10001,0b01110}},
            {'4', new byte[]{0b00010,0b00110,0b01010,0b10010,0b11111,0b00010,0b00010}},
            {'5', new byte[]{0b11111,0b10000,0b11110,0b00001,0b00001,0b10001,0b01110}},
            {'6', new byte[]{0b01110,0b10000,0b10000,0b11110,0b10001,0b10001,0b01110}},
            {'7', new byte[]{0b11111,0b00001,0b00010,0b00100,0b01000,0b01000,0b01000}},
            {'8', new byte[]{0b01110,0b10001,0b10001,0b01110,0b10001,0b10001,0b01110}},
            {'9', new byte[]{0b01110,0b10001,0b10001,0b01111,0b00001,0b00001,0b01110}},
            {'!', new byte[]{0b00100,0b00100,0b00100,0b00100,0b00000,0b00000,0b00100}},
            {'.', new byte[]{0b00000,0b00000,0b00000,0b00000,0b00000,0b00000,0b00100}},
            {'?', new byte[]{0b01110,0b10001,0b00001,0b00010,0b00100,0b00000,0b00100}},
            {'-', new byte[]{0b00000,0b00000,0b00000,0b11111,0b00000,0b00000,0b00000}},
            {':', new byte[]{0b00000,0b00100,0b00000,0b00000,0b00100,0b00000,0b00000}},
        };

        private const int FONT_W = 5;
        private const int FONT_H = 7;
        private const int CHAR_GAP = 1;

        /// <summary>
        /// Rend le texte <see cref="EffectParameters.text"/> centré sur la matrice.
        /// Échelle configurable via <see cref="EffectParameters.textScale"/>.
        /// </summary>
        private static void FxTextDisplay(int w, int h, EffectParameters p, Color32[] output)
        {
            // Fond noir
            FxBlackOut(output);

            string text = (p.text ?? "HELLO").ToLowerInvariant();
            int scale   = Mathf.Max(1, p.textScale);
            Color32 fg  = ApplyIntensity(p.colorA, p.intensity);

            int glyphW = FONT_W * scale;
            int glyphH = FONT_H * scale;
            int gap    = CHAR_GAP * scale;

            int totalW = text.Length * (glyphW + gap) - gap;

            // Centrage horizontal et vertical
            int originX = Mathf.Max(0, (w - totalW) / 2);
            int originY = Mathf.Max(0, (h - glyphH) / 2);

            for (int ci = 0; ci < text.Length; ci++)
            {
                char ch = text[ci];
                if (!Font5x7.TryGetValue(ch, out byte[] glyph))
                    glyph = Font5x7[' '];

                int ox = originX + ci * (glyphW + gap);

                for (int row = 0; row < FONT_H; row++)
                {
                    byte bits = glyph[row];
                    for (int col = 0; col < FONT_W; col++)
                    {
                        // Bit actif ?
                        if ((bits & (1 << (FONT_W - 1 - col))) == 0) continue;

                        // Dessiner un bloc scale×scale
                        for (int sy = 0; sy < scale; sy++)
                        {
                            int py = originY + row * scale + sy;
                            if (py < 0 || py >= h) continue;

                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = ox + col * scale + sx;
                                if (px < 0 || px >= w) continue;

                                output[py * w + px] = fg;
                            }
                        }
                    }
                }
            }
        }
    }
}
