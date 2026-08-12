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

        // ── ROM selection ──────────────────────────────────────
        private string[] _romList;         // all ROM full paths
        private int _currentRomIndex;      // index into _romList

        // ── Cabinet ROM browser ────────────────────────────────
        private bool _romBrowserOpen;
        private bool _romBrowserReturnToPlay;
        private int _romBrowserIndex;
        private float _romBrowserRepeatTimer;

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
                if (_attractMode)
                {
                    LibretroHost.SetAudioPlayMode(false);
                    MelonLogger.Msg("[EmulatorArcadeManager] Already initialised; remaining in attract mode until ROM selection");
                }
                else
                {
                    LibretroHost.SetAudioPlayMode(true);
                    _machine?.SwapToLookingAt();
                    MelonLogger.Msg("[EmulatorArcadeManager] Already initialised, resuming play");
                }
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
            // the configured core is reused when selecting another ROM
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
            if (!_attractMode || _romBrowserOpen)
                return;

            // cabinet interaction starts the currently selected ROM
            _attractMode = false;
            LibretroHost.SetAudioPlayMode(true);
            _machine?.SwapToLookingAt();
            MelonLogger.Msg($"[EmulatorArcadeManager] Cabinet interacted; playing ROM: {Path.GetFileName(_romPath)}");
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
            {
                if (_romBrowserOpen)
                {
                    _romBrowserOpen = false;
                    _attractMode = true;
                    LibretroHost.SetAudioPlayMode(false);
                    MelonLogger.Warning("[EmulatorArcadeManager] ROM browser closed because emulation stopped");
                }
                return;
            }

            LibretroHost.UpdateAudioSpatialization();

            if (_romBrowserOpen)
            {
                UpdateRomBrowserInput();
                return;
            }

            // ── ROM browser hotkey (F9) ──────────────────────
            if (!_attractMode && _romList != null && _romList.Length > 1 &&
                UnityEngine.Input.GetKeyDown(KeyCode.F9))
            {
                OpenRomBrowser(returnToPlay: true);
                return;
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

        private void OpenRomBrowser(bool returnToPlay)
        {
            if (_romList == null || _romList.Length == 0)
                return;

            if (_screenTexture == null || _screenMaterial == null || _screenW <= 0 || _screenH <= 0)
            {
                MelonLogger.Warning("[EmulatorArcadeManager] ROM browser unavailable: cabinet screen texture is not ready");
                return;
            }

            _romBrowserOpen = true;
            _romBrowserReturnToPlay = returnToPlay;
            _romBrowserIndex = Mathf.Clamp(_currentRomIndex, 0, _romList.Length - 1);
            _romBrowserRepeatTimer = 0f;
            LibretroHost.SetAudioPlayMode(false);
            ReApplyScreenMaterial();
            DrawRomBrowserScreen();
            MelonLogger.Msg($"[EmulatorArcadeManager] ROM browser opened ({_romBrowserIndex + 1}/{_romList.Length})");
        }

        private void UpdateRomBrowserInput()
        {
            if (_romList == null || _romList.Length == 0)
                return;

            bool moved = false;
            bool up = UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) ||
                      UnityEngine.Input.GetKeyDown(KeyCode.W) ||
                      UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton4);
            bool down = UnityEngine.Input.GetKeyDown(KeyCode.DownArrow) ||
                        UnityEngine.Input.GetKeyDown(KeyCode.S) ||
                        UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton5);

            float axis = UnityEngine.Input.GetAxisRaw("Vertical");
            _romBrowserRepeatTimer -= Time.unscaledDeltaTime;
            if (!up && !down && _romBrowserRepeatTimer <= 0f)
            {
                if (axis > 0.5f) { up = true; _romBrowserRepeatTimer = 0.16f; }
                else if (axis < -0.5f) { down = true; _romBrowserRepeatTimer = 0.16f; }
            }

            if (up)
            {
                _romBrowserIndex = (_romBrowserIndex - 1 + _romList.Length) % _romList.Length;
                moved = true;
                // prevent the same held keyboard key from being counted again through Unitys vertical axis on the following frame
                _romBrowserRepeatTimer = 0.16f;
            }
            else if (down)
            {
                _romBrowserIndex = (_romBrowserIndex + 1) % _romList.Length;
                moved = true;
                _romBrowserRepeatTimer = 0.16f;
            }

            if (moved)
                DrawRomBrowserScreen();

            bool confirm = UnityEngine.Input.GetKeyDown(KeyCode.Return) ||
                           UnityEngine.Input.GetKeyDown(KeyCode.Space) ||
                           UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton1) ||
                           UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton7);
            bool cancel = UnityEngine.Input.GetKeyDown(KeyCode.Escape) ||
                          UnityEngine.Input.GetKeyDown(KeyCode.Backspace) ||
                          UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton0);

            if (confirm)
                CloseRomBrowser(launch: true);
            else if (cancel)
                CloseRomBrowser(launch: false);
        }

        private void CloseRomBrowser(bool launch)
        {
            if (!_romBrowserOpen)
                return;

            bool returnToPlay = _romBrowserReturnToPlay;
            int selected = _romBrowserIndex;
            if (launch && !LoadRomAtIndex(selected))
            {
                if (!_emuRunning)
                {
                    // both the selected ROM and the previous ROM failed
                    // do not leave input locked behind a menu that can no longer receive updates
                    _romBrowserOpen = false;
                    _attractMode = true;
                    LibretroHost.SetAudioPlayMode(false);
                    MelonLogger.Error("[EmulatorArcadeManager] ROM browser closed after unrecoverable load failure");
                }
                else
                {
                    DrawRomBrowserScreen();
                }
                MelonLogger.Warning($"[EmulatorArcadeManager] Could not load selected ROM: {Path.GetFileName(_romList[selected])}");
                return;
            }

            _romBrowserOpen = false;

            if (launch || returnToPlay)
            {
                _attractMode = false;
                LibretroHost.SetAudioPlayMode(true);
                _machine?.SwapToLookingAt();
                MelonLogger.Msg($"[EmulatorArcadeManager] Playing ROM: {Path.GetFileName(_romPath)}");
            }
            else
            {
                _attractMode = true;
                LibretroHost.SetAudioPlayMode(false);
                MelonLogger.Msg("[EmulatorArcadeManager] ROM browser cancelled: returning to attract mode");
            }
        }

        private bool LoadRomAtIndex(int index)
        {
            if (_romList == null || index < 0 || index >= _romList.Length)
                return false;

            if (index == _currentRomIndex && LibretroHost.IsLoaded)
                return true;

            int previousIndex = _currentRomIndex;
            string previousPath = _romPath;
            LibretroHost.Shutdown();
            _currentRomIndex = index;
            _romPath = _romList[index];

            if (TryLoadCore())
            {
                ResizeTexture(LibretroHost.FramebufferWidth, LibretroHost.FramebufferHeight);
                ConfigLoader.SaveLastRom(Path.GetFileName(_romPath));
                return true;
            }

            _currentRomIndex = previousIndex;
            _romPath = previousPath;
            if (!TryLoadCore())
            {
                _emuRunning = false;
                MelonLogger.Error("[EmulatorArcadeManager] Selected ROM and rollback both failed");
            }
            return false;
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
            _romBrowserOpen = false;

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

        // ── Cabinet-screen ROM browser rendering ─────────────────

        private unsafe void DrawRomBrowserScreen()
        {
            if (_screenTexture == null || _unmanagedBuffer == IntPtr.Zero || _screenW <= 0 || _screenH <= 0)
                return;

            int w = _screenW;
            int h = _screenH;
            byte* pixels = (byte*)_unmanagedBuffer.ToPointer();

            FillBrowserRect(pixels, w, h, 0, 0, w, h, 5, 10, 18);

            int scale = w >= 240 ? 2 : 1;
            int margin = 8 * scale;
            int listY = scale == 2 ? 48 : 34;
            int rowHeight = 8 * scale + 2;
            int controlsY = h - 8 * scale;
            int visibleRows = Mathf.Clamp((controlsY - listY - 4) / rowHeight, 1, 8);
            int first = Mathf.Clamp(_romBrowserIndex - visibleRows / 2, 0,
                                    Mathf.Max(0, _romList.Length - visibleRows));

            FillBrowserRect(pixels, w, h, 3, 3, w - 6, h - 6, 15, 44, 64);
            FillBrowserRect(pixels, w, h, 5, 5, w - 10, h - 10, 5, 10, 18);
            DrawBrowserText(pixels, w, h, "FREE PLAY", margin, 6, scale, 127, 219, 255);
            DrawBrowserText(pixels, w, h,
                $"{_romBrowserIndex + 1}/{_romList.Length}", w - margin - 36 * scale, 8, scale, 170, 183, 196);
            DrawBrowserText(pixels, w, h, "SELECT GAME", margin, 23, 1, 170, 183, 196);

            int maxNameChars = Mathf.Max(4, (w - margin * 2 - 18 * scale) / (6 * scale));
            int last = Mathf.Min(_romList.Length, first + visibleRows);
            for (int i = first; i < last; i++)
            {
                int row = i - first;
                int y = listY + row * rowHeight;
                bool selected = i == _romBrowserIndex;
                if (selected)
                    FillBrowserRect(pixels, w, h, margin - 3, y - 2,
                                    w - margin * 2 + 6, rowHeight, 72, 64, 25);

                string name = GetBrowserName(i, maxNameChars);
                string label = selected ? "> " + name : "  " + name;
                DrawBrowserText(pixels, w, h, label, margin, y, scale,
                    selected ? (byte)255 : (byte)225,
                    selected ? (byte)230 : (byte)235,
                    selected ? (byte)109 : (byte)240);
            }

            if (w < 300)
            {
                DrawBrowserText(pixels, w, h, "ARROWS/ENTER SELECT ESC/BACK EXIT", margin, controlsY, 1, 170, 183, 196);
            }
            else
            {
                DrawBrowserText(pixels, w, h, "UP/DOWN SELECT", margin, controlsY, 1, 170, 183, 196);
                DrawBrowserText(pixels, w, h, "ENTER/SPACE SELECT", w / 2 - 54, controlsY, 1, 170, 183, 196);
                DrawBrowserText(pixels, w, h, "ESC/BACK EXIT", w - margin - 78, controlsY, 1, 170, 183, 196);
            }

            _screenTexture.LoadRawTextureData(_unmanagedBuffer, _screenByteCount);
            _screenTexture.Apply(false);
        }

        private string GetBrowserName(int index, int maxLength)
        {
            string name = Path.GetFileNameWithoutExtension(_romList[index]).ToUpperInvariant();
            if (name.Length <= maxLength)
                return name;

            if (maxLength <= 2)
                return name.Substring(0, maxLength);
            return name.Substring(0, maxLength - 2) + "..";
        }

        private static unsafe void FillBrowserRect(byte* pixels, int width, int height,
                                                    int x, int yTop, int rectWidth, int rectHeight,
                                                    byte red, byte green, byte blue)
        {
            int left = Mathf.Max(0, x);
            int top = Mathf.Max(0, yTop);
            int right = Mathf.Min(width, x + rectWidth);
            int bottom = Mathf.Min(height, yTop + rectHeight);
            for (int y = top; y < bottom; y++)
            {
                int textureY = height - 1 - y;
                for (int drawX = left; drawX < right; drawX++)
                {
                    int offset = (textureY * width + drawX) * 3;
                    pixels[offset] = red;
                    pixels[offset + 1] = green;
                    pixels[offset + 2] = blue;
                }
            }
        }

        private static unsafe void DrawBrowserText(byte* pixels, int width, int height,
                                                    string text, int x, int yTop, int scale,
                                                    byte red, byte green, byte blue)
        {
            if (string.IsNullOrEmpty(text) || scale < 1)
                return;

            int cursor = x;
            foreach (char character in text)
            {
                string pattern = GetBrowserGlyph(character);
                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (pattern[row * 5 + column] != '1')
                            continue;

                        for (int pixelY = 0; pixelY < scale; pixelY++)
                        {
                            for (int pixelX = 0; pixelX < scale; pixelX++)
                            {
                                int drawX = cursor + column * scale + pixelX;
                                int drawY = yTop + row * scale + pixelY;
                                if (drawX < 0 || drawX >= width || drawY < 0 || drawY >= height)
                                    continue;

                                int textureY = height - 1 - drawY;
                                int offset = (textureY * width + drawX) * 3;
                                pixels[offset] = red;
                                pixels[offset + 1] = green;
                                pixels[offset + 2] = blue;
                            }
                        }
                    }
                }
                cursor += 6 * scale;
            }
        }

        private static string GetBrowserGlyph(char character)
        {
            switch (char.ToUpperInvariant(character))
            {
                case 'A': return "01110100011000111111100011000110001";
                case 'B': return "11110100011000111110100011000111110";
                case 'C': return "01111100001000010000100001000001111";
                case 'D': return "11110100011000110001100011000111110";
                case 'E': return "11111100001000011110100001000011111";
                case 'F': return "11111100001000011110100001000010000";
                case 'G': return "01111100001000010111100011000101110";
                case 'H': return "10001100011000111111100011000110001";
                case 'I': return "11111001000010000100001000010011111";
                case 'J': return "00111000100001000010000101001001100";
                case 'K': return "10001100101010011000101001001010001";
                case 'L': return "10000100001000010000100001000011111";
                case 'M': return "10001110111010110101100011000110001";
                case 'N': return "10001110011010110011100011000110001";
                case 'O': return "01110100011000110001100011000101110";
                case 'P': return "11110100011000111110100001000010000";
                case 'Q': return "01110100011000110001101011001001101";
                case 'R': return "11110100011000111110101001001010001";
                case 'S': return "01111100001000001110000010000111110";
                case 'T': return "11111001000010000100001000010000100";
                case 'U': return "10001100011000110001100011000101110";
                case 'V': return "10001100011000110001010100010000100";
                case 'W': return "10001100011000110101101011010110001";
                case 'X': return "10001010100010000100001000101010001";
                case 'Y': return "10001010100010000100001000010000100";
                case 'Z': return "11111000100010000100010001000011111";
                case '0': return "01110100011001110101110011000101110";
                case '1': return "00100011000010000100001000010011111";
                case '2': return "01110100010000100010001000100011111";
                case '3': return "11111000010000100110000011000101110";
                case '4': return "00010001100101010010111110001000010";
                case '5': return "11111100001000011110000010000111110";
                case '6': return "01110100001000011110100011000101110";
                case '7': return "11111000010001000100010000100001000";
                case '8': return "01110100011000101110100011000101110";
                case '9': return "01110100011000101111000010000101110";
                case '/': return "00001000100010000100010001000100001";
                case '-': return "00000000000000011111000000000000000";
                case '_': return "00000000000000000000000000000111111";
                case '.': return "00000000000000000000000000000000100";
                case ':': return "00000000100000000000000100000000000";
                case '>': return "10000010000010000010001000100010000";
                case '(': return "00010001000100001000010000010000010";
                case ')': return "01000001000001000010000100010001000";
                case '+': return "00000001000010011111001000010000000";
                case ' ':
                default: return "00000000000000000000000000000000000";
            }
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
