using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ArcadeParadiseFreePlayMod
{
    public static class LibretroHost
    {
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

        private static int _pollCount;
        private static int _inputQueryCount;
        private static int _kbdQueryCount;
        private static int _joyQueryCount;
        private static int _otherQueryCount;
        private static int _inputNonZeroCount;
        private static HashSet<uint> _loggedRetroKeys = new HashSet<uint>();
        private static HashSet<uint> _seenEnvCommands = new HashSet<uint>();

        private static RetroKeyboardEventDelegate _keyboardCallback;
        private static readonly Dictionary<uint, bool> _prevKeyState = new Dictionary<uint, bool>();

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
            { 275,   KeyCode.RightArrow },
            { 276,   KeyCode.LeftArrow },
            { 49,    KeyCode.Alpha1 },
            { 53,    KeyCode.Alpha5 },
            { 97,  KeyCode.A }, { 98,  KeyCode.B }, { 99,  KeyCode.C },
            { 100, KeyCode.D }, { 101, KeyCode.E }, { 102, KeyCode.F },
            { 103, KeyCode.G }, { 104, KeyCode.H }, { 105, KeyCode.I },
            { 106, KeyCode.J }, { 107, KeyCode.K }, { 108, KeyCode.L },
            { 109, KeyCode.M }, { 110, KeyCode.N }, { 111, KeyCode.O },
            { 112, KeyCode.P }, { 113, KeyCode.Q }, { 114, KeyCode.R },
            { 115, KeyCode.S }, { 116, KeyCode.T }, { 117, KeyCode.U },
            { 118, KeyCode.V }, { 119, KeyCode.W }, { 120, KeyCode.X },
            { 121, KeyCode.Y }, { 122, KeyCode.Z },
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
        public const uint RETRO_DEVICE_JOYPAD = 0;
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

            Console.WriteLine($"[LibretroHost] Game loaded: {Path.GetFileName(romPath)}");
            Console.WriteLine($"[LibretroHost]   Resolution: {_fbWidth}x{_fbHeight} @ {avInfo.timing.fps:F1} fps{(_fbPortrait ? " (portrait, will rotate)" : "")}");
            Console.WriteLine($"[LibretroHost]   Pixel format: {(_pixelFormat == RETRO_PIXEL_FORMAT_XRGB8888 ? "XRGB8888" : "RGB565")}");

            int rawSize = _fbWidth * _fbHeight;
            _framebuffer = new uint[rawSize];
            _rotatedBuffer = _fbPortrait ? new uint[rawSize] : null;
            _frameCount = 0;
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

            // rotate 90 CW into _rotatedBuffer so the caller always gets landscape-oriented data. Uses the same rotation that RetroArch applies to vertical arcade games
            int srcW = _fbWidth, srcH = _fbHeight;
            for (int y = 0; y < srcH; y++)
            {
                for (int x = 0; x < srcW; x++)
                {
                    uint px = _framebuffer[y * srcW + x];
                    // 90 CW: src(x,y) → dst(srcH-1-y, x)
                    int dstX = srcH - 1 - y;
                    int dstY = x;
                    _rotatedBuffer[dstY * srcH + dstX] = px;
                }
            }

            return _rotatedBuffer;
        }

        public static void Shutdown()
        {
            _retro_unload_game();
            _retro_deinit();
            if (_coreHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_coreHandle);
                _coreHandle = IntPtr.Zero;
            }
            _fbPortrait = false;
            _rotatedBuffer = null;
            Console.WriteLine("[LibretroHost] Core unloaded");
        }

        public static int FramebufferWidth => _fbPortrait ? _fbHeight : _fbWidth;
        public static int FramebufferHeight => _fbPortrait ? _fbWidth : _fbHeight;
        public static uint PixelFormat => _pixelFormat;
        public static int FrameCount => _frameCount;

        /// <summary>True if a core is loaded and ready to run frames.</summary>
        public static bool IsLoaded => _coreHandle != IntPtr.Zero;

        private static bool EnvironmentCallback(uint cmd, IntPtr data)
        {
            if (_seenEnvCommands.Count < 30 && _seenEnvCommands.Add(cmd))
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

                case RETRO_ENVIRONMENT_GET_LED_INTERFACE:
                case RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
                case RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY:
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
            _pollCount++;
            if (_keyboardCallback == null) return;

            foreach (var kvp in _keyMap)
            {
                uint retroKey = kvp.Key;
                KeyCode unityKey = kvp.Value;
                bool pressed = Input.GetKey(unityKey);
                _prevKeyState.TryGetValue(retroKey, out bool wasPressed);

                if (pressed != wasPressed)
                {
                    _keyboardCallback(pressed, retroKey, 0, 0);
                    _prevKeyState[retroKey] = pressed;
                }
            }
        }

        private static short InputStateCallback(uint port, uint device, uint index, uint id)
        {
            _inputQueryCount++;
            if (device == 1) _kbdQueryCount++;
            else if (device == 0) _joyQueryCount++;
            else _otherQueryCount++;

            if (device == 1)
            {
                if (_keyMap.TryGetValue(id, out KeyCode key))
                    return Input.GetKey(key) ? (short)1 : (short)0;

                if (port == 0)
                {
                    return id switch
                    {
                        0 => Input.GetKey(KeyCode.Return) ? (short)1 : (short)0,
                        1 => Input.GetKey(KeyCode.DownArrow) ? (short)1 : (short)0,
                        2 => Input.GetKey(KeyCode.LeftArrow) ? (short)1 : (short)0,
                        3 => Input.GetKey(KeyCode.Return) ? (short)1 : (short)0,
                        4 => Input.GetKey(KeyCode.UpArrow) ? (short)1 : (short)0,
                        5 => Input.GetKey(KeyCode.DownArrow) ? (short)1 : (short)0,
                        6 => Input.GetKey(KeyCode.RightArrow) ? (short)1 : (short)0,
                        7 => Input.GetKey(KeyCode.UpArrow) ? (short)1 : (short)0,
                        10 => Input.GetKey(KeyCode.Alpha5) ? (short)1 : (short)0,
                        11 => Input.GetKey(KeyCode.Alpha1) ? (short)1 : (short)0,
                        _ => 0
                    };
                }
                return 0;
            }

            if (device == 0 && port == 0)
            {
                return id switch
                {
                    RETRO_DEVICE_ID_JOYPAD_UP => Input.GetKey(KeyCode.UpArrow) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_DOWN => Input.GetKey(KeyCode.DownArrow) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_LEFT => Input.GetKey(KeyCode.LeftArrow) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_RIGHT => Input.GetKey(KeyCode.RightArrow) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_START => (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Alpha1)) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_SELECT => (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Alpha5)) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_A => (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.X)) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_B => (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.Z)) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_X => (Input.GetKey(KeyCode.S)) ? (short)1 : (short)0,
                    RETRO_DEVICE_ID_JOYPAD_Y => (Input.GetKey(KeyCode.A)) ? (short)1 : (short)0,
                    _ => 0
                };
            }

            return 0;
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
