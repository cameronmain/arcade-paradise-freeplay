using System;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppRAT.Arcade;
using Il2CppRAT.Managers;
using MelonLoader;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    /// <summary>
    /// Unity ArcadeGame component that hosts a libretro emulator core on the cabinet screen
    /// </summary>
    public class EmulatorArcadeManager : ArcadeGame
    {
        // ── Libretro state ────────────────────────────────────
        private bool _emuRunning;
        private bool _attractMode; // true = auto-started demo; skips camera swap
        private bool _initFailed; // prevents infinite retry when core/ROM can't load
        private double _emuFrameAccum; // accumulated real time since last emulator frame

        // ── Cabinet screen ─────────────────────────────────────
        private Texture2D _screenTexture;
        private Material _screenMaterial;
        private Il2CppRAT.Arcade.ArcadeMachine _machine;
        private IntPtr _unmanagedBuffer;
        private int _screenW, _screenH;
        private int _screenByteCount; // RGB24 = w * h * 3

        // ── Config (loaded via ConfigLoader from cabinet.json or autoscan) ──
        private string _corePath;
        private string _romPath;          // current ROM full path
        private string _systemDir;

        // ── Multi-ROM cycling ──────────────────────────────────
        private string[] _romList;         // all ROM full paths
        private int _currentRomIndex;      // index into _romList
        private bool _cycleKeyWasDown;     // debounce for load next ROM hotkey
        private float _cycleDebounceTimer; // post-cycle cooldown (seconds)
        private System.Collections.Generic.HashSet<int> _failedRomIndices; // ROMs that failed during current cycle attempt

        static EmulatorArcadeManager()
        {
            ClassInjector.RegisterTypeInIl2Cpp<EmulatorArcadeManager>();
        }

        public EmulatorArcadeManager(IntPtr ptr) : base(ptr) { }

        // ── ArcadeGame overrides ───────────────────────────────

        public override unsafe void StartGame()
        {
            MelonLogger.Msg("[EmulatorArcadeManager] StartGame() called");

            if (IsInitalised)
            {
                _State_k__BackingField = EState.Play;
                _attractMode = false;
                LibretroHost.SetAudioPlayMode(true);
                _machine?.SwapToLookingAt();
                MelonLogger.Msg("[EmulatorArcadeManager] Already initialised, resuming play");
                return;
            }

            if (InitalizeGame())
            {
                IsInitalised = true;
                _State_k__BackingField = EState.Play;
                MelonLogger.Msg("[EmulatorArcadeManager] InitaliseGame succeeded, calling PlayGame");
                PlayGame();
            }
            else
            {
                MelonLogger.Error("[EmulatorArcadeManager] InitaliseGame failed!");
            }
        }

        public override unsafe void StopGame()
        {
            MelonLogger.Msg("[EmulatorArcadeManager] StopGame() - full reset, restarting attract mode");

            _emuRunning = false;
            _State_k__BackingField = EState.Idle;
            CleanGame();
            IsInitalised = false;
            _attractMode = true;

            if (InitalizeGame())
            {
                IsInitalised = true;
                _State_k__BackingField = EState.Play;
                _emuRunning = true;
                if (_machine?.m_Screen != null && _screenMaterial != null)
                    _machine.m_Screen.material = _screenMaterial;
                MelonLogger.Msg("[EmulatorArcadeManager] Attract mode restarted after exit");
            }
        }

        public override unsafe bool InitalizeGame()
        {
            MelonLogger.Msg("[EmulatorArcadeManager] InitalizeGame() called");

            // ── Load config (preferences only) + autoscan ROMs ──
            if (_corePath == null)
            {
                try
                {
                    var config = ConfigLoader.Load();
                    _corePath = config.core;
                    _systemDir = config.systemDir;

                // always autoscan roms/ folder. Config only sets the starting game.
                _romList = ConfigLoader.ScanRoms();

                // fallback: if roms/ is empty, try wjammers.zip in the FreePlay folder
                if (_romList.Length == 0)
                {
                    MelonLogger.Error("[EmulatorArcadeManager] No ROMs found in roms/");
                    _initFailed = true;
                    return false;
                }

                _currentRomIndex = ConfigLoader.GetDefaultRomIndex(_romList, config);

                // Override with last-played ROM if it still exists in the list
                string lastRom = ConfigLoader.LoadLastRom(_romList);
                if (lastRom != null)
                {
                    for (int i = 0; i < _romList.Length; i++)
                    {
                        if (string.Equals(Path.GetFileName(_romList[i]), lastRom, StringComparison.OrdinalIgnoreCase))
                        {
                            _currentRomIndex = i;
                            break;
                        }
                    }
                }

                _romPath = _romList[_currentRomIndex];

                string romDesc = _romList.Length > 1
                    ? $"{_romList.Length} ROMs, starting with {Path.GetFileName(_romPath)}"
                    : Path.GetFileName(_romPath);
                MelonLogger.Msg($"[EmulatorArcadeManager] Config: core={Path.GetFileName(_corePath)}, {romDesc}, system={_systemDir}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[EmulatorArcadeManager] Failed to load config: {ex.Message}");
                    _initFailed = true;
                    return false;
                }
            }

            m_GameName = "Free Play";
            m_MaxPlayers = 1;
            m_LeaderboardID = "freeplay_arcade";
            m_allowQuit = true;

            Mono = this;
            m_Root = gameObject;
            try
            {
                var inputPlayerClass = IL2CPP.GetIl2CppClass(
                    "Assembly-CSharp.dll", "RAT.Managers", "InputManager/InputPlayer");
                Input = new InputManager.InputPlayer(IL2CPP.il2cpp_object_new(inputPlayerClass));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EmulatorArcadeManager] Failed to create InputPlayer: {ex.Message}");
            }
            Players = new Il2CppReferenceArray<InputManager.InputPlayer>(0);

            // ── Cabinet screen setup ───────────────────────────
            _machine = GetComponent<Il2CppRAT.Arcade.ArcadeMachine>();
            if (_machine != null && _machine.m_Screen != null)
            {
                int w = 640, h = 480;
                _screenW = w;
                _screenH = h;
                _screenTexture = new Texture2D(w, h, TextureFormat.RGB24, false);
                _screenTexture.filterMode = FilterMode.Point;
                _screenByteCount = w * h * 3;
                if (_unmanagedBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_unmanagedBuffer);
                _unmanagedBuffer = Marshal.AllocHGlobal(_screenByteCount);

                var shader = Shader.Find("Unlit/Texture");
                if (shader != null)
                {
                    _screenMaterial = new Material(shader);
                    _screenMaterial.mainTexture = _screenTexture;
                }
                else
                {
                    MelonLogger.Error("[EmulatorArcadeManager] Shader 'Unlit/Texture' not found");
                }

                MelonLogger.Msg($"[EmulatorArcadeManager] Texture2D {w}x{h} prepared");
            }
            else
            {
                MelonLogger.Warning("[EmulatorArcadeManager] Cabinet screen (m_Screen) not found");
            }

            // ── Load the configured libretro core ─────────────────
            // currently the same core is reused for every ROM during cycling
            bool loaded = TryLoadCore();
            if (!loaded)
            {
                _initFailed = true;
                return false;
            }

            MelonLogger.Msg("[EmulatorArcadeManager] Emulator initialised");
            return true;
        }

        private bool TryLoadCore()
        {
            MelonLogger.Msg($"[EmulatorArcadeManager] Loading core: {Path.GetFileName(_corePath)}");

            if (!File.Exists(_corePath))
            {
                MelonLogger.Error($"[EmulatorArcadeManager] Core DLL not found: {_corePath}");
                return false;
            }

            if (!LibretroHost.LoadCore(_corePath))
            {
                MelonLogger.Error($"[EmulatorArcadeManager] Failed to load core");
                return false;
            }

            Directory.CreateDirectory(_systemDir);
            string saveDir = Path.Combine(_systemDir, "saves");
            Directory.CreateDirectory(saveDir);
            LibretroHost.Init(_systemDir, saveDir);

            if (!File.Exists(_romPath))
            {
                MelonLogger.Error($"[EmulatorArcadeManager] ROM not found: {_romPath}");
                LibretroHost.Shutdown();
                return false;
            }

            if (!LibretroHost.LoadGame(_romPath))
            {
                MelonLogger.Error($"[EmulatorArcadeManager] ROM rejected by {Path.GetFileName(_corePath)}");
                LibretroHost.Shutdown();
                return false;
            }

            LibretroHost.StartAudio(gameObject);
            MelonLogger.Msg($"[EmulatorArcadeManager] ROM loaded: {Path.GetFileName(_romPath)}");
            return true;
        }

        public override unsafe void PlayGame()
        {
            MelonLogger.Msg("[EmulatorArcadeManager] PlayGame() called");

            _emuRunning = true;
            LibretroHost.SetAudioPlayMode(!_attractMode);

            if (_attractMode)
            {
                if (_machine?.m_Screen != null && _screenMaterial != null)
                    _machine.m_Screen.material = _screenMaterial;
            }
            else
            {
                _machine?.SwapToLookingAt();
                if (_machine?.m_Screen != null && _screenMaterial != null)
                    _machine.m_Screen.material = _screenMaterial;
            }

            ResizeTexture(LibretroHost.FramebufferWidth, LibretroHost.FramebufferHeight);

            MelonLogger.Msg($"[EmulatorArcadeManager] Game started ({(_attractMode ? "attract" : "play")} mode)");
        }

        // ── Attract mode ────────────────────────────────────────

        public void StartAttractMode()
        {
            if (IsInitalised || _initFailed)
                return;

            _attractMode = true;
            StartGame();
        }

        public void OnPlayerInteract()
        {
            if (!_attractMode)
                return;

            _attractMode = false;
            LibretroHost.SetAudioPlayMode(true);
            _machine?.SwapToLookingAt();
            MelonLogger.Msg("[EmulatorArcadeManager] Transitioned from attract to play mode");
        }

        public void ReApplyScreenMaterial()
        {
            if (_machine?.m_Screen != null && _screenMaterial != null)
                _machine.m_Screen.material = _screenMaterial;
        }

        // ── Per-frame update ────────────────────────────────────

        public void EmuUpdate()
        {
            if (!_emuRunning)
                return;

            LibretroHost.UpdateAudioSpatialization();

            // ── Multi-ROM cycling hotkey (F9) ────────────────
            if (_cycleDebounceTimer > 0f)
            {
                _cycleDebounceTimer -= Time.unscaledDeltaTime;
            }
            else if (_romList != null && _romList.Length > 1)
            {
                bool f9Down = UnityEngine.Input.GetKey(KeyCode.F9);
                if (f9Down && !_cycleKeyWasDown)
                {
                    CycleToNextGame();
                    // Skip the rest of this frame; the core was just reloaded
                    // and needs a clean frame to initialise its video output.
                    _cycleKeyWasDown = true;
                    return;
                }
                _cycleKeyWasDown = f9Down;
            }

            // cache combined keyboard + controller input so callbacks running during retro_run() see fresh state
            LibretroHost.PollInput();

            // frame pacing: run emulator frames at the cores target rate
            _emuFrameAccum += Time.unscaledDeltaTime;
            double frameTime = LibretroHost.FrameTimeSeconds;
            if (_emuFrameAccum < frameTime)
                return;

            // use while (not if) to avoid drift/skip cycles that make input feel sticky & cap at 4 frames to prevent runaway fast-forward after alt-tab
            int maxFrames = 4;
            while (_emuFrameAccum >= frameTime && maxFrames-- > 0)
            {
                _emuFrameAccum -= frameTime;
                try
                {
                    if (!LibretroHost.IsLoaded) { _emuRunning = false; return; }
                    LibretroHost.RunFrame();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[EmulatorArcadeManager] RunFrame error: {ex.Message}");
                    _emuRunning = false;
                    return;
                }
            }
            // prevent accumulator runaway after long pauses
            if (_emuFrameAccum > frameTime * 4)
                _emuFrameAccum = frameTime;

            if (_screenTexture != null && _unmanagedBuffer != IntPtr.Zero)
            {
                uint[] fb = LibretroHost.GetFramebuffer();
                int fbW = LibretroHost.FramebufferWidth;
                int fbH = LibretroHost.FramebufferHeight;

                // LibretroHost already handles portrait rotation;
                // FramebufferWidth/Height and GetFramebuffer() always return landscape data.
                if (fbW != _screenW || fbH != _screenH)
                    ResizeTexture(fbW, fbH);

                int w = _screenW, h = _screenH;
                int byteCount = w * h * 3;

                unsafe
                {
                    byte* dst = (byte*)_unmanagedBuffer.ToPointer();
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            uint px = fb[y * w + x];
                            int dstIdx = ((h - 1 - y) * w + x) * 3;
                            dst[dstIdx]     = (byte)((px >> 16) & 0xFF);
                            dst[dstIdx + 1] = (byte)((px >> 8) & 0xFF);
                            dst[dstIdx + 2] = (byte)(px & 0xFF);
                        }
                    }
                }

                _screenTexture.LoadRawTextureData(_unmanagedBuffer, byteCount);
                _screenTexture.Apply(false);
            }

            if (_machine?.m_Screen != null && _screenMaterial != null &&
                _machine.m_Screen.material != _screenMaterial)
            {
                _machine.m_Screen.material = _screenMaterial;
            }
        }

        // Unity calls this on its audio thread for the AudioSource attached to the cabinet.
        // It is intentionally limited to the lock-free libretro buffer bridge
        public void OnAudioFilterRead(Il2CppStructArray<float> data, int channels)
        {
            LibretroHost.ReadAudio(data, channels);
        }

        // ── Cleanup ─────────────────────────────────────────────

        public override unsafe void CleanGame()
        {
            MelonLogger.Msg("[EmulatorArcadeManager] CleanGame() called");

            _emuRunning = false;

            Input = null;
            Players = null;

            if (_unmanagedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_unmanagedBuffer);
                _unmanagedBuffer = IntPtr.Zero;
            }

            if (_screenMaterial != null)
            {
                UnityEngine.Object.Destroy(_screenMaterial);
                _screenMaterial = null;
            }
            if (_screenTexture != null)
            {
                UnityEngine.Object.Destroy(_screenTexture);
                _screenTexture = null;
            }

            _initFailed = false;
            LibretroHost.Shutdown();
        }

        // ── Multi-ROM cycling ──────────────────────────────────

        /// <summary>
        /// Cycle to the next WORKING ROM in the list. Hotkey: F9.
        /// Keeps advancing past failed ROMs until it finds one that loads.
        /// If ALL ROMs fail, stays on the current one.
        /// </summary>
        private void CycleToNextGame()
        {
            if (_romList == null || _romList.Length <= 1)
                return;

            if (_failedRomIndices == null)
                _failedRomIndices = new System.Collections.Generic.HashSet<int>();

            int startIndex = _currentRomIndex;  // where we started from
            int prevIndex = _currentRomIndex;   // last working ROM
            bool foundWorking = false;

            // Try each ROM in order, skipping ones we already know are broken in this pass
            for (int i = 0; i < _romList.Length; i++)
            {
                _currentRomIndex = (_currentRomIndex + 1) % _romList.Length;

                // If we've looped all the way around, give up
                if (_currentRomIndex == startIndex && i > 0)
                {
                    MelonLogger.Error("[EmulatorArcadeManager] All ROMs failed, staying on current game");
                    _currentRomIndex = prevIndex;
                    _romPath = _romList[prevIndex];
                    if (!TryLoadCore())
                    {
                        MelonLogger.Error("[EmulatorArcadeManager] Rollback also failed, shutting down");
                        _emuRunning = false;
                    }
                    else
                    {
                        ResizeTexture(LibretroHost.FramebufferWidth, LibretroHost.FramebufferHeight);
                    }
                    _cycleDebounceTimer = 2f;
                    _failedRomIndices.Clear();
                    return;
                }

                _romPath = _romList[_currentRomIndex];
                string romName = Path.GetFileName(_romPath);
                MelonLogger.Msg($"[EmulatorArcadeManager] Cycling to ROM {_currentRomIndex + 1}/{_romList.Length}: {romName}");

                LibretroHost.Shutdown();

                if (TryLoadCore())
                {
                    foundWorking = true;
                    break;
                }

                MelonLogger.Warning($"[EmulatorArcadeManager] Skipping {romName} (failed to load), trying next...");
            }

            if (!foundWorking)
            {
                // Already handled above (full loop case)
                return;
            }

            // Success: clear the failure tracker since we found a working ROM
            _failedRomIndices.Clear();
            ResizeTexture(LibretroHost.FramebufferWidth, LibretroHost.FramebufferHeight);
            _cycleDebounceTimer = 2f;

            // Persist the new ROM so the cabinet resumes here next load
            ConfigLoader.SaveLastRom(Path.GetFileName(_romPath));

            MelonLogger.Msg($"[EmulatorArcadeManager] Now playing: {Path.GetFileName(_romPath)}");
        }

        // ── Helpers ─────────────────────────────────────────────

        private void ResizeTexture(int w, int h)
        {
            if (w <= 0 || h <= 0 || (w == _screenW && h == _screenH))
                return;

            MelonLogger.Msg($"[EmulatorArcadeManager] Resizing texture: {_screenW}x{_screenH} → {w}x{h}");

            if (_screenTexture != null)
                UnityEngine.Object.Destroy(_screenTexture);
            if (_unmanagedBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_unmanagedBuffer);
                _unmanagedBuffer = IntPtr.Zero;
            }

            _screenW = w;
            _screenH = h;
            _screenByteCount = w * h * 3;
            _screenTexture = new Texture2D(w, h, TextureFormat.RGB24, false);
            _screenTexture.filterMode = FilterMode.Point;
            _unmanagedBuffer = Marshal.AllocHGlobal(_screenByteCount);

            if (_screenMaterial != null)
                _screenMaterial.mainTexture = _screenTexture;
        }
    }
}
