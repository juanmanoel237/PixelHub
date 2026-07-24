using UnityEngine;
using Laps.Core;
using Laps.Routing;

namespace Laps.Authoring
{
    /// <summary>
    /// Prévisualisation à l'écran (onglet Game) de ce que Unity envoie aux LEDs.
    /// Unity n'allume pas le mur LED dans la Scene 3D : cette overlay montre l'état DMX simulé.
    /// </summary>
    public class LedPreviewOverlay : MonoBehaviour
    {
        private IStateProvider _provider;
        private RoutingEngine _routingEngine;
        private Texture2D _previewTexture;
        private Color[] _previewPixels;
        private int _screenWidth;
        private int _screenHeight;
        private int _previewDispW;
        private int _previewDispH;
        private string _modeLabel = "—";
        private float _refreshTimer;

        private float _bassPeak;
        private float _highPeak;

        // Stickmen toujours visibles dans l'aperçu LED (luma-key).
        private VideoOverlayRenderer _videoOverlay;
        private bool _showCombatOverlay = true;

        private const int PreviewMax = 420;

        public void Init(RoutingEngine routing)
        {
            _routingEngine = routing;
        }

        public void SetProvider(IStateProvider provider, string modeLabel)
        {
            _provider = provider;
            _modeLabel = modeLabel ?? "—";
            _refreshTimer = 0f;
            ForceRefresh();
        }

        /// <summary>Rafraîchit tout de suite l'aperçu (sync eHub / changement de mode).</summary>
        public void ForceRefresh()
        {
            RefreshPreview();
        }

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += InitBuffers;
            if (ConfigManager.Config != null) InitBuffers();

            // Cherche le VideoOverlayRenderer (créé par le Bootstrapper)
            _videoOverlay = FindObjectOfType<VideoOverlayRenderer>();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= InitBuffers;
            if (_previewTexture != null) Destroy(_previewTexture);
        }

        private void InitBuffers()
        {
            if (ConfigManager.Config == null) return;
            _screenWidth = Mathf.Max(1, ConfigManager.Config.mapping.screenWidth);
            _screenHeight = Mathf.Max(1, ConfigManager.Config.mapping.screenHeight);

            float aspect = (float)_screenWidth / _screenHeight;
            _previewDispW = PreviewMax;
            _previewDispH = Mathf.Max(1, Mathf.RoundToInt(PreviewMax / aspect));

            if (_previewTexture != null) Destroy(_previewTexture);
            _previewTexture = new Texture2D(_previewDispW, _previewDispH, TextureFormat.RGB24, false);
            _previewTexture.filterMode = FilterMode.Bilinear;
            _previewTexture.wrapMode = TextureWrapMode.Clamp;
            _previewPixels = new Color[_previewDispW * _previewDispH];
        }

