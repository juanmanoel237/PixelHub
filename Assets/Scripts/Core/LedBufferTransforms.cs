using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Transformations du buffer LED (flip) pour aligner Unity et le mur physique.
    /// </summary>
    public static class LedBufferTransforms
    {
        public static int GetSourceIndex(int x, int y, int width, int height, bool flipY, bool flipX)
        {
            int srcX = flipX ? (width - 1 - x) : x;
            int srcY = flipY ? (height - 1 - y) : y;
            return srcY * width + srcX;
        }

        public static void CopyToTexturePixels(Color32[] state, Color[] pixels, int width, int height, bool flipY, bool flipX)
        {
            if (state == null || pixels == null) return;
            int count = Mathf.Min(state.Length, pixels.Length);

            for (int i = 0; i < count; i++)
            {
                int x = i % width;
                int y = i / width;
                int src = GetSourceIndex(x, y, width, height, flipY, flipX);
                if (src < 0 || src >= state.Length) continue;
                pixels[i] = state[src];
            }
        }

        /// <summary>
        /// Upscale bilinéaire du buffer LED vers une texture d'aperçu (évite l'effet "gros pixels").
        /// SetPixels : y=0 en bas → flipY=true pour afficher le haut du buffer en haut de l'UI.
        /// </summary>
        public static void UpscaleBilinearToTexture(
            Color32[] state, int srcW, int srcH,
            Color[] pixels, int dstW, int dstH,
            bool flipY, bool flipX)
        {
            if (state == null || pixels == null || srcW <= 0 || srcH <= 0 || dstW <= 0 || dstH <= 0)
                return;

            int expected = dstW * dstH;
            if (pixels.Length < expected) return;

            for (int dy = 0; dy < dstH; dy++)
            {
                for (int dx = 0; dx < dstW; dx++)
                {
                    float srcXf = (dx + 0.5f) * srcW / dstW - 0.5f;
                    float srcYf = (dy + 0.5f) * srcH / dstH - 0.5f;

                    if (flipX) srcXf = (srcW - 1) - srcXf;
                    if (flipY) srcYf = (srcH - 1) - srcYf;

                    pixels[dy * dstW + dx] = SampleBilinear(state, srcW, srcH, srcXf, srcYf);
                }
            }
        }

        public static Color SampleBilinear(Color32[] buffer, int width, int height, float x, float y)
        {
            if (buffer == null || width <= 0 || height <= 0)
                return Color.black;

            x = Mathf.Clamp(x, 0f, width - 1);
            y = Mathf.Clamp(y, 0f, height - 1);

            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, height - 1);
            float tx = x - x0;
            float ty = y - y0;

            Color c00 = buffer[y0 * width + x0];
            Color c10 = buffer[y0 * width + x1];
            Color c01 = buffer[y1 * width + x0];
            Color c11 = buffer[y1 * width + x1];

            return Color.Lerp(Color.Lerp(c00, c10, tx), Color.Lerp(c01, c11, tx), ty);
        }

        public static Color32 SampleBilinear32(Color32[] buffer, int width, int height, float x, float y)
        {
            return (Color32)SampleBilinear(buffer, width, height, x, y);
        }

        public static void FlipBufferY(Color32[] buffer, int width, int height)
        {
            if (buffer == null || width <= 0 || height <= 0) return;
            for (int y = 0; y < height / 2; y++)
            {
                int y2 = height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int i1 = y * width + x;
                    int i2 = y2 * width + x;
                    var tmp = buffer[i1];
                    buffer[i1] = buffer[i2];
                    buffer[i2] = tmp;
                }
            }
        }

        public static void FlipBufferX(Color32[] buffer, int width, int height)
        {
            if (buffer == null || width <= 0 || height <= 0) return;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width / 2; x++)
                {
                    int x2 = width - 1 - x;
                    int i1 = y * width + x;
                    int i2 = y * width + x2;
                    var tmp = buffer[i1];
                    buffer[i1] = buffer[i2];
                    buffer[i2] = tmp;
                }
            }
        }
    }
}
