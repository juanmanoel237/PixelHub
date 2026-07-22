using UnityEngine;
using UnityEngine.Playables;
using Laps.Core;

namespace Laps.Authoring
{
    /// <summary>
    /// Fournisseur d'état audio-réactif : analyse la musique (FFT) et génère un effet
    /// "pump + explosion" (anneau) synchronisé sur les kicks (basses).
    ///
    /// Objectif : reproduire un effet "ça pompe / ça explose" et l'envoyer vers le LED (Art-Net).
    /// </summary>
    public class AudioReactiveProvider : MonoBehaviour, IStateProvider
    {
        public enum VisualEffect
        {
            /// <summary>Style 1 — égaliseur radial propre (cyan/magenta).</summary>
            NeonRadial = 0,
            /// <summary>Style 2 — voiles aurora fluides (bleu/violet).</summary>
            SpiralFlow = 1,
            /// <summary>Style 3 — anneaux horizon élégants (or/cyan).</summary>
            PulseRings = 2,
            ContinentIntro = 3
        }
        [Header("Audio")]
        [Tooltip("AudioSource qui joue la musique (idéalement celui piloté par Timeline). Si vide, on auto-découvre un AudioSource en lecture.")]
        [SerializeField] private AudioSource _audioSource;
        [Tooltip("Si aucun AudioSource n'est trouvé, analyse le mix global (AudioListener). Recommandé avec Timeline.")]
        [SerializeField] private bool _useAudioListenerWhenNoSource = true;
        [Tooltip("Relance l'AudioSource seulement s'il a un clip propre (pas via Timeline).")]
        [SerializeField] private bool _autoPlayAudioSource = false;

        [Header("Changement d'effets (show)")]
        [SerializeField] private bool _autoCycleEffects = true;
        [SerializeField, Min(2f)] private float _effectCycleSeconds = 16f;
        [SerializeField, Range(0f, 1f)] private float _effectCrossfade = 0.35f;
        [SerializeField] private VisualEffect[] _effects = new[] { VisualEffect.NeonRadial, VisualEffect.SpiralFlow, VisualEffect.PulseRings };

        [Header("Intro — BIENVENUE AU CONTINENT")]
        [SerializeField] private bool _playIntroFirst = false;
        [SerializeField] private string _introLine1 = "bienvenue";
        [SerializeField] private string _introLine2 = "au continent";
        [SerializeField, Range(8, 20)] private int _introHeroScale = 13;
        [SerializeField, Range(2, 6)] private int _introPhraseScale = 4;
        [SerializeField, Min(2f)] private float _introHoldSeconds = 8f;
        [SerializeField] private float _introLetterHold = 1.1f;
        [SerializeField] private float _introLetterEnter = 0.5f;
        [SerializeField] private float _introLetterExit = 0.45f;
        [SerializeField, Range(8, 64)] private int _introEqBars = 36;

        [Header("FFT")]
        [SerializeField] private int _fftSize = 1024;
        [SerializeField] private FFTWindow _fftWindow = FFTWindow.BlackmanHarris;

        [Header("Basses / kicks")]
        [Tooltip("Bande de bins pour les basses (indices FFT).")]
        [SerializeField] private int _bassBinStart = 1;
        [SerializeField] private int _bassBinEnd = 20;
        [SerializeField] private float _bassGain = 120f;
        [Tooltip("Attack (montée) : plus petit = monte plus vite.")]
        [SerializeField] private float _bassAttack = 0.08f;
        [Tooltip("Release (descente) : plus petit = descend plus vite.")]
        [SerializeField] private float _bassRelease = 0.18f;
        [SerializeField] private float _kickThreshold = 0.35f;
        [SerializeField] private float _kickCooldownSeconds = 0.12f;

        [Header("Aigus (visualisation)")]
        [Tooltip("Bande de bins pour les aigus (indices FFT).")]
        [SerializeField] private int _highBinStart = 80;
        [SerializeField] private int _highBinEnd = 220;
        [SerializeField] private float _highGain = 65f;
        [SerializeField] private float _highAttack = 0.06f;
        [SerializeField] private float _highRelease = 0.16f;

        [Header("Voix / beat (synchro \"au morceau\")")]
        [Tooltip("Bande de bins pour la présence vocale (médiums).")]
        [SerializeField] private int _voiceBinStart = 35;
        [SerializeField] private int _voiceBinEnd = 140;
        [SerializeField] private float _voiceGain = 55f;
        [SerializeField] private float _voiceAttack = 0.06f;
        [SerializeField] private float _voiceRelease = 0.20f;
        [Tooltip("Seuil d'activité vocale (0..1).")]
        [SerializeField, Range(0f, 1f)] private float _voiceThreshold = 0.18f;

        [Tooltip("Détection de beat/onset via flux spectral (attaques).")]
        [SerializeField, Range(0f, 1f)] private float _onsetThreshold = 0.12f;
        [SerializeField] private float _onsetCooldownSeconds = 0.10f;

        [Header("Couleurs")]
        [SerializeField] private Color _baseColor = new Color(0.0f, 0.47f, 1.0f); // bleu
        [SerializeField] private Color _kickColor = Color.white;
        [Tooltip("Luminosité minimale même sans basses (évite un écran tout noir).")]
        [SerializeField] private float _ambientMin = 0.22f;