        private void Update()
        {
            // unscaledDeltaTime : l'aperçu continue même en pause (sync eHub)
            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer < 0.05f) return;
            _refreshTimer = 0f;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_previewTexture == null || _previewPixels == null) return;

            Color32[] state = null;
            if (_routingEngine != null && _routingEngine.TryGetDisplaySnapshot(out Color32[] routed))
                state = routed;

            if (state == null)
            {
                if (_provider == null) return;
                state = _provider.GetState();
                if (state == null || state.Length == 0) return;

                int len = Mathf.Min(state.Length, _screenWidth * _screenHeight);
                var copy = new Color32[len];
                System.Array.Copy(state, copy, len);
                LedFireworks.CompositeOnto(copy, _screenWidth, _screenHeight);
                LedTextOverlay.CompositeOnto(copy, _screenWidth, _screenHeight);
                state = copy;
            }

            if (state == null || state.Length == 0) return;

            // Upscale bilinéaire vers la résolution d'affichage UI (évite la pixellisation).
            LedBufferTransforms.UpscaleBilinearToTexture(
                state, _screenWidth, _screenHeight,
                _previewPixels, _previewDispW, _previewDispH,
                flipY: true, flipX: false);

            _previewTexture.SetPixels(_previewPixels);
            _previewTexture.Apply();
        }

        private void OnGUI()
        {
            const int margin = 10;
            int previewW = _previewDispW > 0 ? _previewDispW : PreviewMax;
            int previewH = _previewDispH > 0 ? _previewDispH : PreviewMax;

            GUI.Box(new Rect(margin, margin, 280, 200), "PixelHub — envoi Art-Net");

            float y = margin + 22;
            GUI.Label(new Rect(margin + 8, y, 244, 18), $"Mode : {_modeLabel}");
            y += 20;

            if (GUI.Button(new Rect(margin + 8, y, 128, 24), "Config IP (I)"))
                RouterPanelBus.RequestToggleConfig();
            if (GUI.Button(new Rect(margin + 142, y, 128, 24), "Debug DMX (U)"))
                RouterPanelBus.RequestToggleDebug();
            y += 28;

            if (_provider is AudioReactiveProvider audio)
            {
                string audioInfo = audio.HasAudioInput ? "Audio OK" : "Audio : démo (pulse)";
                GUI.Label(new Rect(margin + 8, y, 244, 18), $"Basses : {audio.BassSmooth:P0}  ({audioInfo})");
                y += 18;

                // Barres : basses / aigus
                const float barW = 160f;
                const float barH = 10f;
                const float barX = margin + 8;

                DrawBar(new Rect(barX, y + 2, barW, barH), audio.BassSmooth, ref _bassPeak, new Color(0.15f, 0.75f, 1.0f), "B");
                y += 16;
                DrawBar(new Rect(barX, y + 2, barW, barH), audio.HighSmooth, ref _highPeak, new Color(1.0f, 0.55f, 0.15f), "H");
                y += 16;

                // Voix + Onset
                DrawBar(new Rect(barX, y + 2, barW, barH), audio.VoiceSmooth, ref _highPeak, new Color(0.35f, 1.0f, 0.65f), "V");
                y += 16;
                string onset = audio.IsOnset ? "ON" : "—";
                GUI.Label(new Rect(margin + 8, y, 244, 18), $"Onset : {audio.OnsetSmooth:P0}  [{onset}]");
                y += 18;

                GUI.Label(new Rect(margin + 8, y, 244, 18), $"Effet : {audio.CurrentEffectLabel}");
                y += 18;

                if (audio.IsIntroPlaying)
                {
                    if (audio.IsIntroFullPhrase)
                        GUI.Label(new Rect(margin + 8, y, 244, 18), "Intro : phrase complète");
                    else
                        GUI.Label(new Rect(margin + 8, y, 244, 18),
                            $"Intro : {audio.IntroRevealedChars + 1}/{audio.IntroTotalChars}  « {char.ToUpper(audio.IntroCurrentLetter)} »");
                    y += 18;
                }
            }

            if (_routingEngine != null)
            {
                GUI.Label(new Rect(margin + 8, y, 264, 18), $"Paquets/s : {_routingEngine.PacketsPerSecond:F1}");
                y += 18;
                GUI.Label(new Rect(margin + 8, y, 264, 18), $"Total envoyés : {_routingEngine.PacketsSentTotal}");
                y += 18;
            }

            if (_provider is EHubReceiver ehubRx)
            {
                int port = ConfigManager.Config?.network?.ehubProtocolPort ?? 9001;
                GUI.Label(new Rect(margin + 8, y, 264, 18),
                    $"eHuB protocole : port {port} — {ehubRx.UpdatesReceived} RX, {ehubRx.LastEntityCount} entités");
                y += 18;
            }

            if (EHubStatus.Enabled)
            {
                if (EHubStatus.Connected)
                {
                    if (EHubStatus.Role == EHubRole.Host)
                    {
                        GUI.Label(new Rect(margin + 8, y, 244, 18),
                            $"eHub Hôte — {EHubStatus.TotalPostes} poste(s) — mur LED actif");
                    }
                    else
                    {
                        GUI.color = new Color(0.7f, 0.9f, 1f);
                        GUI.Label(new Rect(margin + 8, y, 244, 18),
                            $"eHub Client → {EHubStatus.HostIp} — aperçu local, LED = hôte");
                        GUI.color = Color.white;
                    }
                }
                else if (EHubStatus.HostDetectedOnLan)
                {
                    GUI.color = new Color(1f, 0.85f, 0.4f);
                    GUI.Label(new Rect(margin + 8, y, 244, 18), "eHub — hôte détecté, connectez-vous en client");
                    GUI.color = Color.white;
                }
                else
                    GUI.Label(new Rect(margin + 8, y, 244, 18), "eHub — 1 hôte + clients (panneau bas)");
                y += 18;
            }

            if (_provider is DebugPanel debugPanel)
            {
                GUI.color = new Color(1f, 0.9f, 0.3f);
                GUI.Label(new Rect(margin + 8, y, 264, 36), debugPanel.LastLyreStatus);
                GUI.color = Color.white;
                y += 36;
            }
            else if (_provider != null)
            {
                var lyres = _provider.GetLyreStates();
                if (lyres != null && lyres.Length > 0)
                {
                    GUI.Label(new Rect(margin + 8, y, 264, 18), $"Lyres actives : {lyres.Length}");
                    y += 18;
                }
            }

            if (ConfigManager.Config?.network.controllers?.Length > 0)
            {
                var c = ConfigManager.Config.network.controllers[0];
                GUI.Label(new Rect(margin + 8, y, 264, 18), $"Mur LED → {c.ip}:6454");
                y += 18;
            }

            if (ConfigManager.Config?.mapping?.lyres?.Length > 0)
            {
                var lyre = ConfigManager.Config.mapping.lyres[0];
                GUI.Label(new Rect(margin + 8, y, 264, 18), $"Lyres → {lyre.controllerIp} univ {lyre.universe}");
            }

            if (_previewTexture != null)
            {
                int x = Screen.width - previewW - margin;
                var map = ConfigManager.Config?.mapping;
                string orient = map != null ? $"Y={(map.flipY ? "flip" : "—")} X={(map.flipX ? "flip" : "—")}" : "";
                GUI.Box(new Rect(x - 4, margin - 4, previewW + 8, previewH + 28), $"Aperçu LEDs ({orient})");
                GUI.DrawTexture(new Rect(x, margin + 18, previewW, previewH), _previewTexture, ScaleMode.ScaleToFit);

                // Stickmen déjà composés dans le buffer routé (VideoOverlayCompositor).
            }

            GUI.Label(new Rect(margin, Screen.height - 22, Screen.width - margin * 2, 20),
                "Mur:R/G/B/0/1 | Lyres:6–9 | Config=I | Debug DMX=U | T/E/A/V | eHub bas");
        }

        private static void DrawBar(Rect rect, float value01, ref float peak, Color fill, string label)
        {
            value01 = Mathf.Clamp01(value01);

            // Peak hold simple
            peak = Mathf.Max(peak, value01);
            peak = Mathf.MoveTowards(peak, 0f, Time.deltaTime * 0.6f);

            // Fond
            var bg = new Color(0f, 0f, 0f, 0.35f);
            var border = new Color(1f, 1f, 1f, 0.22f);
            GUI.color = bg;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = border;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Texture2D.whiteTexture);

            // Remplissage
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * value01, rect.height), Texture2D.whiteTexture);

            // Peak marker
            GUI.color = Color.white;
            float px = rect.x + rect.width * peak;
            GUI.DrawTexture(new Rect(px, rect.y - 2, 2, rect.height + 4), Texture2D.whiteTexture);

            // Label
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.xMax + 6, rect.y - 4, 80, 18), $"{label}:{value01:P0}");
            GUI.color = Color.white;
        }
    }
}
