using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public static class LibretroHost
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly Dictionary<KeyCode, int> _vkMap = new Dictionary<KeyCode, int>
        {
            { KeyCode.UpArrow,    0x26 }, { KeyCode.DownArrow,  0x28 },
            { KeyCode.LeftArrow,  0x25 }, { KeyCode.RightArrow, 0x27 },
            { KeyCode.Return,     0x0D }, { KeyCode.LeftShift,  0xA0 },
            { KeyCode.LeftControl,0x11 }, { KeyCode.LeftAlt,    0x12 },
            { KeyCode.Space,      0x20 }, { KeyCode.Escape,     0x1B },
            { KeyCode.Tab,        0x09 }, { KeyCode.Backspace,  0x08 },
            { KeyCode.Delete,     0x2E },
            { KeyCode.Alpha0, 0x30 }, { KeyCode.Alpha1, 0x31 }, { KeyCode.Alpha2, 0x32 },
            { KeyCode.Alpha3, 0x33 }, { KeyCode.Alpha4, 0x34 }, { KeyCode.Alpha5, 0x35 },
            { KeyCode.Alpha6, 0x36 }, { KeyCode.Alpha7, 0x37 }, { KeyCode.Alpha8, 0x38 },
            { KeyCode.Alpha9, 0x39 },
            { KeyCode.A, 0x41 }, { KeyCode.B, 0x42 }, { KeyCode.C, 0x43 },
            { KeyCode.D, 0x44 }, { KeyCode.E, 0x45 }, { KeyCode.F, 0x46 },
            { KeyCode.G, 0x47 }, { KeyCode.H, 0x48 }, { KeyCode.I, 0x49 },
            { KeyCode.J, 0x4A }, { KeyCode.K, 0x4B }, { KeyCode.L, 0x4C },
            { KeyCode.M, 0x4D }, { KeyCode.N, 0x4E }, { KeyCode.O, 0x4F },
            { KeyCode.P, 0x50 }, { KeyCode.Q, 0x51 }, { KeyCode.R, 0x52 },
            { KeyCode.S, 0x53 }, { KeyCode.T, 0x54 }, { KeyCode.U, 0x55 },
            { KeyCode.V, 0x56 }, { KeyCode.W, 0x57 }, { KeyCode.X, 0x58 },
            { KeyCode.Y, 0x59 }, { KeyCode.Z, 0x5A },
            { KeyCode.F1, 0x70 }, { KeyCode.F5, 0x74 }, { KeyCode.F9, 0x78 },
        };

        private static bool IsWinKeyDown(KeyCode key)
        {
            return _vkMap.TryGetValue(key, out int vk) && (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        // Cached input state from the keyboard and controller
        private static bool _inpUp, _inpDown, _inpLeft, _inpRight;
        private static bool _inpStart, _inpCoin, _inpBtnA, _inpBtnB;

        public static void PollInput()
        {
            bool kbUp    = IsWinKeyDown(KeyCode.UpArrow)    || IsWinKeyDown(KeyCode.W);
            bool kbDown  = IsWinKeyDown(KeyCode.DownArrow)  || IsWinKeyDown(KeyCode.S);
            bool kbLeft  = IsWinKeyDown(KeyCode.LeftArrow)  || IsWinKeyDown(KeyCode.A);
            bool kbRight = IsWinKeyDown(KeyCode.RightArrow) || IsWinKeyDown(KeyCode.D);
            bool kbStart = IsWinKeyDown(KeyCode.Return)     || IsWinKeyDown(KeyCode.Alpha1);
            bool kbCoin  = IsWinKeyDown(KeyCode.Alpha5);
            bool kbA     = IsWinKeyDown(KeyCode.LeftAlt)    || IsWinKeyDown(KeyCode.X);
            bool kbB     = IsWinKeyDown(KeyCode.LeftControl)|| IsWinKeyDown(KeyCode.Z);

            float axisH = Input.GetAxis("Horizontal");
            float axisV = Input.GetAxis("Vertical");
            bool padUp    = axisV > 0.5f;
            bool padDown  = axisV < -0.5f;
            bool padLeft  = axisH < -0.5f;
            bool padRight = axisH > 0.5f;
            bool padStart = Input.GetKey(KeyCode.JoystickButton7);
            bool padCoin  = Input.GetKey(KeyCode.JoystickButton6);
            bool padA     = Input.GetKey(KeyCode.JoystickButton1);
            bool padB     = Input.GetKey(KeyCode.JoystickButton0);

            _inpUp    = kbUp    || padUp;
            _inpDown  = kbDown  || padDown;
            _inpLeft  = kbLeft  || padLeft;
            _inpRight = kbRight || padRight;
            _inpStart = kbStart || padStart;
            _inpCoin  = kbCoin  || padCoin;
            _inpBtnA  = kbA     || padA;
            _inpBtnB  = kbB     || padB;
        }

        private static IntPtr _coreHandle;
        private static string _corePath;

        private static RetroEnvironmentDelegate _envDelegate;
        private static RetroVideoRefreshDelegate _videoDelegate;
        private static RetroAudioSampleDelegate _audioDelegate;
        private static RetroAudioSampleBatchDelegate _audioBatchDelegate;
        private static RetroInputPollDelegate _inputPollDelegate;
        private static RetroInputStateDelegate _inputStateDelegate;

        private static uint _pixelFormat;
        private static int _fbWidth, _fbHeight, _fbPitch;
        private static uint[] _framebuffer;
        private static uint[] _rotatedBuffer;
        private static bool _fbPortrait;
        private static string _systemDir;
        private static string _saveDir;
        private static string _contentDir;
        private static int _frameCount;
        private static bool _coreInitialized;
        private static bool _gameLoaded;

        private static HashSet<uint> _seenEnvCommands = new HashSet<uint>();
        private static RetroKeyboardEventDelegate _keyboardCallback;
        private static bool _prevCtrl, _prevAlt;

        private static readonly Dictionary<string, IntPtr> _variableValues = new Dictionary<string, IntPtr>();
        private static readonly HashSet<string> _seenVarKeys = new HashSet<string>();

        private static readonly Dictionary<uint, KeyCode> _keyMap = new Dictionary<uint, KeyCode>
        {
            { 13,    KeyCode.Return },
            { 27,    KeyCode.Escape },
            { 32,    KeyCode.Space },
            { 9,     KeyCode.Tab },
            { 8,     KeyCode.Backspace },
            { 127,   KeyCode.Delete },
            { 273,   KeyCode.UpArrow },
            { 274,   KeyCode.DownArrow },
            { 275,   KeyCode.LeftArrow },
            { 276,   KeyCode.RightArrow },
            { 49,    KeyCode.Alpha1 },
            { 53,    KeyCode.Alpha5 },
        };

        private delegate uint RetroApiVersionDelegate();
        private delegate void RetroInitDelegate();
        private delegate void RetroDeinitDelegate();
        private delegate void RetroSetEnvironmentDelegate(IntPtr callback);
        private delegate void RetroSetVideoRefreshDelegate(IntPtr callback);
        private delegate void RetroSetAudioSampleDelegate(IntPtr callback);
        private delegate void RetroSetAudioSampleBatchDelegate(IntPtr callback);
        private delegate void RetroSetInputPollDelegate(IntPtr callback);
        private delegate void RetroSetInputStateDelegate(IntPtr callback);
        private delegate void RetroSetControllerPortDeviceDelegate(uint port, uint device);
        private delegate void RetroGetSystemInfoDelegate(ref RetroSystemInfo info);
        private delegate void RetroGetSystemAvInfoDelegate(ref RetroSystemAvInfo info);
        private delegate bool RetroLoadGameDelegate(ref RetroGameInfo game);
        private delegate void RetroRunDelegate();
        private delegate void RetroUnloadGameDelegate();
        private delegate IntPtr RetroGetMemoryDataDelegate(uint id);
        private delegate UIntPtr RetroGetMemorySizeDelegate(uint id);

        private static RetroApiVersionDelegate _retro_api_version;
        private static RetroInitDelegate _retro_init;
        private static RetroDeinitDelegate _retro_deinit;
        private static RetroSetEnvironmentDelegate _retro_set_environment;
        private static RetroSetVideoRefreshDelegate _retro_set_video_refresh;
        private static RetroSetAudioSampleDelegate _retro_set_audio_sample;
        private static RetroSetAudioSampleBatchDelegate _retro_set_audio_sample_batch;
        private static RetroSetInputPollDelegate _retro_set_input_poll;
        private static RetroSetInputStateDelegate _retro_set_input_state;
        private static RetroSetControllerPortDeviceDelegate _retro_set_controller_port_device;
        private static RetroGetSystemInfoDelegate _retro_get_system_info;
        private static RetroGetSystemAvInfoDelegate _retro_get_system_av_info;
        private static RetroLoadGameDelegate _retro_load_game;
        private static RetroRunDelegate _retro_run;
        private static RetroUnloadGameDelegate _retro_unload_game;
        private static RetroGetMemoryDataDelegate _retro_get_memory_data;
        private static RetroGetMemorySizeDelegate _retro_get_memory_size;

        public const uint RETRO_MEMORY_VIDEO_RAM = 2;
        public const uint RETRO_PIXEL_FORMAT_RGB565 = 0;
        public const uint RETRO_PIXEL_FORMAT_XRGB8888 = 1;
        // Libretro device values: JOYPAD=1, KEYBOARD=3, ANALOG=5.
        // JOYPAD=0 is RETRO_DEVICE_NONE and is not a controller device
        public const uint RETRO_DEVICE_JOYPAD = 1;
        public const uint RETRO_DEVICE_KEYBOARD = 3;
        public const uint RETRO_DEVICE_ANALOG = 5;
        public const uint RETRO_DEVICE_ID_JOYPAD_MASK = 256;
        public const uint RETRO_DEVICE_ID_JOYPAD_B = 0;
        public const uint RETRO_DEVICE_ID_JOYPAD_Y = 1;
        public const uint RETRO_DEVICE_ID_JOYPAD_SELECT = 2;
        public const uint RETRO_DEVICE_ID_JOYPAD_START = 3;
        public const uint RETRO_DEVICE_ID_JOYPAD_UP = 4;
        public const uint RETRO_DEVICE_ID_JOYPAD_DOWN = 5;
        public const uint RETRO_DEVICE_ID_JOYPAD_LEFT = 6;
        public const uint RETRO_DEVICE_ID_JOYPAD_RIGHT = 7;
        public const uint RETRO_DEVICE_ID_JOYPAD_A = 8;
        public const uint RETRO_DEVICE_ID_JOYPAD_X = 9;

        private const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10;
        private const uint RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 9;
        private const uint RETRO_ENVIRONMENT_GET_CONTENT_DIRECTORY = 53;
        private const uint RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY = 16;
        private const uint RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME = 18;
        private const uint RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS = 11;
        private const uint RETRO_ENVIRONMENT_SET_KEYBOARD_CALLBACK = 12;
        private const uint RETRO_ENVIRONMENT_GET_LANGUAGE = 64;
        private const uint RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION = 52;
        private const uint RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL = 66;
        private const uint RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY = 30;
        private const uint RETRO_ENVIRONMENT_GET_LOG_INTERFACE = 27;
        private const uint RETRO_ENVIRONMENT_SET_CONTROLLER_INFO = 47;
        private const uint RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS = 41;
        private const uint RETRO_ENVIRONMENT_GET_LED_INTERFACE = 46;
        private const uint RETRO_ENVIRONMENT_GET_VARIABLE = 15;

        [StructLayout(LayoutKind.Sequential)]
        public struct RetroSystemInfo
        {
            public IntPtr library_name;
            public IntPtr library_version;
            public IntPtr valid_extensions;
            [MarshalAs(UnmanagedType.I1)] public bool need_fullpath;
            [MarshalAs(UnmanagedType.I1)] public bool block_extract;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RetroGameGeometry
        {
            public uint base_width;
            public uint base_height;
            public uint max_width;
            public uint max_height;
            public float aspect_ratio;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RetroSystemTiming
        {
            public double fps;
            public double sample_rate;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RetroSystemAvInfo
        {
            public RetroGameGeometry geometry;
            public RetroSystemTiming timing;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RetroGameInfo
        {
            public IntPtr path;
            public IntPtr data;
            public UIntPtr size;
            public IntPtr meta;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool RetroEnvironmentDelegate(uint cmd, IntPtr data);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RetroVideoRefreshDelegate(IntPtr data, uint width, uint height, UIntPtr pitch);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RetroAudioSampleDelegate(short left, short right);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr RetroAudioSampleBatchDelegate(IntPtr data, UIntPtr frames);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RetroInputPollDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate short RetroInputStateDelegate(uint port, uint device, uint index, uint id);

        [StructLayout(LayoutKind.Sequential)]
        private struct RetroKeyboardCallback
        {
            public IntPtr callback;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RetroKeyboardEventDelegate(
            [MarshalAs(UnmanagedType.I1)] bool down,
            uint keycode,
            uint character,
            ushort key_modifiers);

        public static bool LoadCore(string coreDllPath)
        {
            _corePath = coreDllPath;
            _coreHandle = NativeLibrary.Load(coreDllPath);
            if (_coreHandle == IntPtr.Zero) return false;

            _retro_api_version = GetDelegate<RetroApiVersionDelegate>("retro_api_version");
            _retro_init = GetDelegate<RetroInitDelegate>("retro_init");
            _retro_deinit = GetDelegate<RetroDeinitDelegate>("retro_deinit");
            _retro_set_environment = GetDelegate<RetroSetEnvironmentDelegate>("retro_set_environment");
            _retro_set_video_refresh = GetDelegate<RetroSetVideoRefreshDelegate>("retro_set_video_refresh");
            _retro_set_audio_sample = GetDelegate<RetroSetAudioSampleDelegate>("retro_set_audio_sample");
            _retro_set_audio_sample_batch = GetDelegate<RetroSetAudioSampleBatchDelegate>("retro_set_audio_sample_batch");
            _retro_set_input_poll = GetDelegate<RetroSetInputPollDelegate>("retro_set_input_poll");
            _retro_set_input_state = GetDelegate<RetroSetInputStateDelegate>("retro_set_input_state");
            _retro_set_controller_port_device = GetDelegate<RetroSetControllerPortDeviceDelegate>("retro_set_controller_port_device");
            _retro_get_system_info = GetDelegate<RetroGetSystemInfoDelegate>("retro_get_system_info");
            _retro_get_system_av_info = GetDelegate<RetroGetSystemAvInfoDelegate>("retro_get_system_av_info");
            _retro_load_game = GetDelegate<RetroLoadGameDelegate>("retro_load_game");
            _retro_run = GetDelegate<RetroRunDelegate>("retro_run");
            _retro_unload_game = GetDelegate<RetroUnloadGameDelegate>("retro_unload_game");
            _retro_get_memory_data = GetDelegate<RetroGetMemoryDataDelegate>("retro_get_memory_data");
            _retro_get_memory_size = GetDelegate<RetroGetMemorySizeDelegate>("retro_get_memory_size");

            uint apiVer = _retro_api_version();
            Console.WriteLine($"[LibretroHost] Core loaded: {Path.GetFileName(coreDllPath)} (API v{apiVer})");
            return true;
        }

        public static void Init(string systemDir, string saveDir)
        {
            _systemDir = systemDir;
            _saveDir = saveDir;
            _contentDir = systemDir;

            _envDelegate = EnvironmentCallback;
            _videoDelegate = VideoRefreshCallback;
            _audioDelegate = AudioSampleCallback;
            _audioBatchDelegate = AudioSampleBatchCallback;
            _inputPollDelegate = InputPollCallback;
            _inputStateDelegate = InputStateCallback;

            _retro_set_environment(Marshal.GetFunctionPointerForDelegate(_envDelegate));
            _retro_set_video_refresh(Marshal.GetFunctionPointerForDelegate(_videoDelegate));
            _retro_set_audio_sample(Marshal.GetFunctionPointerForDelegate(_audioDelegate));
            _retro_set_audio_sample_batch(Marshal.GetFunctionPointerForDelegate(_audioBatchDelegate));
            _retro_set_input_poll(Marshal.GetFunctionPointerForDelegate(_inputPollDelegate));
            _retro_set_input_state(Marshal.GetFunctionPointerForDelegate(_inputStateDelegate));

            _retro_init();
            _coreInitialized = true;

            // inform core that port 0 contains a standard RetroPad
            _retro_set_controller_port_device(0, RETRO_DEVICE_JOYPAD);
            Console.WriteLine("[LibretroHost] Port 0 configured as JOYPAD (device 1)");

            var sysInfo = new RetroSystemInfo();
            _retro_get_system_info(ref sysInfo);
            string name = sysInfo.library_name != IntPtr.Zero ? Marshal.PtrToStringAnsi(sysInfo.library_name) : "?";
            string ver = sysInfo.library_version != IntPtr.Zero ? Marshal.PtrToStringAnsi(sysInfo.library_version) : "?";
            Console.WriteLine($"[LibretroHost] Core initialised: {name} {ver}");
        }

        public static bool LoadGame(string romPath)
        {
            _contentDir = Path.GetDirectoryName(Path.GetFullPath(romPath)) ?? _systemDir;

            var pathPtr = Marshal.StringToHGlobalAnsi(romPath);
            var gameInfo = new RetroGameInfo();
            gameInfo.path = pathPtr;
            gameInfo.data = IntPtr.Zero;
            gameInfo.size = UIntPtr.Zero;
            gameInfo.meta = IntPtr.Zero;

            _gameLoaded = false;
            bool ok = _retro_load_game(ref gameInfo);
            Marshal.FreeHGlobal(pathPtr);

            if (!ok)
            {
                Console.WriteLine($"[LibretroHost] Failed to load game: {romPath}");
                return false;
            }

            var avInfo = new RetroSystemAvInfo();
            _retro_get_system_av_info(ref avInfo);
            _fbWidth = (int)avInfo.geometry.base_width;
            _fbHeight = (int)avInfo.geometry.base_height;
            _fbPitch = _fbWidth;
            _fbPortrait = _fbHeight > _fbWidth;
            TargetFps = avInfo.timing.fps > 0 ? avInfo.timing.fps : 60.0;

            Console.WriteLine($"[LibretroHost] Game loaded: {Path.GetFileName(romPath)}");
            Console.WriteLine($"[LibretroHost]   Resolution: {_fbWidth}x{_fbHeight} @ {avInfo.timing.fps:F1} fps{(_fbPortrait ? " (portrait, will rotate)" : "")}");
            Console.WriteLine($"[LibretroHost]   Pixel format: {(_pixelFormat == RETRO_PIXEL_FORMAT_XRGB8888 ? "XRGB8888" : "RGB565")}");

            int rawSize = _fbWidth * _fbHeight;
            _framebuffer = new uint[rawSize];
            _rotatedBuffer = _fbPortrait ? new uint[rawSize] : null;
            _frameCount = 0;
            _gameLoaded = true;
            return true;
        }

        public static void RunFrame()
        {
            _retro_run();
            _frameCount++;
        }

        public static uint[] GetFramebuffer()
        {
            if (_framebuffer == null) return Array.Empty<uint>();

            if (!_fbPortrait)
                return _framebuffer;

            int srcW = _fbWidth, srcH = _fbHeight;
            for (int y = 0; y < srcH; y++)
            {
                for (int x = 0; x < srcW; x++)
                {
                    uint px = _framebuffer[y * srcW + x];
                    int dstX = srcH - 1 - y;
                    int dstY = x;
                    _rotatedBuffer[dstY * srcH + dstX] = px;
                }
            }

            return _rotatedBuffer;
        }

        public static void Shutdown()
        {
            // The manager may call Shutdown more than once during a failed load
            // or while changing content. Do not invoke delegates after the native
            // library has been released.
            if (_coreHandle == IntPtr.Zero)
            {
                _gameLoaded = false;
                _coreInitialized = false;
                return;
            }

            if (_gameLoaded && _retro_unload_game != null)
                _retro_unload_game();
            _gameLoaded = false;

            if (_coreInitialized && _retro_deinit != null)
                _retro_deinit();
            _coreInitialized = false;

            IntPtr handle = _coreHandle;
            _coreHandle = IntPtr.Zero;
            NativeLibrary.Free(handle);

            _keyboardCallback = null;
            _retro_api_version = null;
            _retro_init = null;
            _retro_deinit = null;
            _retro_set_environment = null;
            _retro_set_video_refresh = null;
            _retro_set_audio_sample = null;
            _retro_set_audio_sample_batch = null;
            _retro_set_input_poll = null;
            _retro_set_input_state = null;
            _retro_set_controller_port_device = null;
            _retro_get_system_info = null;
            _retro_get_system_av_info = null;
            _retro_load_game = null;
            _retro_run = null;
            _retro_unload_game = null;
            _retro_get_memory_data = null;
            _retro_get_memory_size = null;

            _fbPortrait = false;
            _rotatedBuffer = null;
            _framebuffer = null;
            Console.WriteLine("[LibretroHost] Core unloaded");
        }

        public static int FramebufferWidth => _fbPortrait ? _fbHeight : _fbWidth;
        public static int FramebufferHeight => _fbPortrait ? _fbWidth : _fbHeight;
        public static uint PixelFormat => _pixelFormat;
        public static int FrameCount => _frameCount;
        public static double TargetFps { get; private set; } = 60.0;
        public static double FrameTimeSeconds => 1.0 / TargetFps;

        public static bool IsLoaded => _coreHandle != IntPtr.Zero;

        private static bool EnvironmentCallback(uint cmd, IntPtr data)
        {
            if (_seenEnvCommands.Count < 40 && _seenEnvCommands.Add(cmd))
                Console.WriteLine($"[LibretroHost] EnvCmd: {cmd}");

            switch (cmd)
            {
                case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
                    _pixelFormat = (uint)Marshal.ReadInt32(data);
                    return true;

                case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
                    Marshal.WriteIntPtr(data, Marshal.StringToHGlobalAnsi(_systemDir));
                    return true;

                case RETRO_ENVIRONMENT_GET_CONTENT_DIRECTORY:
                    Marshal.WriteIntPtr(data, Marshal.StringToHGlobalAnsi(_contentDir));
                    return true;

                case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
                    Marshal.WriteIntPtr(data, Marshal.StringToHGlobalAnsi(_saveDir));
                    return true;

                case RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION:
                    if (data != IntPtr.Zero) Marshal.WriteInt32(data, 1);
                    return true;

                case RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL:
                    return true;

                case RETRO_ENVIRONMENT_GET_LANGUAGE:
                    if (data != IntPtr.Zero) Marshal.WriteInt32(data, 0);
                    return true;

                case RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS:
                case RETRO_ENVIRONMENT_SET_CONTROLLER_INFO:
                case RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS:
                case RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME:
                    return true;

                case RETRO_ENVIRONMENT_SET_KEYBOARD_CALLBACK:
                {
                    var cb = (RetroKeyboardCallback)Marshal.PtrToStructure(data, typeof(RetroKeyboardCallback));
                    _keyboardCallback = cb.callback != IntPtr.Zero
                        ? Marshal.GetDelegateForFunctionPointer<RetroKeyboardEventDelegate>(cb.callback)
                        : null;
                    Console.WriteLine($"[LibretroHost] Keyboard callback: {(_keyboardCallback != null)}");
                    return _keyboardCallback != null;
                }

                case RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
                    return false;

                case RETRO_ENVIRONMENT_GET_VARIABLE:
                    if (data != IntPtr.Zero)
                    {
                        IntPtr keyPtr = Marshal.ReadIntPtr(data);
                        IntPtr valuePtrPtr = data + IntPtr.Size;
                        string key = keyPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(keyPtr) : null;

                        if (key != null && _seenVarKeys.Add(key))
                            Console.WriteLine($"[LibretroHost] GET_VARIABLE: {key}");

                        string val = (key == "mame_boot_to_osd" || key == "mame_boot_to_bios")
                            ? "disabled" : "";

                        if (!_variableValues.TryGetValue(val, out IntPtr valPtr))
                        {
                            valPtr = Marshal.StringToHGlobalAnsi(val);
                            _variableValues[val] = valPtr;
                        }
                        Marshal.WriteIntPtr(valuePtrPtr, valPtr);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private static void VideoRefreshCallback(IntPtr data, uint width, uint height, UIntPtr pitch)
        {
            _fbWidth = (int)width;
            _fbHeight = (int)height;
            _fbPitch = (int)pitch.ToUInt64();

            if (data == IntPtr.Zero)
                return;

            int needed = _fbWidth * _fbHeight;
            if (_framebuffer == null || _framebuffer.Length < needed)
                _framebuffer = new uint[needed];
            if (_rotatedBuffer == null || _rotatedBuffer.Length < needed)
                _rotatedBuffer = new uint[needed];

            if (_pixelFormat == RETRO_PIXEL_FORMAT_XRGB8888)
            {
                Marshal.Copy(data, (int[])(object)_framebuffer, 0, needed);
            }
            else
            {
                uint rowPitch = (uint)_fbPitch;
                if (rowPitch == 0) rowPitch = width * 2;

                byte[] raw = new byte[rowPitch * height];
                Marshal.Copy(data, raw, 0, raw.Length);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = (int)(y * rowPitch + x * 2);
                        ushort px = (ushort)(raw[srcIdx] | (raw[srcIdx + 1] << 8));
                        byte r = (byte)((px >> 11) & 0x1F);
                        byte g = (byte)((px >> 5) & 0x3F);
                        byte b = (byte)(px & 0x1F);
                        _framebuffer[y * (int)width + x] = 0xFF000000u |
                            (uint)((r << 3) | (r >> 2)) << 16 |
                            (uint)((g << 2) | (g >> 4)) << 8 |
                            (uint)((b << 3) | (b >> 2));
                    }
                }
            }
        }

        private static void AudioSampleCallback(short left, short right) { }
        private static UIntPtr AudioSampleBatchCallback(IntPtr data, UIntPtr frames) { return frames; }

        private static void InputPollCallback()
        {
            if (_keyboardCallback == null) return;

            // Forward the two cached action states through the keyboard
            // callback using the configured virtual-key values.
            // VK_LCONTROL=0x11 and VK_LMENU=0x12 are the Windows virtual-key
            // codes used by this input path.
            bool ctrl = _inpBtnB, alt = _inpBtnA;
            if (ctrl != _prevCtrl) { _keyboardCallback(ctrl, 0x11, 0, 0); _prevCtrl = ctrl; }
            if (alt  != _prevAlt)  { _keyboardCallback(alt,  0x12, 0, 0); _prevAlt  = alt;  }
        }

        private static short InputStateCallback(uint port, uint device, uint index, uint id)
        {
            short result = 0;

            if (device == RETRO_DEVICE_JOYPAD && port == 0)
            {
                // RETRO_DEVICE_ID_JOYPAD_MASK is a pressed-button bitmask,
                // not a boolean capability flag. The second cached action is
                // exposed through more than one button slot for compatibility
                // with cores that use different layouts.
                if (id == RETRO_DEVICE_ID_JOYPAD_MASK)
                {
                    int mask = 0;
                    if (_inpBtnB)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_B;
                    if (_inpBtnA)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_Y;
                    if (_inpCoin)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_SELECT;
                    if (_inpStart) mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_START;
                    if (_inpUp)    mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_UP;
                    if (_inpDown)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_DOWN;
                    if (_inpLeft)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_LEFT;
                    if (_inpRight) mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_RIGHT;
                    if (_inpBtnA)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_A;
                    result = unchecked((short)mask);
                }
                else
                {
                    result = id switch
                    {
                        RETRO_DEVICE_ID_JOYPAD_B => _inpBtnB ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_Y => _inpBtnA ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_SELECT => _inpCoin ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_START => _inpStart ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_UP => _inpUp ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_DOWN => _inpDown ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_LEFT => _inpLeft ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_RIGHT => _inpRight ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_A => _inpBtnA ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_X => 0,
                        _ => 0
                    };
                }
            }
            else if (device == RETRO_DEVICE_KEYBOARD)
            {
                // Physical keyboard queries use RETROK_* IDs. Controller state
                // is handled by the RetroPad path.
                result = _keyMap.TryGetValue(id, out KeyCode key) && IsWinKeyDown(key)
                    ? (short)1 : (short)0;
            }
            else if (device == RETRO_DEVICE_ANALOG && port == 0)
            {
                // Analog input queries use this path for directional axes.
                result = (int)id switch
                {
                    0 => _inpLeft ? (short)-0x7FFF : _inpRight ? (short)0x7FFF : (short)0,
                    1 => _inpUp ? (short)-0x7FFF : _inpDown ? (short)0x7FFF : (short)0,
                    _ => 0
                };
            }

            return result;
        }

        private static T GetDelegate<T>(string name) where T : Delegate
        {
            IntPtr ptr = NativeLibrary.GetExport(_coreHandle, name);
            if (ptr == IntPtr.Zero)
                throw new Exception($"Failed to resolve export '{name}' from {_corePath}");
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }
    }
}
