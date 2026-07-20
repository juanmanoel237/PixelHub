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
