using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Composite vidéo overlay (neige) sur le buffer LED.
    /// Authoring alimente les pixels via NeigeOverlayRenderer ;
    /// Routing appelle CompositeOnto sans dépendre de Authoring.
    /// </summary>
    public static class NeigeOverlayCompositor
    {
        private const float LumaThreshold = 0.10f;
        private const float LumaSoftness = 0.08f;
        private const float LumaBoost = 3.0f;   // multiplie la luma pour que la neige ressorte
        private const float BrightBoost = 1.8f; // amplifie les couleurs pour les LEDs

        private static Color32[] _pixels;
        private static int _videoWidth;
        private static int _videoHeight;
        private static bool _hasFrame;

        public static void SetFrame(Color32[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0)
            {
                _hasFrame = false;
                return;
            }

            if (_pixels == null || _pixels.Length != pixels.Length)
                _pixels = new Color32[pixels.Length];

            // GPU readback : origine en bas → on retourne verticalement.
            int rowLen = width;
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * rowLen;
                int dstRow = y * rowLen;
                System.Array.Copy(pixels, srcRow, _pixels, dstRow, rowLen);
            }
            _videoWidth = width;
            _videoHeight = height;
            _hasFrame = true;
        }

        public static void ClearFrame() => _hasFrame = false;

        public static void CompositeOnto(Color32[] buffer, int screenWidth, int screenHeight)
        {
            if (!_hasFrame || _pixels == null || _videoWidth <= 0 || _videoHeight <= 0)
                return;
            if (buffer == null || screenWidth <= 0 || screenHeight <= 0)
                return;

            // Plein cadre : la vidéo occupe tout l'écran (pas de conservation du ratio)
            int offsetX = 0;
            int offsetY = 0;
            int drawW = screenWidth;
            int drawH = screenHeight;

            for (int y = 0; y < drawH; y++)
            {
                // Lecture normale (SetFrame a déjà inversé le readback GPU)
                int sy = y * _videoHeight / drawH;
                int by = offsetY + y;
                if (by < 0 || by >= screenHeight) continue;

                for (int x = 0; x < drawW; x++)
                {
                    int sx = x * _videoWidth / drawW;
                    int bx = offsetX + x;
                    if (bx < 0 || bx >= screenWidth) continue;

                    Color32 src = _pixels[sy * _videoWidth + sx];
                    float luma = (0.2126f * src.r + 0.7152f * src.g + 0.0722f * src.b) / 255f;
                    float alpha = Mathf.SmoothStep(LumaThreshold, LumaThreshold + LumaSoftness,
                                                   Mathf.Clamp01(luma * LumaBoost)) * (src.a / 255f);
                    if (alpha < 0.01f) continue;
                    // Boost brightness pour que la neige soit bien visible sur les LEDs
                    src = new Color32(
                        (byte)Mathf.Clamp(src.r * BrightBoost, 0, 255),
                        (byte)Mathf.Clamp(src.g * BrightBoost, 0, 255),
                        (byte)Mathf.Clamp(src.b * BrightBoost, 0, 255),
                        255);

                    int idx = by * screenWidth + bx;
                    if (idx < 0 || idx >= buffer.Length) continue;

                    Color32 dst = buffer[idx];
                    buffer[idx] = new Color32(
                        (byte)Mathf.Clamp(dst.r + (src.r - dst.r) * alpha, 0, 255),
                        (byte)Mathf.Clamp(dst.g + (src.g - dst.g) * alpha, 0, 255),
                        (byte)Mathf.Clamp(dst.b + (src.b - dst.b) * alpha, 0, 255),
                        255);
                }
            }
        }
    }
}
