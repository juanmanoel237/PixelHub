using UnityEngine;

namespace Laps.Core
{
    /// <summary>
    /// Composite vidéo overlay (stickmen) sur le buffer LED.
    /// Authoring alimente les pixels ; Routing appelle CompositeOnto sans dépendre de Authoring.
    /// </summary>
    public static class VideoOverlayCompositor
    {
        private const float LumaThreshold = 0.10f;
        private const float LumaSoftness = 0.08f;

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

            float vidW = screenWidth;
            float vidH = vidW / ((float)_videoWidth / _videoHeight);
            if (vidH > screenHeight)
            {
                vidH = screenHeight;
                vidW = vidH * ((float)_videoWidth / _videoHeight);
            }

            int offsetX = (screenWidth - Mathf.RoundToInt(vidW)) / 2;
            int offsetY = screenHeight - Mathf.RoundToInt(vidH);
            int drawW = Mathf.RoundToInt(vidW);
            int drawH = Mathf.RoundToInt(vidH);

            for (int y = 0; y < drawH; y++)
            {
                float sy = (y + 0.5f) * _videoHeight / drawH - 0.5f;
                int by = offsetY + y;
                if (by < 0 || by >= screenHeight) continue;

                for (int x = 0; x < drawW; x++)
                {
                    float sx = (x + 0.5f) * _videoWidth / drawW - 0.5f;
                    int bx = offsetX + x;
                    if (bx < 0 || bx >= screenWidth) continue;

                    Color32 src = LedBufferTransforms.SampleBilinear32(_pixels, _videoWidth, _videoHeight, sx, sy);
                    float luma = (0.2126f * src.r + 0.7152f * src.g + 0.0722f * src.b) / 255f;
                    // Luma-key (MP4 fond noir) × canal alpha (WebM VP8+alpha)
                    float alpha = Mathf.SmoothStep(LumaThreshold, LumaThreshold + LumaSoftness, luma) * (src.a / 255f);
                    if (alpha < 0.01f) continue;

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