        [Header("Style global (3 presets premium)")]
        [Tooltip("Fond noir (recommandé sur mur LED). 'Transparent' n'existe pas sur des LEDs : le noir = éteint.")]
        [SerializeField] private bool _blackBackground = true;
        [SerializeField] private Color _bgCenter = new Color(0.015f, 0.015f, 0.045f);
        [SerializeField] private Color _bgEdge = new Color(0.03f, 0.02f, 0.08f);
        [SerializeField, Range(0.5f, 1f)] private float _neonSaturation = 0.82f;
        [SerializeField, Range(0.7f, 1.1f)] private float _neonValue = 0.98f;
        [SerializeField] private float _hueSpeed = 0.045f;

        [Header("Effet anneau (explosion)")]
        [SerializeField] private float _ringSpeed = 0.9f;       // pixels par seconde (en unités normalisées)
        [SerializeField] private float _ringThickness = 0.06f;  // épaisseur relative
        [SerializeField] private float _ringFade = 1.35f;       // vitesse de fade

        [Header("Style 1 — Prism Ring")]
        [SerializeField, Range(96, 320)] private int _spokeCount = 200;
        [SerializeField, Range(0.15f, 0.55f)] private float _baseRadius = 0.30f;
        [SerializeField, Range(0.05f, 0.45f)] private float _barMaxLength = 0.26f;
        [SerializeField, Range(0.002f, 0.04f)] private float _barThickness = 0.014f;
        [SerializeField, Range(0f, 1f)] private float _innerWaveAmount = 0.35f;
        [SerializeField] private float _innerWaveFreq = 5f;
        [SerializeField] private float _innerWaveSpeed = 0.85f;
        [SerializeField, Range(0.002f, 0.06f)] private float _innerRingThickness = 0.016f;

        [Header("FFT égaliseur")]
        [SerializeField] private int _vizBinStart = 2;
        [SerializeField] private int _vizBinEnd = 420;
        [SerializeField] private float _vizGain = 140f;
        [SerializeField] private float _vizAttack = 0.07f;
        [SerializeField] private float _vizRelease = 0.18f;
        [Header("Kick (basses)")]
        [Tooltip("Flash global sur kick (peut paraître blanc/clignotant). Désactive-le si tu veux un rendu plus \"propre\".")]
        [SerializeField] private bool _enableKickFlash = false;
        [SerializeField, Range(0f, 2f)] private float _kickFlash = 0.9f;

        private float[] _spectrum;
        private Color32[] _state;
        private LyreState[] _emptyLyres = new LyreState[0];
        private float[] _spokes;
        private float[] _spokesSmooth;

        private int _w;
        private int _h;

        private float _bassSmooth;
        private float _highSmooth;
        private float _voiceSmooth;
        private float _onsetSmooth;
        private float _lastOnsetTime = -999f;
        private float _lastKickDspTime = -999f;
        private float _lastKickStrength;
        private bool _warnedNoAudio;
        private int _effectIndex;
        private float _lastEffectSwitchTime;
        private int _introRevealedChars;
        private float _introLetterReveal;
        private float _introIdleTimer;
        private float _introFullPhraseUntil;
        private bool _introFinished;
        private float _introLetterBurst;
        private char[] _introLetters;
        private int _heroLetterIndex;
        private float _heroLetterStartTime;
        private bool _introFullPhrase;

        /// <summary>Valeur basses lissée 0..1 (pour debug overlay).</summary>
        public float BassSmooth => _bassSmooth;
        /// <summary>Valeur aigus lissée 0..1 (pour debug overlay).</summary>
        public float HighSmooth => _highSmooth;
        /// <summary>Présence vocale (médiums) 0..1.</summary>
        public float VoiceSmooth => _voiceSmooth;
        /// <summary>Onset/beat (attaques) 0..1.</summary>
        public float OnsetSmooth => _onsetSmooth;
        public bool IsOnset { get; private set; }
        public bool IsVoiceActive => _voiceSmooth >= _voiceThreshold;
        public bool HasAudioSource => _audioSource != null;
        public bool HasAudioInput =>
            (_useAudioListenerWhenNoSource && !AudioListener.pause) ||
            (_audioSource != null && _audioSource.isPlaying);
        public VisualEffect CurrentEffect => (_playIntroFirst && !_introFinished)
            ? VisualEffect.ContinentIntro
            : ((_effects != null && _effects.Length > 0)
                ? _effects[Mathf.Clamp(_effectIndex, 0, _effects.Length - 1)]
                : VisualEffect.NeonRadial);

        public string CurrentEffectLabel => CurrentEffect switch
        {
            VisualEffect.NeonRadial => "Prism Ring",
            VisualEffect.SpiralFlow => "Aurora Veil",
            VisualEffect.PulseRings => "Horizon Pulse",
            VisualEffect.ContinentIntro => "Intro",
            _ => "—"
        };
        public bool IsIntroPlaying => _playIntroFirst && !_introFinished;
        public int IntroRevealedChars => _introFullPhrase ? IntroTotalChars : _heroLetterIndex;
        public int IntroTotalChars => _introLetters?.Length ?? 0;
        public char IntroCurrentLetter => (_introLetters != null && _heroLetterIndex < _introLetters.Length)
            ? _introLetters[_heroLetterIndex]
            : ' ';
        public bool IsIntroFullPhrase => _introFullPhrase;

        public Color32[] GetState() => _state;
        public LyreState[] GetLyreStates() => _emptyLyres;

        /// <summary>Branche l'AudioSource de la Timeline (ShowDirector).</summary>
        public void SetAudioSource(AudioSource source)
        {
            _audioSource = source;
            if (source != null)
                Debug.Log($"[AudioReactive] AudioSource lié : {source.gameObject.name}");
        }

