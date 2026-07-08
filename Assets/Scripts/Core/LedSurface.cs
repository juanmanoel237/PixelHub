using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Écriture pixel sur la matrice LED en tenant compte du câblage serpentin
    /// (lignes impaires inversées) pour un rendu correct sur le mur physique.
    /// </summary>
    public static class LedSurface
    {
        public static int ToIndex(int x, int y, int width, bool serpentine)
        {
            int lx = serpentine && (y & 1) == 1 ? width - 1 - x : x;
            return y * width + lx;
        }

        public static void SetPixel(int w, int h, int x, int y, Color32 c, Color32[] buffer, bool serpentine)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            int idx = ToIndex(x, y, w, serpentine);
            if (idx < 0 || idx >= buffer.Length) return;
            buffer[idx] = c;
        }

        public static void FillRect(int w, int h, int x, int y, int rw, int rh, Color c, Color32[] buffer, bool serpentine)
        {
            int x1 = Mathf.Clamp(x, 0, w - 1);
            int y1 = Mathf.Clamp(y, 0, h - 1);
            int x2 = Mathf.Clamp(x + rw, 0, w);
            int y2 = Mathf.Clamp(y + rh, 0, h);
            Color32 c32 = c;
            for (int py = y1; py < y2; py++)
            {
                for (int px = x1; px < x2; px++)
                    SetPixel(w, h, px, py, c32, buffer, serpentine);
            }
        }

        /// <summary>Lit un pixel pour l'aperçu (coordonnées visuelles mur LED).</summary>
        public static Color32 GetPixelVisual(int w, int h, int x, int y, Color32[] buffer, bool serpentine)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return Color.black;
            int idx = ToIndex(x, y, w, serpentine);
            if (idx < 0 || idx >= buffer.Length) return Color.black;
            return buffer[idx];
        }
    }
}
