using UnityEngine;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Pilotage des projecteurs DMX (mur LAPS) + simulateur visuel.
    /// - Rotation uniquement via les flèches (pan/tilt manuel)
    /// - Option F3 : effets lumière au beat (sans rotation auto)
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

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
                _show = !_show;
            if (Input.GetKeyDown(KeyCode.F3))
                _nightclubMode = !_nightclubMode;

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
        }

        /// <summary>
        /// 1) Ctrl+1..4 = sélectionner la lyre à recolorer
        /// 2) R/G/B/M/J/C/W/0 = appliquer la couleur à la lyre sélectionnée
        /// </summary>
        private void HandleColorHotkeys()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) _colorTargetHead = 0;
                else if (Input.GetKeyDown(KeyCode.Alpha2)) _colorTargetHead = 1;
                else if (Input.GetKeyDown(KeyCode.Alpha3)) _colorTargetHead = 2;
                else if (Input.GetKeyDown(KeyCode.Alpha4)) _colorTargetHead = 3;
            }

            if (_colorTargetHead < 0) return;

            int idx = FirstHeadIndex + _colorTargetHead;
            var s = _states[idx];
            bool changed = false;

            if (Input.GetKeyDown(KeyCode.R)) { ApplyColor(ref s, PresetColors[0]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.G)) { ApplyColor(ref s, PresetColors[1]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.B)) { ApplyColor(ref s, PresetColors[2]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.M)) { ApplyColor(ref s, PresetColors[3]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.J)) { ApplyColor(ref s, PresetColors[4]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.C)) { ApplyColor(ref s, PresetColors[6]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.W)) { ApplyColor(ref s, PresetColors[7]); changed = true; }
            else if (Input.GetKeyDown(KeyCode.Alpha0)) { ApplyColor(ref s, new Color32(0, 0, 0, 255)); changed = true; }

            if (changed)
                _states[idx] = s;
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

                // Pan/tilt : uniquement les flèches, jamais modifié ici
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

            const int w = 440;
            const int h = 200;
            var rect = new Rect(12, Screen.height - h - 12, w, h);
            GUI.Box(rect, "Projecteurs — Flèches = pan/tilt | Ctrl+1..4 = lyre");

            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 24, rect.width - 20, rect.height - 34));

            string pauseLabel = GlobalPause.IsPaused ? " [PAUSE]" : "";
            GUILayout.Label($"Lyre {_colorTargetHead + 1} | Flèches = rotation | F3 effets beat{pauseLabel}");

            GUILayout.BeginHorizontal();
            for (int i = 0; i < HeadCount; i++)
            {
                var s = _states[FirstHeadIndex + i];
                if (GUILayout.Button($"Lyre {i + 1}"))
                    ApplyColor(ref _states[FirstHeadIndex + i], PresetColors[(i * 2) % PresetColors.Length]);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tous Rouge")) SetAllHeadsColor(PresetColors[0]);
            if (GUILayout.Button("Tous Vert")) SetAllHeadsColor(PresetColors[1]);
            if (GUILayout.Button("Tous Bleu")) SetAllHeadsColor(PresetColors[2]);
            if (GUILayout.Button("Tous Off")) SetAllHeadsColor(new Color32(0, 0, 0, 255));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            _beatColorCycle = GUILayout.Toggle(_beatColorCycle, "Couleurs qui changent au beat");
            GUILayout.EndHorizontal();

            // Sliders couleur (pour faire varier précisément, sans casser les mouvements)
            GUILayout.Space(4);
            int target = _colorTargetHead >= 0 ? _colorTargetHead : 0;
            int idxTarget = FirstHeadIndex + target;
            var ts = _states[idxTarget];
            GUILayout.Label($"Sliders couleur — Lyre {target + 1} (Ctrl+{target + 1} pour changer la cible)");
            float r = ts.color.r;
            float g = ts.color.g;
            float b = ts.color.b;
            GUILayout.Label($"R {Mathf.RoundToInt(r)}");
            r = GUILayout.HorizontalSlider(r, 0, 255);
            GUILayout.Label($"G {Mathf.RoundToInt(g)}");
            g = GUILayout.HorizontalSlider(g, 0, 255);
            GUILayout.Label($"B {Mathf.RoundToInt(b)}");
            b = GUILayout.HorizontalSlider(b, 0, 255);
            ts.color = new Color32((byte)r, (byte)g, (byte)b, 255);
            if (ts.dimmer < 80f && (r + g + b) > 3f) ts.dimmer = 255;
            _states[idxTarget] = ts;

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
            const int size = 54;
            const int gap = 12;
            int totalW = HeadCount * size + (HeadCount - 1) * gap;
            float x0 = Screen.width - totalW - 16f;
            float y0 = 16f;

            GUI.Box(new Rect(x0 - 8, y0 - 8, totalW + 16, size + 56), "Simulateur lyres");

            for (int i = 0; i < HeadCount; i++)
            {
                var s = _states[FirstHeadIndex + i];
                float x = x0 + i * (size + gap);
                var r = new Rect(x, y0 + 8, size, size);

                var bg = new Color(0.08f, 0.08f, 0.12f, 0.92f);
                GUI.color = bg;
                GUI.DrawTexture(r, Texture2D.whiteTexture);

                // Faisceau (couleur lyre)
                GUI.color = new Color(s.color.r / 255f, s.color.g / 255f, s.color.b / 255f, s.dimmer / 255f);
                var beam = new Rect(r.x + 14, r.y + 14, 26, 26);
                GUI.DrawTexture(beam, Texture2D.whiteTexture);

                // Indicateur pan (rotation)
                float ang = (s.pan / 255f) * Mathf.PI * 2f;
                float cx = r.x + r.width * 0.5f;
                float cy = r.y + r.height * 0.5f;
                float lx = cx + Mathf.Cos(ang) * 18f;
                float ly = cy + Mathf.Sin(ang) * 18f;
                DrawLine(new Vector2(cx, cy), new Vector2(lx, ly), 2f, Color.white);

                GUI.color = Color.white;
                GUI.Label(new Rect(x, y0 + size + 10, size, 18), $"L{i + 1}", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 });
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