        /// <summary>Relance l'intro lettre par lettre (ex: en appuyant sur A).</summary>
        public void ResetIntro()
        {
            RebuildIntroLetters();
            _introRevealedChars = 0;
            _introLetterReveal = 0f;
            _introIdleTimer = 0f;
            _introFullPhraseUntil = 0f;
            _introFinished = false;
            _introLetterBurst = 0f;
            _heroLetterIndex = 0;
            _heroLetterStartTime = Time.time;
            _introFullPhrase = false;
            _lastEffectSwitchTime = Time.time;
        }

        private void Awake()
        {
            ConfigManager.OnConfigReloaded += OnConfigReloaded;
            if (ConfigManager.Config != null) OnConfigReloaded();
            EnsureBuffers();
        }

        private void OnDestroy()
        {
            ConfigManager.OnConfigReloaded -= OnConfigReloaded;
        }

        private void OnConfigReloaded()
        {
            _w = Mathf.Max(1, ConfigManager.Config.mapping.screenWidth);
            _h = Mathf.Max(1, ConfigManager.Config.mapping.screenHeight);
            _state = new Color32[ConfigManager.Config.mapping.ledCount];
            RebuildIntroLetters();
        }

        private void RebuildIntroLetters()
        {
            string full = $"{_introLine1} {_introLine2}".ToLowerInvariant();
            var list = new System.Collections.Generic.List<char>();
            foreach (char c in full)
            {
                if (c != ' ') list.Add(c);
            }
            _introLetters = list.ToArray();
        }

        private void EnsureBuffers()
        {
            _fftSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(_fftSize, 64, 8192));
            _spectrum = new float[_fftSize];
            _spokes = new float[_spokeCount];
            _spokesSmooth = new float[_spokeCount];
            _prevSpectrum = new float[_fftSize];
            _output = new float[256];
        }

        private float[] _prevSpectrum;
        private float[] _output;

