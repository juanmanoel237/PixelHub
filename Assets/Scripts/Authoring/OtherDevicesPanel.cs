using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Pilotage des projecteurs DMX (mur LAPS) + simulateur visuel.
    /// - Rotation via flèches (pan/tilt manuel)
    /// - Option F3 : effets lumière + rotation auto "spectacle" sur la musique
    /// </summary>
    public class OtherDevicesPanel : MonoBehaviour, ILyreStateProvider
    {
        private const int HeadCount = 4;
        private const int StaticIndex = 0;
        private const int FirstHeadIndex = 1;

        private readonly LyreState[] _states = new LyreState[5];

        [Header("Effets au beat (F3, sans rotation auto)")]
        [SerializeField] private bool _nightclubMode = false;
        [SerializeField] private float _beatStrobeDuration = 0.12f;
        [SerializeField] private bool _beatColorCycle = true;
        [SerializeField, Range(0f, 1f)] private float _beatHueStep = 0.06f;
        [SerializeField] private bool _autoRotateOnBeat = true;
        [SerializeField, Range(10f, 127f)] private float _autoPanAmplitude = 84f;
        [SerializeField, Range(6f, 90f)] private float _autoTiltAmplitude = 34f;
        [SerializeField, Range(0.2f, 2f)] private float _autoSpeedMin = 0.6f;
        [SerializeField, Range(0.5f, 4f)] private float _autoSpeedMax = 2.2f;

        [Header("Contrôle manuel (flèches)")]
        [SerializeField] private float _arrowPanSpeed = 120f;
        [SerializeField] private float _arrowTiltSpeed = 90f;

        private bool _show = true;
        private int _colorTargetHead = 0; // 0..3, lyre sélectionnée (Ctrl+1..4)
        private AudioReactiveProvider _audio;
        private float[] _spectrum;
        private float _bassSmooth;
        private float _lastBeatTime = -10f;
        private float _strobeUntil;
        private readonly float[] _headHue = { 0.86f, 0.55f, 0.13f, 0.72f };

        private float _panTiltSyncAccumPan;
        private float _panTiltSyncAccumTilt;
        private float _panTiltSyncTimer;
        private int _panTiltSyncHead = -1;

        public int ColorTargetHead => _colorTargetHead;
        public bool NightclubMode => _nightclubMode;
        public bool BeatColorCycle => _beatColorCycle;

        private static readonly Color32[] PresetColors =
        {
            new Color32(255, 0, 0, 255),
            new Color32(0, 255, 0, 255),
            new Color32(0, 120, 255, 255),
            new Color32(255, 0, 255, 255),
            new Color32(255, 255, 0, 255),
            new Color32(255, 128, 0, 255),
            new Color32(0, 255, 220, 255),
            new Color32(255, 255, 255, 255),
        };

        private void Awake()
        {
            _states[StaticIndex] = new LyreState
            {
                lyreName = "StaticProjector",
                dimmer = 0,
                color = new Color32(0, 0, 0, 255)
            };

            for (int i = 0; i < HeadCount; i++)
            {
                int idx = FirstHeadIndex + i;
                _states[idx] = new LyreState
                {
                    lyreName = $"MovingHead{i + 1}",
                    pan = 128f,
                    tilt = 128f,
                    dimmer = 0,
                    color = new Color32(0, 0, 0, 255),
                    strobe = 0f
                };
            }
        }

        private void Start()
        {
            _audio = FindObjectOfType<AudioReactiveProvider>();
            _spectrum = new float[512];
        }

        public LyreState[] GetLyreStates() => _states;

        public void TestMovingHead(int headIndex)
        {
            if (headIndex < 1 || headIndex > HeadCount) return;
            int idx = FirstHeadIndex + headIndex - 1;
            var s = _states[idx];
            s.pan = 128f;
            s.tilt = 128f;
            s.dimmer = 255;
            s.color = new Color32(255, 255, 255, 255);
            s.strobe = 0;
            _states[idx] = s;
            Debug.Log($"[OtherDevicesPanel] ★ MovingHead{headIndex} ON");
        }

        public void TestStaticProjector()
        {
            var s = _states[StaticIndex];
            s.dimmer = 255;
            s.color = new Color32(255, 255, 255, 255);
            _states[StaticIndex] = s;
            Debug.Log("[OtherDevicesPanel] ★ StaticProjector ON");
        }

        public void BlackOutAllLyres()
        {
            for (int i = 0; i < _states.Length; i++)
            {
                var s = _states[i];
                s.dimmer = 0;
                s.strobe = 0;
                _states[i] = s;
            }
            Debug.Log("[OtherDevicesPanel] Lyres OFF");
        }

        public void SetNightclubMode(bool enabled) => _nightclubMode = enabled;

        public void RequestSelectHead(int head)
        {
            ApplySelectHead(head);
            PublishLyre(EHubLyreAction.SelectHead, head);
        }

        public void RequestNightclubToggle()
        {
            _nightclubMode = !_nightclubMode;
            PublishLyre(EHubLyreAction.NightclubToggle, _nightclubMode ? 1 : 0);
        }

        public void RequestBeatColorCycle(bool enabled)
        {
            _beatColorCycle = enabled;
            PublishLyre(EHubLyreAction.BeatColorCycle, enabled ? 1 : 0);
        }

        public void RequestPresetColor(int head, int presetIndex)
        {
            ApplyPresetColor(head, presetIndex);
            PublishLyre(EHubLyreAction.PresetColor, head, presetIndex);
        }

        public void RequestSetAllPreset(int presetIndex)
        {
            ApplySetAllPreset(presetIndex);
            PublishLyre(EHubLyreAction.SetAllPreset, -1, presetIndex);
        }

        public void RequestSetRgb(int head, byte r, byte g, byte b)
        {
            ApplySetRgb(head, r, g, b);
            PublishLyre(EHubLyreAction.SetRgb, head, 0, 0, $"{r},{g},{b}");
        }

        public void ApplyLyreControl(EHubMessage msg)
        {
            switch (msg.intArg)
            {
                case EHubLyreAction.SelectHead:
                    ApplySelectHead(msg.intArg2);
                    break;
                case EHubLyreAction.PresetColor:
                    ApplyPresetColor(msg.intArg2, (int)msg.floatArg);
                    break;
                case EHubLyreAction.PanTiltDelta:
                    ApplyPanTiltDelta(msg.intArg2, msg.floatArg, msg.floatArg2);
                    break;
                case EHubLyreAction.NightclubToggle:
                    _nightclubMode = msg.intArg2 == 1;
                    break;
                case EHubLyreAction.SetAllPreset:
                    ApplySetAllPreset((int)msg.floatArg);
                    break;
                case EHubLyreAction.SetRgb:
                    if (TryParseRgb(msg.stringArg, out byte r, out byte g, out byte b))
                        ApplySetRgb(msg.intArg2, r, g, b);
                    break;
                case EHubLyreAction.BeatColorCycle:
                    _beatColorCycle = msg.intArg2 == 1;
                    break;
                case EHubLyreAction.SyncSnapshot:
                    ApplySyncSnapshot(msg.stringArg);
                    break;
            }
        }

        public string BuildSyncSnapshot()
        {
            var parts = new System.Text.StringBuilder();
            parts.Append(_colorTargetHead).Append('|');
            parts.Append(_nightclubMode ? '1' : '0').Append('|');
            parts.Append(_beatColorCycle ? '1' : '0').Append('|');
            for (int i = 0; i < HeadCount; i++)
            {
                var s = _states[FirstHeadIndex + i];
                parts.Append(i).Append(':').Append(s.pan.ToString("F1")).Append(',')
                    .Append(s.tilt.ToString("F1")).Append(',')
                    .Append(s.color.r).Append(',').Append(s.color.g).Append(',')
                    .Append(s.color.b).Append(',').Append(s.dimmer).Append('|');
            }
            var st = _states[StaticIndex];
            parts.Append("S:").Append(st.color.r).Append(',').Append(st.color.g).Append(',')
                .Append(st.color.b).Append(',').Append(st.dimmer);
            return parts.ToString();
        }

        private void ApplySyncSnapshot(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            string[] chunks = data.Split('|');
            if (chunks.Length < 3) return;

            if (int.TryParse(chunks[0], out int target)) _colorTargetHead = target;
            _nightclubMode = chunks[1] == "1";
            _beatColorCycle = chunks[2] == "1";

            for (int c = 3; c < chunks.Length; c++)
            {
                string chunk = chunks[c];
                if (string.IsNullOrEmpty(chunk)) continue;
                if (chunk.StartsWith("S:"))
                {
                    if (TryParseHeadState(chunk.Substring(2), out _, out _, out byte r, out byte g, out byte b, out byte dim))
                    {
                        var s = _states[StaticIndex];
                        s.color = new Color32(r, g, b, 255);
                        s.dimmer = dim;
                        _states[StaticIndex] = s;
                    }
                    continue;
                }

                int colon = chunk.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(chunk.Substring(0, colon), out int head)) continue;
                if (!TryParseHeadState(chunk.Substring(colon + 1), out float pan, out float tilt,
                        out byte cr, out byte cg, out byte cb, out byte cdim)) continue;

                int idx = FirstHeadIndex + Mathf.Clamp(head, 0, HeadCount - 1);
                var hs = _states[idx];
                hs.pan = pan;
                hs.tilt = tilt;
                hs.color = new Color32(cr, cg, cb, 255);
                hs.dimmer = cdim;
                _states[idx] = hs;
            }
        }

        private static bool TryParseHeadState(string s, out float pan, out float tilt,
            out byte r, out byte g, out byte b, out byte dimmer)
        {
            pan = tilt = 0;
            r = g = b = dimmer = 0;
            string[] p = s.Split(',');
            if (p.Length < 6) return false;
            return float.TryParse(p[0], out pan)
                && float.TryParse(p[1], out tilt)
                && byte.TryParse(p[2], out r)
                && byte.TryParse(p[3], out g)
                && byte.TryParse(p[4], out b)
                && byte.TryParse(p[5], out dimmer);
        }

        private static bool TryParseRgb(string s, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string[] p = s.Split(',');
            return p.Length >= 3
                && byte.TryParse(p[0], out r)
                && byte.TryParse(p[1], out g)
                && byte.TryParse(p[2], out b);
        }

        private void PublishLyre(int action, int head, float f1 = 0f, float f2 = 0f, string s = null)
        {
            EHubSyncBus.PublishLocal(new EHubMessage
            {
                type = EHubMessageTypes.LyreControl,
                intArg = action,
                intArg2 = head,
                floatArg = f1,
                floatArg2 = f2,
                stringArg = s
            });
        }

        private void ApplySelectHead(int head) => _colorTargetHead = Mathf.Clamp(head, 0, HeadCount - 1);

        private void ApplyPresetColor(int head, int presetIndex)
        {
            head = Mathf.Clamp(head, 0, HeadCount - 1);
            int idx = FirstHeadIndex + head;
            var s = _states[idx];
            if (presetIndex == 999)
                ApplyColor(ref s, new Color32(0, 0, 0, 255));
            else if (presetIndex >= 0 && presetIndex < PresetColors.Length)
                ApplyColor(ref s, PresetColors[presetIndex]);
            _states[idx] = s;
        }

        private void ApplySetAllPreset(int presetIndex)
        {
            Color32 c = presetIndex == 999
                ? new Color32(0, 0, 0, 255)
                : PresetColors[Mathf.Clamp(presetIndex, 0, PresetColors.Length - 1)];
            SetAllHeadsColor(c);
        }

        private void ApplySetRgb(int head, byte r, byte g, byte b)
        {
            head = Mathf.Clamp(head, 0, HeadCount - 1);
            int idx = FirstHeadIndex + head;
            var s = _states[idx];
            ApplyColor(ref s, new Color32(r, g, b, 255));
            _states[idx] = s;
        }

        private void ApplyPanTiltDelta(int head, float panDelta, float tiltDelta)
        {
            head = Mathf.Clamp(head, 0, HeadCount - 1);
            int idx = FirstHeadIndex + head;
            var s = _states[idx];
            s.pan = Mathf.Clamp(s.pan + panDelta, 0f, 255f);
            s.tilt = Mathf.Clamp(s.tilt + tiltDelta, 0f, 255f);
            _states[idx] = s;
        }

        private void FlushPanTiltSync()
        {
            if (_panTiltSyncHead < 0) return;
            if (Mathf.Abs(_panTiltSyncAccumPan) < 0.001f && Mathf.Abs(_panTiltSyncAccumTilt) < 0.001f)
                return;

            PublishLyre(EHubLyreAction.PanTiltDelta, _panTiltSyncHead, _panTiltSyncAccumPan, _panTiltSyncAccumTilt);
            _panTiltSyncAccumPan = 0f;
            _panTiltSyncAccumTilt = 0f;
            _panTiltSyncTimer = 0f;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
                _show = !_show;
            if (Input.GetKeyDown(KeyCode.F3))
                RequestNightclubToggle();

            HandleColorHotkeys();
            HandleArrowControl();
            UpdateBeatEffects();
        }

        /// <summary>
        /// Flèches = pan/tilt de la lyre sélectionnée (Ctrl+1..4).
        /// Gauche/Droite = pan, Haut/Bas = tilt.
        /// </summary>
        private void HandleArrowControl()
        {
            if (GlobalPause.IsPaused) return;

            float panDelta = 0f;
            float tiltDelta = 0f;
            if (Input.GetKey(KeyCode.LeftArrow)) panDelta -= _arrowPanSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.RightArrow)) panDelta += _arrowPanSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.UpArrow)) tiltDelta += _arrowTiltSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.DownArrow)) tiltDelta -= _arrowTiltSpeed * Time.deltaTime;

            if (Mathf.Abs(panDelta) < 0.001f && Mathf.Abs(tiltDelta) < 0.001f)
                return;

            int idx = FirstHeadIndex + Mathf.Clamp(_colorTargetHead, 0, HeadCount - 1);
            var s = _states[idx];
            s.pan = Mathf.Clamp(s.pan + panDelta, 0f, 255f);
            s.tilt = Mathf.Clamp(s.tilt + tiltDelta, 0f, 255f);
            _states[idx] = s;

            if (_panTiltSyncHead != _colorTargetHead)
            {
                FlushPanTiltSync();
                _panTiltSyncHead = _colorTargetHead;
            }
            _panTiltSyncAccumPan += panDelta;
            _panTiltSyncAccumTilt += tiltDelta;
            _panTiltSyncTimer += Time.deltaTime;
            if (_panTiltSyncTimer >= 0.05f)
                FlushPanTiltSync();
        }

        /// <summary>
        /// 1) Ctrl+1..4 = sélectionner la lyre à recolorer
        /// 2) R/G/B/M/J/C/W/0 = appliquer la couleur à la lyre sélectionnée
        /// </summary>
        private void HandleColorHotkeys()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) RequestSelectHead(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) RequestSelectHead(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) RequestSelectHead(2);
                else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) RequestSelectHead(3);
            }

            if (_colorTargetHead < 0) return;

            int head = _colorTargetHead;
            if (Input.GetKeyDown(KeyCode.R)) RequestPresetColor(head, 0);
            else if (Input.GetKeyDown(KeyCode.G)) RequestPresetColor(head, 1);
            else if (Input.GetKeyDown(KeyCode.B)) RequestPresetColor(head, 2);
            else if (Input.GetKeyDown(KeyCode.M)) RequestPresetColor(head, 3);
            else if (Input.GetKeyDown(KeyCode.J)) RequestPresetColor(head, 4);
            else if (Input.GetKeyDown(KeyCode.C)) RequestPresetColor(head, 6);
            else if (Input.GetKeyDown(KeyCode.W)) RequestPresetColor(head, 7);
            else if (Input.GetKeyDown(KeyCode.Alpha0)) RequestPresetColor(head, 999);
        }

        private void UpdateBeatEffects()
        {
            if (!_nightclubMode) return;
            if (GlobalPause.IsPaused) return;

            float bass = ReadBass01();
            bool beat = DetectBeat(bass);
            if (beat)
                _strobeUntil = Time.time + _beatStrobeDuration;

            float strobeActive = Time.time < _strobeUntil ? 200f : 0f;

            for (int i = 0; i < HeadCount; i++)
            {
                int idx = FirstHeadIndex + i;
                var s = _states[idx];

                // Rotation auto "spectacle" synchronisée sur l'intensité (si F3 actif)
                if (_autoRotateOnBeat)
                {
                    float energy = Mathf.Clamp01(bass + (beat ? 0.25f : 0f));
                    float speed = Mathf.Lerp(_autoSpeedMin, _autoSpeedMax, energy);
                    float phase = i * 1.35f;
                    float panAmp = _autoPanAmplitude * (0.65f + 0.35f * energy);
                    float tiltAmp = _autoTiltAmplitude * (0.6f + 0.4f * energy);
                    s.pan = Mathf.Clamp(128f + Mathf.Sin(Time.time * speed + phase) * panAmp, 0f, 255f);
                    s.tilt = Mathf.Clamp(128f + Mathf.Cos(Time.time * (speed * 0.8f) + phase * 1.15f) * tiltAmp, 0f, 255f);
                }

                s.dimmer = Mathf.Clamp(200f + bass * 55f + (beat ? 55f : 0f), 0f, 255f);
                s.strobe = strobeActive;

                if (_beatColorCycle && beat)
                {
                    _headHue[i] = Mathf.Repeat(_headHue[i] + _beatHueStep, 1f);
                    Color c = Color.HSVToRGB(_headHue[i], 0.95f, 1f);
                    s.color = (Color32)c;
                }

                _states[idx] = s;
            }

            var st = _states[StaticIndex];
            st.dimmer = Mathf.Clamp(140f + bass * 115f, 0f, 255f);
            _states[StaticIndex] = st;
        }

        private float ReadBass01()
        {
            if (_audio != null)
                return Mathf.Clamp01(_audio.BassSmooth);

            AudioListener.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
            float sum = 0f;
            int end = Mathf.Min(12, _spectrum.Length);
            for (int i = 2; i < end; i++) sum += _spectrum[i];
            float raw = sum / Mathf.Max(1, end - 2) * 80f;
            _bassSmooth = Mathf.Lerp(_bassSmooth, Mathf.Clamp01(raw), Time.deltaTime * 8f);
            return _bassSmooth;
        }

        private bool DetectBeat(float bass)
        {
            if (_audio != null && _audio.IsOnset)
            {
                _lastBeatTime = Time.time;
                return true;
            }

            if (bass > 0.55f && Time.time - _lastBeatTime > 0.18f)
            {
                _lastBeatTime = Time.time;
                return true;
            }
            return false;
        }

        private static void ApplyColor(ref LyreState s, Color32 c)
        {
            s.color = c;
            if (c.r == 0 && c.g == 0 && c.b == 0)
                s.dimmer = 0;
            else if (s.dimmer < 80f)
                s.dimmer = 255;
        }

        private void OnGUI()
        {
            DrawSimulator();
            if (!_show) return;

            // Au-dessus du panneau eHub (bas-gauche) pour éviter le chevauchement.
            const int eHubReserve = 148;
            const int w = 360;
            const int h = 158;
            var rect = new Rect(12, Screen.height - eHubReserve - h - 16, w, h);
            GUI.Box(rect, "Projecteurs — Flèches = pan/tilt | F2=masquer | Ctrl+1..4");

            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 22, rect.width - 16, rect.height - 28));

            string pauseLabel = GlobalPause.IsPaused ? " [PAUSE]" : "";
            GUILayout.Label($"Lyre {_colorTargetHead + 1} | F3 effets beat{pauseLabel}");

            GUILayout.BeginHorizontal();
            for (int i = 0; i < HeadCount; i++)
            {
                if (GUILayout.Button($"L{i + 1}"))
                    RequestPresetColor(i, (i * 2) % PresetColors.Length);
            }
            if (GUILayout.Button("R")) RequestSetAllPreset(0);
            if (GUILayout.Button("V")) RequestSetAllPreset(1);
            if (GUILayout.Button("B")) RequestSetAllPreset(2);
            if (GUILayout.Button("Off")) RequestSetAllPreset(999);
            GUILayout.EndHorizontal();

            bool beatCycle = _beatColorCycle;
            bool newBeatCycle = GUILayout.Toggle(beatCycle, "Couleurs au beat");
            if (newBeatCycle != beatCycle)
                RequestBeatColorCycle(newBeatCycle);

            int target = _colorTargetHead >= 0 ? _colorTargetHead : 0;
            int idxTarget = FirstHeadIndex + target;
            var ts = _states[idxTarget];
            GUILayout.BeginHorizontal();
            GUILayout.Label($"R{ts.color.r}", GUILayout.Width(36));
            float r = GUILayout.HorizontalSlider(ts.color.r, 0, 255);
            GUILayout.Label($"G{ts.color.g}", GUILayout.Width(36));
            float g = GUILayout.HorizontalSlider(ts.color.g, 0, 255);
            GUILayout.Label($"B{ts.color.b}", GUILayout.Width(36));
            float b = GUILayout.HorizontalSlider(ts.color.b, 0, 255);
            GUILayout.EndHorizontal();
            if (Mathf.RoundToInt(r) != ts.color.r || Mathf.RoundToInt(g) != ts.color.g || Mathf.RoundToInt(b) != ts.color.b)
                RequestSetRgb(target, (byte)r, (byte)g, (byte)b);

            GUILayout.EndArea();
        }

        private void SetAllHeadsColor(Color32 c)
        {
            for (int i = 0; i < HeadCount; i++)
            {
                var s = _states[FirstHeadIndex + i];
                ApplyColor(ref s, c);
                _states[FirstHeadIndex + i] = s;
            }
        }

        private void DrawSimulator()
        {
            const int size = 48;
            const int gap = 10;
            // Sous l'aperçu LED (haut-droite), bien séparé des autres panneaux.
            const int previewMax = 420;
            int totalW = HeadCount * size + (HeadCount - 1) * gap;
            float x0 = Screen.width - totalW - 16f;
            float y0 = 10f + 28f + previewMax + 10f;

            GUI.Box(new Rect(x0 - 8, y0 - 8, totalW + 16, size + 48), "Simulateur lyres");

            for (int i = 0; i < HeadCount; i++)
            {
                var s = _states[FirstHeadIndex + i];
                float x = x0 + i * (size + gap);
                var r = new Rect(x, y0 + 8, size, size);

                var bg = new Color(0.08f, 0.08f, 0.12f, 0.92f);
                GUI.color = bg;
                GUI.DrawTexture(r, Texture2D.whiteTexture);

                GUI.color = new Color(s.color.r / 255f, s.color.g / 255f, s.color.b / 255f, s.dimmer / 255f);
                var beam = new Rect(r.x + 12, r.y + 12, 24, 24);
                GUI.DrawTexture(beam, Texture2D.whiteTexture);

                float ang = (s.pan / 255f) * Mathf.PI * 2f;
                float cx = r.x + r.width * 0.5f;
                float cy = r.y + r.height * 0.5f;
                float lx = cx + Mathf.Cos(ang) * 16f;
                float ly = cy + Mathf.Sin(ang) * 16f;
                DrawLine(new Vector2(cx, cy), new Vector2(lx, ly), 2f, Color.white);

                GUI.color = Color.white;
                GUI.Label(new Rect(x, y0 + size + 8, size, 18), $"L{i + 1}", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
            }

            GUI.color = Color.white;
        }

        private static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
        {
            var saved = GUI.color;
            GUI.color = color;
            var dir = b - a;
            float len = dir.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var rect = new Rect(a.x, a.y - width * 0.5f, len, width);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = saved;
        }
    }
}