        private void Update()
        {
            if (_state == null || _state.Length == 0) return;

            if (_audioSource == null)
                TryFindAudioSource();

            float bass = 0f;
            float high = 0f;
            float voice = 0f;
            bool gotSpectrum = false;

            // La musique vient de la Timeline (une seule source). On analyse le mix global.
            if (_useAudioListenerWhenNoSource && AudioListener.pause == false)
            {
                AudioListener.GetSpectrumData(_spectrum, 0, _fftWindow);
                bass = ComputeBandAverage(_spectrum, _bassBinStart, _bassBinEnd) * _bassGain;
                high = ComputeBandAverage(_spectrum, _highBinStart, _highBinEnd) * _highGain;
                voice = ComputeBandAverage(_spectrum, _voiceBinStart, _voiceBinEnd) * _voiceGain;
                bass = Mathf.Sqrt(bass);
                high = Mathf.Sqrt(high);
                voice = Mathf.Sqrt(voice);
                gotSpectrum = bass + high + voice > 0.0001f;
            }

            if (!gotSpectrum && _audioSource != null)
            {
                if (_autoPlayAudioSource && !_audioSource.isPlaying && _audioSource.clip != null)
                    _audioSource.Play();

                if (_audioSource.isPlaying)
                {
                    _audioSource.GetSpectrumData(_spectrum, 0, _fftWindow);
                    bass = ComputeBandAverage(_spectrum, _bassBinStart, _bassBinEnd) * _bassGain;
                    high = ComputeBandAverage(_spectrum, _highBinStart, _highBinEnd) * _highGain;
                    voice = ComputeBandAverage(_spectrum, _voiceBinStart, _voiceBinEnd) * _voiceGain;
                    bass = Mathf.Sqrt(bass);
                    high = Mathf.Sqrt(high);
                    voice = Mathf.Sqrt(voice);
                    gotSpectrum = true;
                }
            }

            if (!gotSpectrum && _audioSource == null && !_useAudioListenerWhenNoSource && !_warnedNoAudio)
            {
                _warnedNoAudio = true;
                Debug.LogWarning(
                    "[AudioReactive] Aucun AudioSource trouvé. Ajoute un AudioSource sur ShowDirector " +
                    "ou assigne-le dans l'Inspector. Mode démo (pulse) actif.");
            }

            bass = Mathf.Clamp01(bass);
            high = Mathf.Clamp01(high);
            voice = Mathf.Clamp01(voice);

            // Lissage attack/release
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float aBass = (bass > _bassSmooth) ? _bassAttack : _bassRelease;
            float kBass = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, aBass));
            _bassSmooth = Mathf.Lerp(_bassSmooth, bass, kBass);

            float aHigh = (high > _highSmooth) ? _highAttack : _highRelease;
            float kHigh = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, aHigh));
            _highSmooth = Mathf.Lerp(_highSmooth, high, kHigh);

            float aVoice = (voice > _voiceSmooth) ? _voiceAttack : _voiceRelease;
            float kVoice = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, aVoice));
            _voiceSmooth = Mathf.Lerp(_voiceSmooth, voice, kVoice);

            // Onset detection (spectral flux)
            float flux = 0f;
            int start = Mathf.Clamp(_vizBinStart, 0, _spectrum.Length - 1);
            int end = Mathf.Clamp(_vizBinEnd, start, _spectrum.Length - 1);
            for (int i = start; i <= end; i++)
            {
                float d = _spectrum[i] - _prevSpectrum[i];
                if (d > 0f) flux += d;
                _prevSpectrum[i] = _spectrum[i];
            }
            flux = Mathf.Clamp01(Mathf.Sqrt(flux * 8f)); // compress/normalize
            float aOn = (flux > _onsetSmooth) ? 0.04f : 0.18f;
            float kOn = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, aOn));
            _onsetSmooth = Mathf.Lerp(_onsetSmooth, flux, kOn);

            IsOnset = false;
            if ((Time.time - _lastOnsetTime) >= _onsetCooldownSeconds && _onsetSmooth >= _onsetThreshold)
            {
                IsOnset = true;
                _lastOnsetTime = Time.time;
            }

            // Détection kick (avec cooldown) basée sur l'horloge audio (DSP)
            double dsp = AudioSettings.dspTime;
            bool canKick = (dsp - _lastKickDspTime) > _kickCooldownSeconds;
            bool isKick = canKick && _bassSmooth >= _kickThreshold;
            if (isKick)
            {
                _lastKickDspTime = (float)dsp;
                _lastKickStrength = _bassSmooth;
            }

            // Sans audio : pulse de démo pour voir que l'aperçu fonctionne
            float renderBass = _bassSmooth;
            if (_audioSource == null && !_useAudioListenerWhenNoSource)
                renderBass = (Mathf.Sin(Time.time * 6f) * 0.5f + 0.5f) * 0.6f;

            UpdateVisualizerSpokes(dt);
            UpdateIntro(isKick || IsOnset, dt);
            if (_playIntroFirst && !_introFinished)
                RenderContinentIntro(renderBass, _highSmooth, isKick ? _bassSmooth : 0f);
            else
            {
                UpdateEffectCycling();
                RenderShow(renderBass, _highSmooth, (float)dsp, isKick ? _bassSmooth : 0f);
            }
        }

        private void UpdateIntro(bool isKick, float dt)
        {
            if (!_playIntroFirst || _introFinished) return;

            if (_introFullPhrase)
            {
                if (_introFullPhraseUntil <= 0.001f)
                    _introFullPhraseUntil = Time.time + _introHoldSeconds;
                if (Time.time >= _introFullPhraseUntil)
                    _introFinished = true;
                return;
            }

            int total = IntroTotalChars;
            if (total == 0)
            {
                _introFullPhrase = true;
                return;
            }

            if (_heroLetterIndex >= total)
            {
                _introFullPhrase = true;
                _introLetterBurst = 1f;
                return;
            }

            float elapsed = Time.time - _heroLetterStartTime;
            float enter = _introLetterEnter;
            float hold = _introLetterHold;
            float exit = _introLetterExit;

            // Kick pendant le hold → passage à la sortie
            if (isKick && elapsed >= enter && elapsed < enter + hold)
                _heroLetterStartTime = Time.time - (enter + hold);

            if (elapsed >= enter + hold + exit)
            {
                _heroLetterIndex++;
                _heroLetterStartTime = Time.time;
                _introLetterBurst = 1f;
                if (_heroLetterIndex >= total)
                    _introFullPhrase = true;
            }

            _introIdleTimer += dt;
            if (isKick) _introLetterBurst = 1f;
            _introLetterBurst = Mathf.MoveTowards(_introLetterBurst, 0f, dt * 2.2f);

            // Anim reveal pour la lettre en cours
            if (elapsed < enter)
                _introLetterReveal = Mathf.Clamp01(elapsed / enter);
            else if (elapsed < enter + hold)
                _introLetterReveal = 1f;
            else
            {
                float t = (elapsed - enter - hold) / Mathf.Max(0.001f, exit);
                _introLetterReveal = 1f - Mathf.Clamp01(t);
            }
        }

        private void UpdateEffectCycling()
        {
            if (!_autoCycleEffects) return;
            if (_effects == null || _effects.Length == 0) return;
            if (_effectCycleSeconds <= 0.01f) return;

            float now = Time.time;
            if (_lastEffectSwitchTime <= 0.001f) _lastEffectSwitchTime = now;
            if ((now - _lastEffectSwitchTime) >= _effectCycleSeconds)
            {
                _lastEffectSwitchTime = now;
                _effectIndex = (_effectIndex + 1) % _effects.Length;
            }
        }

        private void RenderShow(float bass01, float high01, float dspTime, float kickStrength)
        {
            if (_effects == null || _effects.Length == 0)
            {
                RenderNeonVisualizer(bass01, high01, dspTime, kickStrength);
                return;
            }

            int a = Mathf.Clamp(_effectIndex, 0, _effects.Length - 1);
            int b = (a + 1) % _effects.Length;

            float blend = 0f;
            if (_autoCycleEffects && _effectCrossfade > 0.001f)
            {
                float segT = Mathf.Clamp01((Time.time - _lastEffectSwitchTime) / Mathf.Max(0.001f, _effectCycleSeconds));
                float w = Mathf.Clamp01(_effectCrossfade);
                blend = Mathf.Clamp01((w - segT) / Mathf.Max(0.0001f, w));
            }

            RenderEffect(_effects[a], bass01, high01, dspTime, kickStrength);
            if (blend <= 0.001f) return;

            // second buffer for crossfade (only during window)
            var tmp = new Color32[_state.Length];
            var save = _state;
            _state = tmp;
            RenderEffect(_effects[b], bass01, high01, dspTime, kickStrength);
            _state = save;

            float t = blend;
            for (int i = 0; i < save.Length; i++)
            {
                Color ca = save[i];
                Color cb = tmp[i];
                save[i] = (Color32)Color.Lerp(ca, cb, t);
            }
        }

        private void RenderEffect(VisualEffect effect, float bass01, float high01, float dspTime, float kickStrength)
        {
            switch (effect)
            {
                case VisualEffect.SpiralFlow:
                    RenderSpiralFlow(bass01, high01, dspTime, kickStrength);
                    break;
                case VisualEffect.PulseRings:
                    RenderPulseRings(bass01, high01, dspTime, kickStrength);
                    break;
                case VisualEffect.ContinentIntro:
                    RenderContinentIntro(bass01, high01, kickStrength);
                    break;
                case VisualEffect.NeonRadial:
                default:
                    RenderNeonVisualizer(bass01, high01, dspTime, kickStrength);
                    break;
            }
        }

        private void UpdateVisualizerSpokes(float dt)
        {
            if (_spectrum == null || _spectrum.Length == 0) return;

            if (_spokes == null || _spokes.Length != _spokeCount)
            {
                _spokes = new float[_spokeCount];
                _spokesSmooth = new float[_spokeCount];
            }

            int start = Mathf.Clamp(_vizBinStart, 0, _spectrum.Length - 1);
            int end = Mathf.Clamp(_vizBinEnd, start, _spectrum.Length - 1);
            int span = Mathf.Max(1, end - start);

            for (int i = 0; i < _spokeCount; i++)
            {
                // Mapping bin ~ log-ish (donne plus de détails dans les basses)
                float t = (float)i / Mathf.Max(1, _spokeCount - 1);
                float binF = start + Mathf.Pow(t, 1.85f) * span;
                int bin = Mathf.Clamp(Mathf.RoundToInt(binF), start, end);

                float v = _spectrum[bin] * _vizGain;
                v = Mathf.Sqrt(Mathf.Max(0f, v));
                v = Mathf.Clamp01(v);

                float a = (v > _spokesSmooth[i]) ? _vizAttack : _vizRelease;
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.001f, a));
                _spokesSmooth[i] = Mathf.Lerp(_spokesSmooth[i], v, k);
                _spokes[i] = _spokesSmooth[i];
            }
        }

        private void TryFindAudioSource()
        {
            var director = FindObjectOfType<PlayableDirector>();
            if (director != null)
            {
                var src = director.GetComponent<AudioSource>();
                if (src != null)
                {
                    SetAudioSource(src);
                    return;
                }
            }

            var sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var s in sources)
            {
                if (s != null && (s.isPlaying || s.clip != null))
                {
                    SetAudioSource(s);
                    return;
                }
            }
        }

        private static float ComputeBandAverage(float[] spectrum, int start, int end)
        {
            if (spectrum == null || spectrum.Length == 0) return 0f;
            start = Mathf.Clamp(start, 0, spectrum.Length - 1);
            end = Mathf.Clamp(end, start, spectrum.Length - 1);
            float sum = 0f;
            int count = 0;
            for (int i = start; i <= end; i++)
            {
                sum += spectrum[i];
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        private struct StylePalette
        {
            public float HueA;
            public float HueB;
            public float Sat;
            public float Val;
        }

        private static StylePalette GetStylePalette(VisualEffect fx) => fx switch
        {
            VisualEffect.SpiralFlow => new StylePalette { HueA = 0.56f, HueB = 0.70f, Sat = 0.70f, Val = 0.94f },
            VisualEffect.PulseRings => new StylePalette { HueA = 0.07f, HueB = 0.53f, Sat = 0.58f, Val = 0.95f },
            _ => new StylePalette { HueA = 0.52f, HueB = 0.86f, Sat = 0.78f, Val = 0.97f },
        };

        private static Color HueGradient(float hueA, float hueB, float t, float sat, float val)
        {
            float h = Mathf.Lerp(hueA, hueB, Mathf.Clamp01(t));
            return Color.HSVToRGB(Mathf.Repeat(h, 1f), sat, val);
        }

        private void FillPremiumBackground(float cx, float cy, float invHalf, float pump)
        {
            if (_blackBackground)
            {
                // Noir pur = LEDs éteintes (équivalent visuel d'un "fond transparent")
                for (int i = 0; i < _state.Length; i++)
                    _state[i] = new Color32(0, 0, 0, 255);
                return;
            }

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int idx = y * _w + x;
                    if (idx >= _state.Length) continue;
                    float dx = (x - cx) * invHalf;
                    float dy = (y - cy) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color bg = Color.Lerp(_bgCenter, _bgEdge, Mathf.Clamp01(r * 1.1f));
                    bg *= Mathf.Lerp(0.90f, 1.03f, pump * 0.4f);
                    _state[idx] = (Color32)bg;
                }
            }
        }

        /// <summary>Style 1 — Prism Ring : égaliseur radial net, anneau fin, dégradé cyan→magenta.</summary>
        private void RenderNeonVisualizer(float bass01, float high01, float dspTime, float kickStrength)
        {
            var pal = GetStylePalette(VisualEffect.NeonRadial);
            float half = Mathf.Max(_w, _h) * 0.5f;
            float cx = (_w - 1) * 0.5f;
            float cy = (_h - 1) * 0.35f; // Décalé vers le haut
            float invHalf = 1f / Mathf.Max(1f, half);
            float time = Time.time;
            float pump = Mathf.Pow(bass01, 0.5f);
            float hueDrift = time * _hueSpeed * 0.4f;

            FillPremiumBackground(cx, cy, invHalf, pump);

            float kickAge = (float)(dspTime - _lastKickDspTime);
            float kickRing = 0f;
            float kickR = 0f;
            if (kickAge >= 0f && kickAge < 1.8f)
            {
                kickR = kickAge * (_ringSpeed * 0.75f);
                kickRing = Mathf.Clamp01(_lastKickStrength * Mathf.Exp(-kickAge * 1.6f)) * 0.55f;
            }

            float baseR = _baseRadius;
            float barMax = _barMaxLength * (0.8f + 0.45f * pump);
            float thick = _barThickness;
            float innerR = Mathf.Max(0.08f, baseR - 0.06f);
            float voiceBoost = IsVoiceActive ? 0.18f : 0f;

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int idx = y * _w + x;
                    if (idx >= _state.Length) continue;

                    float dx = (x - cx) * invHalf;
                    float dy = (y - cy) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float ang01 = (ang + Mathf.PI) * (0.5f / Mathf.PI);
                    Color outC = _state[idx];

                    // Anneau interne lisse (pas trop ondulé)
                    if (_innerWaveAmount > 0.001f)
                    {
                        float wave = Mathf.Sin(ang * _innerWaveFreq + time * _innerWaveSpeed) * 0.5f;
                        float waveR = innerR + (0.012f + 0.025f * high01) * wave * _innerWaveAmount;
                        float d = Mathf.Abs(r - waveR);
                        float a = Mathf.SmoothStep(1f, 0f, d / Mathf.Max(0.0001f, _innerRingThickness));
                        if (a > 0f)
                        {
                            Color c = Color.Lerp(Color.red, Color.white, Mathf.PingPong(ang01 * 2f + time * 0.3f, 1f));
                            outC = Color.Lerp(outC, c, a * (0.35f + 0.4f * high01 + voiceBoost));
                        }
                    }

                    // Barres radiales — profil net + glow au bout
                    if (_spokes != null && _spokes.Length > 0)
                    {
                        int spoke = Mathf.Clamp(Mathf.FloorToInt(ang01 * _spokeCount), 0, _spokeCount - 1);
                        float v = _spokes[spoke];
                        float len = v * barMax;
                        float startR = baseR;
                        float endR = baseR + len;
                        if (r >= startR - thick && r <= endR + thick * 1.5f)
                        {
                            float along = len > 0.001f ? Mathf.Clamp01((r - startR) / len) : 0f;
                            float edge = 1f - Mathf.Abs(r - Mathf.Clamp(r, startR, endR)) / Mathf.Max(0.0001f, thick);
                            edge = Mathf.Clamp01(edge);
                            float tipGlow = Mathf.Pow(along, 1.2f);
                            float a = edge * (0.22f + 0.78f * tipGlow) * (0.4f + 0.6f * v);
                            Color c = Color.Lerp(Color.red, Color.white, Mathf.PingPong(ang01 * 2f + time * 0.5f, 1f));
                            outC = Color.Lerp(outC, c, a);
                        }
                    }

                    if (kickRing > 0.001f)
                    {
                        float d = Mathf.Abs(r - kickR);
                        float ring = Mathf.SmoothStep(1f, 0f, d / Mathf.Max(0.0001f, _ringThickness * 0.85f));
                        Color ringC = Color.Lerp(Color.red, Color.white, kickRing);
                        outC = Color.Lerp(outC, ringC, ring * kickRing);
                    }

                    _state[idx] = (Color32)outC;
                }
            }
        }

        /// <summary>Style 2 — Aurora Veil : voiles lumineux doux, bleu/violet, très fluide.</summary>
        private void RenderSpiralFlow(float bass01, float high01, float dspTime, float kickStrength)
        {
            var pal = GetStylePalette(VisualEffect.SpiralFlow);
            float half = Mathf.Max(_w, _h) * 0.5f;
            float cx = (_w - 1) * 0.5f;
            float cy = (_h - 1) * 0.35f; // Décalé vers le haut
            float invHalf = 1f / Mathf.Max(1f, half);
            float time = Time.time;
            float pump = Mathf.Pow(bass01, 0.5f);
            float voice = _voiceSmooth;

            FillPremiumBackground(cx, cy, invHalf, pump);

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int idx = y * _w + x;
                    if (idx >= _state.Length) continue;

                    float dx = (x - cx) * invHalf;
                    float dy = (y - cy) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float ang01 = (ang + Mathf.PI) * (0.5f / Mathf.PI);

                    float v = 0f;
                    if (_spokes != null && _spokes.Length > 0)
                    {
                        int spoke = Mathf.Clamp(Mathf.FloorToInt(ang01 * _spokeCount), 0, _spokeCount - 1);
                        v = _spokes[spoke];
                    }

                    // 3 voiles aurora superposées — doux, pas chaotique
                    float layer1 = 0.5f + 0.5f * Mathf.Sin(ang * 2.2f + r * 8f - time * 0.9f);
                    float layer2 = 0.5f + 0.5f * Mathf.Sin(ang * 3.1f - r * 6f + time * 0.6f);
                    float layer3 = 0.5f + 0.5f * Mathf.Sin(r * 14f - time * 1.1f + ang * 0.5f);
                    float veil = (layer1 * 0.45f + layer2 * 0.35f + layer3 * 0.20f);

                    float band = Mathf.Exp(-Mathf.Pow((r - (0.32f + 0.12f * pump)) / (0.22f + 0.06f * high01), 2f));
                    float a = veil * band * (0.25f + 0.55f * pump + 0.35f * v + 0.25f * voice);
                    a = Mathf.Clamp01(a);

                    float blend = Mathf.PingPong(ang01 * 0.6f + r * 0.25f + time * _hueSpeed, 1f);
                    Color c = Color.Lerp(Color.red, Color.white, blend);
                    Color outC = Color.Lerp(_state[idx], c, a);

                    _state[idx] = (Color32)outC;
                }
            }
        }

        /// <summary>Style 3 — Horizon Pulse : anneaux concentriques élégants or/cyan.</summary>
        private void RenderPulseRings(float bass01, float high01, float dspTime, float kickStrength)
        {
            var pal = GetStylePalette(VisualEffect.PulseRings);
            float half = Mathf.Max(_w, _h) * 0.5f;
            float cx = (_w - 1) * 0.5f;
            float cy = (_h - 1) * 0.35f; // Décalé vers le haut
            float invHalf = 1f / Mathf.Max(1f, half);
            float time = Time.time;
            float pump = Mathf.Pow(bass01, 0.5f);

            FillPremiumBackground(cx, cy, invHalf, pump);

            int ringCount = 4;
            float ringSpeed = 0.22f + 0.28f * pump;
            float thick = 0.012f + 0.022f * Mathf.Max(pump, high01 * 0.5f);

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int idx = y * _w + x;
                    if (idx >= _state.Length) continue;

                    float dx = (x - cx) * invHalf;
                    float dy = (y - cy) * invHalf;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang01 = (Mathf.Atan2(dy, dx) + Mathf.PI) * (0.5f / Mathf.PI);

                    float v = 0f;
                    if (_spokes != null && _spokes.Length > 0)
                    {
                        int spoke = Mathf.Clamp(Mathf.FloorToInt(ang01 * _spokeCount), 0, _spokeCount - 1);
                        v = _spokes[spoke];
                    }

                    float glow = 0f;
                    for (int i = 0; i < ringCount; i++)
                    {
                        float phase = (float)i / ringCount;
                        float rr = Mathf.Repeat(time * ringSpeed + phase, 1f) * 0.68f;
                        float d = Mathf.Abs(r - rr);
                        float ring = Mathf.SmoothStep(1f, 0f, d / Mathf.Max(0.0001f, thick));
                        glow += ring * (0.28f + 0.72f * (0.4f * v + 0.6f * pump));
                    }
                    glow = Mathf.Clamp01(glow * 0.85f);

                    float blend = Mathf.PingPong(ang01 * 0.4f + pump * 0.2f + time * _hueSpeed, 1f);
                    Color c = Color.Lerp(Color.red, Color.white, blend);
                    Color outC = Color.Lerp(_state[idx], c, glow);

                    _state[idx] = (Color32)outC;
                }
            }
        }

        private void RenderContinentIntro(float bass01, float high01, float kickStrength)
        {
            float time = Time.time;
            float pump = Mathf.Pow(bass01, 0.55f);
            float hue = Mathf.Repeat(0.10f + time * 0.06f, 1f);

            // Fond élégant sombre avec léger dégradé radial
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    float dx = (x - _w * 0.5f) / _w;
                    float dy = (y - _h * 0.5f) / _h;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    Color bg = Color.Lerp(new Color(0.02f, 0.02f, 0.06f), new Color(0.04f, 0.02f, 0.1f), r * 1.2f);
                    _state[y * _w + x] = (Color32)bg;
                }
            }

            DrawIntroSpectrumEq(pump, high01);
            DrawHeroRadialEq(pump, high01);

            if (_introFullPhrase)
            {
                // ── Finale : phrase complète premium ──
                float shimmer = 0.88f + 0.12f * Mathf.Sin(time * 3.5f + pump * 5f);
                Color gold = Color.HSVToRGB(Mathf.Repeat(hue + 0.08f, 1f), 0.45f, 1f) * shimmer;
                Color cyan = Color.HSVToRGB(Mathf.Repeat(hue + 0.55f, 1f), 0.6f, 0.95f) * shimmer;

                // Double ligne avec léger décalage couleur
                EffectLibrary.RenderTextTwoLines(
                    _w, _h, _introLine1, _introLine2, _introPhraseScale,
                    (Color32)gold, _state, int.MaxValue, 1f);

                // Halo discret autour du texte
                DrawPhraseHalo(pump, high01, time);
            }
            else if (_introLetters != null && _heroLetterIndex < _introLetters.Length)
            {
                // ── Une lettre géante au centre, puis disparaît ──
                char ch = _introLetters[_heroLetterIndex];
                float scaleF = _introHeroScale * EaseOutBack(_introLetterReveal);
                int scale = Mathf.Max(1, Mathf.RoundToInt(scaleF));
                float alpha = Mathf.Clamp01(_introLetterReveal);

                Color letterCol = Color.HSVToRGB(
                    Mathf.Repeat(hue + _heroLetterIndex * 0.04f, 1f),
                    0.65f + 0.25f * high01,
                    0.85f + 0.15f * pump);
                letterCol *= alpha;

                EffectLibrary.RenderSingleCharCentered(
                    _w, _h, ch, scale, (Color32)letterCol, _state, alpha);

                // Anneau néon autour de la lettre
                DrawLetterRing(pump, high01, alpha);
            }
        }

        private static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private void DrawHeroRadialEq(float pump, float high01)
        {
            int bars = 9;
            int barW = Mathf.Max(3, _w / 36);
            int gap = barW;
            int totalW = bars * (barW + gap) - gap;
            int x0 = (_w - totalW) / 2;
            int yBase = _h / 2 + Mathf.RoundToInt(_h * 0.08f);
            int maxH = Mathf.RoundToInt(_h * 0.22f * (0.65f + 0.55f * pump));
            int segH = Mathf.Max(2, barW - 1);
            int segGap = 2;
            float burst = _introLetterBurst;

            for (int b = 0; b < bars; b++)
            {
                float v = 0.2f + 0.8f * pump;
                if (_spokes != null && _spokes.Length > 0)
                {
                    int si = Mathf.Clamp(b * 4, 0, _spokes.Length - 1);
                    v = Mathf.Clamp01(_spokes[si] + pump * 0.4f);
                }
                if (b == bars / 2) v = Mathf.Clamp01(v + burst * 0.7f);

                int barH = Mathf.RoundToInt(maxH * v);
                int bx = x0 + b * (barW + gap);
                Color pink = new Color(1f, 0.15f, 0.58f);
                Color pinkDim = new Color(0.28f, 0.04f, 0.16f);

                for (int sy = 0; sy < barH; sy += segH + segGap)
                {
                    int segTop = yBase - sy - segH;
                    if (segTop < 0) break;
                    float fade = 1f - (float)sy / Mathf.Max(1, maxH);
                    FillRect(bx, segTop, barW, segH, Color.Lerp(pinkDim, pink, fade));
                }

                int mirrorH = Mathf.RoundToInt(barH * 0.45f);
                for (int sy = 0; sy < mirrorH; sy += segH + segGap)
                {
                    int segTop = yBase + sy;
                    if (segTop + segH >= _h) break;
                    float fade = 1f - (float)sy / Mathf.Max(1, mirrorH);
                    FillRect(bx, segTop, barW, segH, Color.Lerp(Color.black, pink * 0.4f, fade * 0.65f));
                }
            }
        }

        private void DrawLetterRing(float pump, float high01, float alpha)
        {
            float cx = _w * 0.5f;
            float cy = _h * 0.48f;
            float radius = _h * 0.28f * (0.85f + 0.2f * pump);
            float thick = 2.5f + 3f * high01;
            float time = Time.time;
            Color ring = Color.HSVToRGB(Mathf.Repeat(0.10f + time * 0.12f, 1f), 0.75f, 1f);
            ring.a = alpha * 0.85f;

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    float d = Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius);
                    if (d > thick) continue;
                    float a = Mathf.Clamp01(1f - d / thick) * alpha;
                    int idx = y * _w + x;
                    if (idx < 0 || idx >= _state.Length) continue;
                    _state[idx] = (Color32)Color.Lerp(_state[idx], ring, a * 0.7f);
                }
            }
        }

        private void DrawPhraseHalo(float pump, float high01, float time)
        {
            float cx = _w * 0.5f;
            float cy = _h * 0.5f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 2.5f);
            Color halo = Color.HSVToRGB(Mathf.Repeat(0.10f + time * 0.05f, 1f), 0.35f, 0.35f + 0.25f * pump);

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    float dx = (x - cx) / (_w * 0.42f);
                    float dy = (y - cy) / (_h * 0.28f);
                    float e = dx * dx + dy * dy;
                    if (e > 1.2f) continue;
                    float a = Mathf.Clamp01(1f - e) * (0.12f + 0.18f * pulse) * (0.6f + 0.4f * high01);
                    int idx = y * _w + x;
                    if (idx < 0 || idx >= _state.Length) continue;
                    _state[idx] = (Color32)Color.Lerp(_state[idx], halo, a);
                }
            }
        }

        private void DrawIntroSpectrumEq(float pump, float high01)
        {
            int bars = Mathf.Clamp(_introEqBars, 8, 64);
            int barW = Mathf.Max(2, _w / (bars * 2));
            int gap = Mathf.Max(1, barW / 2);
            int step = barW + gap;
            int totalW = bars * step - gap;
            int x0 = (_w - totalW) / 2;
            int yBase = Mathf.RoundToInt(_h * 0.88f);
            int maxH = Mathf.RoundToInt(_h * 0.22f);
            int segH = Mathf.Max(2, barW);
            int segGap = 1;

            for (int b = 0; b < bars; b++)
            {
                float v = 0f;
                if (_spokes != null && _spokes.Length > 0)
                {
                    int si = Mathf.Clamp(Mathf.FloorToInt((float)b / bars * _spokes.Length), 0, _spokes.Length - 1);
                    v = _spokes[si];
                }
                v = Mathf.Clamp01(v * (0.55f + 0.75f * pump) + high01 * 0.12f);
                int barH = Mathf.RoundToInt(maxH * v);
                int bx = x0 + b * step;

                for (int sy = 0; sy < barH; sy += segH + segGap)
                {
                    int segTop = yBase - sy - segH;
                    if (segTop < 0) break;
                    float ratio = (float)sy / Mathf.Max(1, maxH);
                    Color c = SpectrumBarColor(ratio);
                    FillRect(bx, segTop, barW, segH, c);
                }
            }
        }

        private static Color SpectrumBarColor(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            if (ratio < 0.45f) return Color.Lerp(new Color(0.1f, 0.9f, 0.2f), new Color(0.95f, 0.95f, 0.1f), ratio / 0.45f);
            if (ratio < 0.75f) return Color.Lerp(new Color(0.95f, 0.95f, 0.1f), new Color(1f, 0.45f, 0.05f), (ratio - 0.45f) / 0.3f);
            return Color.Lerp(new Color(1f, 0.45f, 0.05f), new Color(1f, 0.1f, 0.15f), (ratio - 0.75f) / 0.25f);
        }

        private void FillRect(int x, int y, int w, int h, Color c)
        {
            int x1 = Mathf.Clamp(x, 0, _w - 1);
            int y1 = Mathf.Clamp(y, 0, _h - 1);
            int x2 = Mathf.Clamp(x + w, 0, _w);
            int y2 = Mathf.Clamp(y + h, 0, _h);
            Color32 c32 = c;
            for (int py = y1; py < y2; py++)
            {
                int row = py * _w;
                for (int px = x1; px < x2; px++)
                    _state[row + px] = c32;
            }
        }
    }
}

