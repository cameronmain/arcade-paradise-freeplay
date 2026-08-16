using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
        private static bool _inpBtnX, _inpBtnY, _inpL, _inpR, _inpL2, _inpR2, _inpL3, _inpR3;
        private static short _inpL2Value, _inpR2Value;
        private static short _inpAnalogLeftX, _inpAnalogLeftY, _inpAnalogRightX, _inpAnalogRightY;
        private static bool _inputEnabled;

        // XInput gives Unity's legacy input path the same standard controller
        // semantics that RetroArch uses (including analog triggers). Non-XInput
        // devices still use the Unity joystick fallback below.
        private static bool _xInputUnavailable;
        private static int _xInputUser = -1;
        private static bool _xInputLegacyDll;
        private static bool _menuConfirmWasDown;
        private static bool _menuCancelWasDown;

        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_START = 0x0010;
        private const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0400;
        private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0800;
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;
        private const ushort XINPUT_GAMEPAD_X = 0x4000;
        private const ushort XINPUT_GAMEPAD_Y = 0x8000;
        private const byte XINPUT_TRIGGER_THRESHOLD = 30;

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort buttons;
            public byte leftTrigger;
            public byte rightTrigger;
            public short thumbLX;
            public short thumbLY;
            public short thumbRX;
            public short thumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint packetNumber;
            public XInputGamepad gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint userIndex, ref XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetStateLegacy(uint userIndex, ref XInputState state);

        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

        private static void ClearInputState()
        {
            _inpUp = _inpDown = _inpLeft = _inpRight = false;
            _inpStart = _inpCoin = _inpBtnA = _inpBtnB = false;
            _inpBtnX = _inpBtnY = _inpL = _inpR = false;
            _inpL2 = _inpR2 = _inpL3 = _inpR3 = false;
            _inpL2Value = _inpR2Value = 0;
            _inpAnalogLeftX = _inpAnalogLeftY = 0;
            _inpAnalogRightX = _inpAnalogRightY = 0;
        }

        private static float ReadJoystickAxis(int axis)
        {
            float strongest = 0f;
            for (int joystick = 1; joystick <= 4; joystick++)
            {
                try
                {
                    float value = Input.GetAxisRaw($"Joystick{joystick}Axis{axis}");
                    if (Mathf.Abs(value) > Mathf.Abs(strongest))
                        strongest = value;
                }
                catch
                {
                    // missing legacy axis is normal for some controllers
                }
            }
            return strongest;
        }

        private static short ToRetroAxis(float value)
        {
            return (short)Mathf.Clamp(Mathf.RoundToInt(value * 32767f), -32767, 32767);
        }

        private static bool TryReadXInput(out XInputState state)
        {
            state = new XInputState();
            if (_xInputUnavailable)
                return false;

            try
            {
                if (!_xInputLegacyDll)
                {
                    if (_xInputUser >= 0)
                    {
                        uint result = XInputGetState14((uint)_xInputUser, ref state);
                        if (result == ERROR_SUCCESS)
                            return true;
                        if (result != ERROR_DEVICE_NOT_CONNECTED)
                            _xInputUser = -1;
                    }

                    for (uint user = 0; user < 4 && _xInputUser < 0; user++)
                    {
                        uint result = XInputGetState14(user, ref state);
                        if (result == ERROR_SUCCESS)
                        {
                            _xInputUser = (int)user;
                            return true;
                        }
                    }
                }
                else
                {
                    if (_xInputUser >= 0)
                    {
                        uint result = XInputGetStateLegacy((uint)_xInputUser, ref state);
                        if (result == ERROR_SUCCESS)
                            return true;
                        _xInputUser = -1;
                    }

                    for (uint user = 0; user < 4 && _xInputUser < 0; user++)
                    {
                        uint result = XInputGetStateLegacy(user, ref state);
                        if (result == ERROR_SUCCESS)
                        {
                            _xInputUser = (int)user;
                            return true;
                        }
                    }
                }
            }
            catch (DllNotFoundException)
            {
                if (!_xInputLegacyDll)
                {
                    _xInputLegacyDll = true;
                    return TryReadXInput(out state);
                }
                _xInputUnavailable = true;
            }
            catch (EntryPointNotFoundException)
            {
                if (!_xInputLegacyDll)
                {
                    _xInputLegacyDll = true;
                    return TryReadXInput(out state);
                }
                _xInputUnavailable = true;
            }

            return false;
        }

        private static bool IsPressed(ushort buttons, ushort button)
        {
            return (buttons & button) != 0;
        }

        private static bool MenuButtonDown(ushort xInputButton, KeyCode fallbackKey, ref bool wasDown)
        {
            bool isDown;
            XInputState xinput;
            if (TryReadXInput(out xinput))
                isDown = IsPressed(xinput.gamepad.buttons, xInputButton);
            else
                isDown = Input.GetKey(fallbackKey);

            bool pressed = isDown && !wasDown;
            wasDown = isDown;
            return pressed;
        }

        public static bool MenuConfirmDown()
        {
            return MenuButtonDown(XINPUT_GAMEPAD_A, KeyCode.JoystickButton1, ref _menuConfirmWasDown);
        }

        public static bool MenuCancelDown()
        {
            return MenuButtonDown(XINPUT_GAMEPAD_B, KeyCode.JoystickButton0, ref _menuCancelWasDown);
        }

        public static void ResetMenuInput()
        {
            _menuConfirmWasDown = false;
            _menuCancelWasDown = false;
        }

        private static float TriggerFromUnityAxes(float primary, float alternate)
        {
            // unity exposes DirectInput trigger axes as positive values on most devices. 
            // dont use absolute value here: on devices that  expose a resting signed axis, -1 means released, not pressed
            return Mathf.Clamp01(Mathf.Max(primary, alternate));
        }

        private static void PollXInputFallback(out bool connected)
        {
            connected = false;
            XInputState xinput;
            if (TryReadXInput(out xinput))
            {
                connected = true;
                ushort buttons = xinput.gamepad.buttons;
                _inpUp |= IsPressed(buttons, XINPUT_GAMEPAD_DPAD_UP) || xinput.gamepad.thumbLY > 16000;
                _inpDown |= IsPressed(buttons, XINPUT_GAMEPAD_DPAD_DOWN) || xinput.gamepad.thumbLY < -16000;
                _inpLeft |= IsPressed(buttons, XINPUT_GAMEPAD_DPAD_LEFT) || xinput.gamepad.thumbLX < -16000;
                _inpRight |= IsPressed(buttons, XINPUT_GAMEPAD_DPAD_RIGHT) || xinput.gamepad.thumbLX > 16000;
                _inpStart |= IsPressed(buttons, XINPUT_GAMEPAD_START);
                _inpCoin |= IsPressed(buttons, XINPUT_GAMEPAD_BACK);

                // Trying to set up a universal control scheme is a absolute pain, 
                // currently matching the controller's retroarch/arcade action order:
                // physical Xbox B feeds libretro A, and physical Xbox A feeds libretro B. 
                // Keep keyboard bindings independent below.
                _inpBtnA |= IsPressed(buttons, XINPUT_GAMEPAD_B);
                _inpBtnB |= IsPressed(buttons, XINPUT_GAMEPAD_A);
                _inpBtnX |= IsPressed(buttons, XINPUT_GAMEPAD_X);
                _inpBtnY |= IsPressed(buttons, XINPUT_GAMEPAD_Y);
                _inpL |= IsPressed(buttons, XINPUT_GAMEPAD_LEFT_SHOULDER);
                _inpR |= IsPressed(buttons, XINPUT_GAMEPAD_RIGHT_SHOULDER);
                _inpL2Value = ToRetroAxis(xinput.gamepad.leftTrigger / 255f);
                _inpR2Value = ToRetroAxis(xinput.gamepad.rightTrigger / 255f);
                _inpL2 |= _inpL2Value >= ToRetroAxis(XINPUT_TRIGGER_THRESHOLD / 255f);
                _inpR2 |= _inpR2Value >= ToRetroAxis(XINPUT_TRIGGER_THRESHOLD / 255f);
                _inpL3 |= IsPressed(buttons, XINPUT_GAMEPAD_LEFT_THUMB);
                _inpR3 |= IsPressed(buttons, XINPUT_GAMEPAD_RIGHT_THUMB);
                _inpAnalogLeftX = ToRetroAxis(xinput.gamepad.thumbLX / 32767f);
                _inpAnalogLeftY = ToRetroAxis(-xinput.gamepad.thumbLY / 32768f);
                _inpAnalogRightX = ToRetroAxis(xinput.gamepad.thumbRX / 32767f);
                _inpAnalogRightY = ToRetroAxis(-xinput.gamepad.thumbRY / 32768f);
            }
        }

        private static void PollUnityAnalogFallback()
        {
            float axisH = Input.GetAxis("Horizontal");
            float axisV = Input.GetAxis("Vertical");
            float rightX = ReadJoystickAxis(3);
            float rightY = ReadJoystickAxis(4);
            float triggerLeft = TriggerFromUnityAxes(ReadJoystickAxis(5), ReadJoystickAxis(9));
            float triggerRight = TriggerFromUnityAxes(ReadJoystickAxis(6), ReadJoystickAxis(10));

            _inpAnalogLeftX = ToRetroAxis(Mathf.Clamp(axisH, -1f, 1f));
            _inpAnalogLeftY = ToRetroAxis(Mathf.Clamp(-axisV, -1f, 1f));
            _inpAnalogRightX = ToRetroAxis(Mathf.Clamp(rightX, -1f, 1f));
            _inpAnalogRightY = ToRetroAxis(Mathf.Clamp(-rightY, -1f, 1f));
            _inpL2Value = ToRetroAxis(triggerLeft);
            _inpR2Value = ToRetroAxis(triggerRight);
            _inpL2 |= _inpL2Value >= 16384;
            _inpR2 |= _inpR2Value >= 16384;
        }

        public static void SetInputEnabled(bool enabled)
        {
            _inputEnabled = enabled;
            if (!enabled)
                ClearInputState();
        }

        public static void PollInput()
        {
            if (!_inputEnabled)
            {
                ClearInputState();
                return;
            }

            ClearInputState();

            bool kbUp    = IsWinKeyDown(KeyCode.UpArrow)    || IsWinKeyDown(KeyCode.W);
            bool kbDown  = IsWinKeyDown(KeyCode.DownArrow)  || IsWinKeyDown(KeyCode.S);
            bool kbLeft  = IsWinKeyDown(KeyCode.LeftArrow)  || IsWinKeyDown(KeyCode.A);
            bool kbRight = IsWinKeyDown(KeyCode.RightArrow) || IsWinKeyDown(KeyCode.D);
            bool kbStart = IsWinKeyDown(KeyCode.Return)     || IsWinKeyDown(KeyCode.Alpha1);
            bool kbCoin  = IsWinKeyDown(KeyCode.Alpha5);
            bool kbA     = IsWinKeyDown(KeyCode.LeftAlt)    || IsWinKeyDown(KeyCode.X);
            bool kbB     = IsWinKeyDown(KeyCode.LeftControl)|| IsWinKeyDown(KeyCode.Z);

            // Keep keyboard input available regardless of controller type.
            // Controller input is selected as one complete source below so a physical button cannot be interpreted once by XInput and again by unitys differently ordered legacy button slots
            _inpUp    = kbUp;
            _inpDown  = kbDown;
            _inpLeft  = kbLeft;
            _inpRight = kbRight;
            _inpStart = kbStart;
            _inpCoin  = kbCoin;
            _inpBtnA  = kbA;
            _inpBtnB  = kbB;
            _inpBtnX  = false;
            _inpBtnY  = kbA;
            _inpL     = _inpR = _inpL3 = _inpR3 = false;

            bool xInputConnected;
            PollXInputFallback(out xInputConnected);
            if (!xInputConnected)
            {
                // Only query unitys legacy joystick API when there is no XInput device, 
                // so it cannot contend with the games own controller input handling and add perframe load while a controller is in use
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
                bool padX     = Input.GetKey(KeyCode.JoystickButton2);
                bool padY     = Input.GetKey(KeyCode.JoystickButton3);
                bool padL     = Input.GetKey(KeyCode.JoystickButton4);
                bool padR     = Input.GetKey(KeyCode.JoystickButton5);
                bool padL3    = Input.GetKey(KeyCode.JoystickButton8);
                bool padR3    = Input.GetKey(KeyCode.JoystickButton9);

                _inpUp    |= padUp;
                _inpDown  |= padDown;
                _inpLeft  |= padLeft;
                _inpRight |= padRight;
                _inpStart |= padStart;
                _inpCoin  |= padCoin;
                _inpBtnA  |= padA;
                _inpBtnB  |= padB;
                _inpBtnX   = padX;
                _inpBtnY  |= padY;
                _inpL      = padL;
                _inpR      = padR;
                _inpL3     = padL3;
                _inpR3     = padR3;
                PollUnityAnalogFallback();
            }
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
        private static byte[] _rawVideoBuffer;
        private static bool _fbPortrait;
        private static string _systemDir;
        private static string _saveDir;
        private static string _contentDir;
        private static int _frameCount;
        private static bool _coreInitialized;
        private static bool _gameLoaded;

        // libretro produces signed 16-bit interleaved stereo PCM from the retro_run() thread
        // unity consumes float PCM on its audio thread
        private static AudioSource _audioSource;
        private static AudioClip _audioClip;
        private static AudioListener _audioListener;
        private static AudioRingBuffer _audioBuffer;
        private static double _audioSampleRate = 44100.0;
        private static double _audioResampleRatio = 1.0;
        private static bool _audioPlayingMode;
        private static float _audioGain = 1f;
        private static float _audioPan; // -1 (full left) .. +1 (full right), applied on the audio thread
        private static float _cabinetDistance;
        private const double AUDIO_BUFFER_SECONDS = 0.30;
        private const float ATTRACT_GAIN = 0.7f; // attract audio is injected via OnAudioFilterRead, which bypasses unity's 3D rolloff, so cap its level here

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
        public const uint RETRO_DEVICE_ID_JOYPAD_L = 10;
        public const uint RETRO_DEVICE_ID_JOYPAD_R = 11;
        public const uint RETRO_DEVICE_ID_JOYPAD_L2 = 12;
        public const uint RETRO_DEVICE_ID_JOYPAD_R2 = 13;
        public const uint RETRO_DEVICE_ID_JOYPAD_L3 = 14;
        public const uint RETRO_DEVICE_ID_JOYPAD_R3 = 15;

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
            _audioSampleRate = avInfo.timing.sample_rate > 0 ? avInfo.timing.sample_rate : 44100.0;

            Console.WriteLine($"[LibretroHost] Game loaded: {Path.GetFileName(romPath)}");
            Console.WriteLine($"[LibretroHost]   Resolution: {_fbWidth}x{_fbHeight} @ {avInfo.timing.fps:F1} fps{(_fbPortrait ? " (portrait, will rotate)" : "")}");
            Console.WriteLine($"[LibretroHost]   Audio: {_audioSampleRate:F0} Hz stereo");
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

        public static void ReadAudio(Il2CppStructArray<float> data, int channels)
        {
            var buffer = Volatile.Read(ref _audioBuffer);
            if (buffer == null)
            {
                for (int i = 0; i < data.Length; i++)
                    data[i] = 0f;
                return;
            }

            buffer.Read(data, channels, _audioResampleRatio);

            float gain = Volatile.Read(ref _audioGain);
            float pan = Volatile.Read(ref _audioPan);

            if (channels >= 2)
            {
                // balance panning keeps the centre at full level and fades the far channel;
                // centred (pan == 0) leaves both channels untouched so play mode is unaffected
                float gainL = gain * (pan <= 0f ? 1f : 1f - pan);
                float gainR = gain * (pan >= 0f ? 1f : 1f + pan);
                if (gainL != 1f || gainR != 1f)
                {
                    for (int i = 0; i + 1 < data.Length; i += 2)
                    {
                        data[i]     *= gainL;
                        data[i + 1] *= gainR;
                    }
                }
            }
            else if (gain != 1f)
            {
                for (int i = 0; i < data.Length; i++)
                    data[i] *= gain;
            }
        }

        /// <summary>
        /// Creates the cabinet's spatialised speaker source after libretro has reported the ROM's native audio rate. 
        /// The cabinet AudioSource drives Unity's filter callback; libretro only writes PCM into the buffer.
        /// </summary>
        public static void StartAudio(GameObject cabinet)
        {
            StopAudio();

            if (cabinet == null)
                return;

            int sampleRate = (int)Math.Round(_audioSampleRate);
            if (sampleRate < 4000 || sampleRate > 192000)
                sampleRate = 44100;

            int outputSampleRate = AudioSettings.outputSampleRate;
            if (outputSampleRate < 4000 || outputSampleRate > 192000)
                outputSampleRate = sampleRate;

            _audioResampleRatio = sampleRate / (double)outputSampleRate;
            int bufferSamples = Math.Max(4, (int)Math.Round(sampleRate * 2 * AUDIO_BUFFER_SECONDS));
            var buffer = new AudioRingBuffer(bufferSamples);
            Interlocked.Exchange(ref _audioBuffer, buffer);

            try
            {
                _audioSource = cabinet.AddComponent<AudioSource>();
                _audioListener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                _audioSource.spatialBlend = 1f;
                _audioSource.minDistance = 1f;
                _audioSource.maxDistance = 3f;
                _audioSource.rolloffMode = AudioRolloffMode.Custom;
                _audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                    new AnimationCurve(
                        new Keyframe(1f, 1f),
                        new Keyframe(2f, 0.7f),
                        new Keyframe(3f, 0f)));
                _audioSource.playOnAwake = false;
                _audioSource.loop = true;
                _audioSource.volume = 1f;

                // the clip is only a silent clock for Unitys audio pipeline
                // EmulatorArcadeManager.OnAudioFilterRead replaces its samples from the libretro ring buffer on Unitys audio thread
                _audioClip = AudioClip.Create(
                    "LibretroCabinetAudioClock",
                    outputSampleRate,
                    2,
                    outputSampleRate,
                    false);
                _audioSource.clip = _audioClip;
                _audioSource.Play();
                SetAudioPlayMode(false);

                Console.WriteLine($"[LibretroHost] Cabinet audio started: {sampleRate} Hz -> {outputSampleRate} Hz, " +
                                  $"buffer={AUDIO_BUFFER_SECONDS * 1000:F0}ms, spatialized 1-3m");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LibretroHost] Cabinet audio setup failed: {ex.Message}");
                StopAudio();
            }
        }

        public static void SetAudioPlayMode(bool playing)
        {
            SetInputEnabled(playing);
            _audioPlayingMode = playing;
            Volatile.Write(ref _audioGain, 1f);
            Volatile.Write(ref _audioPan, 0f);
            if (_audioSource == null)
                return;

            _audioSource.spatialBlend = playing ? 0f : 1f;
            _audioSource.volume = playing ? 2f : 1f;
        }

        public static void UpdateAudioSpatialization()
        {
            if (_audioSource == null || _audioPlayingMode)
                return;

            if (_audioListener == null)
                _audioListener = UnityEngine.Object.FindObjectOfType<AudioListener>();

            Transform listener = _audioListener != null
                ? _audioListener.transform
                : Camera.main?.transform;
            if (listener == null)
            {
                // fail closed rather than allowing attract audio to become a global source if the scene has not exposed its listener yet
                Volatile.Write(ref _audioGain, 0f);
                _audioSource.volume = 0f;
                return;
            }

            float distance = Vector3.Distance(_audioSource.transform.position, listener.position);
            _cabinetDistance = distance;

            // manual stereo panning: OnAudioFilterRead bypasses unity's 3D panning,
            // so derive left/right from the listener's facing direction
            Vector3 toSource = _audioSource.transform.position - listener.position;
            float lateral = toSource.sqrMagnitude > 0.0001f
                ? Vector3.Dot(toSource.normalized, listener.right)
                : 0f;
            Volatile.Write(ref _audioPan, Mathf.Clamp(lateral, -1f, 1f));

            float gain;
            if (distance <= 2f)
                gain = 1f;
            else if (distance >= AttractFreezeRange)
                gain = 0f;
            else if (distance <= 4f)
                gain = Mathf.Lerp(1f, 0.7f, (distance - 2f) / 2f);
            else
                gain = Mathf.Lerp(0.7f, 0f, (distance - 4f) / 2f);
            gain *= ATTRACT_GAIN;

            // OnAudioFilterRead injects samples after part of unitys normal AudioSource path, so enforce rolloff here on the main thread. 
            // guarantees silence beyond the cabinets attract-mode range without touching Unity from the audio thread
            Volatile.Write(ref _audioGain, gain);
            _audioSource.volume = gain;
        }

        private static void StopAudio()
        {
            Interlocked.Exchange(ref _audioBuffer, null);

            if (_audioSource != null)
            {
                _audioSource.Stop();
                UnityEngine.Object.Destroy(_audioSource);
                _audioSource = null;
            }

            if (_audioClip != null)
            {
                UnityEngine.Object.Destroy(_audioClip);
                _audioClip = null;
            }

            _audioListener = null;
            _audioPlayingMode = false;
            Volatile.Write(ref _audioGain, 1f);
            _audioResampleRatio = 1.0;
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
            // Stop Unity audio and release emulator input before unloading the
            // native core. This also handles failed/repeated shutdowns where no
            // core handle remains.
            SetInputEnabled(false);
            StopAudio();

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
            _prevCtrl = false;
            _prevAlt = false;
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
            _rawVideoBuffer = null;
            Console.WriteLine("[LibretroHost] Core unloaded");
        }

        public static int FramebufferWidth => _fbPortrait ? _fbHeight : _fbWidth;
        public static int FramebufferHeight => _fbPortrait ? _fbWidth : _fbHeight;
        public static uint PixelFormat => _pixelFormat;
        public static float CabinetDistance => _cabinetDistance;
        public const float AttractFreezeRange = 6f; // freeze the attract demo beyond this distance
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

                int rawLength = checked((int)(rowPitch * height));
                if (_rawVideoBuffer == null || _rawVideoBuffer.Length < rawLength)
                    _rawVideoBuffer = new byte[rawLength];
                Marshal.Copy(data, _rawVideoBuffer, 0, rawLength);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = (int)(y * rowPitch + x * 2);
                        ushort px = (ushort)(_rawVideoBuffer[srcIdx] | (_rawVideoBuffer[srcIdx + 1] << 8));
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

        private static void AudioSampleCallback(short left, short right)
        {
            var buffer = Volatile.Read(ref _audioBuffer);
            buffer?.Write(left, right);
        }

        private static UIntPtr AudioSampleBatchCallback(IntPtr data, UIntPtr frames)
        {
            var buffer = Volatile.Read(ref _audioBuffer);
            if (buffer == null || data == IntPtr.Zero)
                return frames;

            ulong frameCount = frames.ToUInt64();
            int accepted = buffer.WriteBatch(data, frameCount > int.MaxValue ? int.MaxValue : (int)frameCount);
            return (UIntPtr)accepted;
        }

        private sealed class AudioRingBuffer
        {
            private readonly float[] _samples;
            private readonly int _capacity;
            private int _writePosition;
            private int _readPosition;

            public AudioRingBuffer(int requestedSampleCapacity)
            {
                // keep the stereo frames intact and leave one sample slot empty so that equal read/write positions unambiguously mean "empty"
                _capacity = Math.Max(4, requestedSampleCapacity & ~1);
                _samples = new float[_capacity];
            }

            public void Write(short left, short right)
            {
                int write = _writePosition;
                int read = Volatile.Read(ref _readPosition);
                int available = write >= read
                    ? _capacity - (write - read) - 1
                    : read - write - 1;

                if (available < 2)
                    return;

                _samples[write] = left / 32768f;
                write = (write + 1) % _capacity;
                _samples[write] = right / 32768f;
                Volatile.Write(ref _writePosition, (write + 1) % _capacity);
            }

            public int WriteBatch(IntPtr data, int frames)
            {
                if (frames <= 0)
                    return 0;

                int write = _writePosition;
                int read = Volatile.Read(ref _readPosition);
                int available = write >= read
                    ? _capacity - (write - read) - 1
                    : read - write - 1;
                int accepted = Math.Min(frames, available / 2);

                unsafe
                {
                    short* source = (short*)data.ToPointer();
                    for (int i = 0; i < accepted; i++)
                    {
                        _samples[write] = source[i * 2] / 32768f;
                        write = (write + 1) % _capacity;
                        _samples[write] = source[i * 2 + 1] / 32768f;
                        write = (write + 1) % _capacity;
                    }
                }

                Volatile.Write(ref _writePosition, write);
                return accepted;
            }

            private double _resamplePhase;

            public void Read(Il2CppStructArray<float> destination, int channels, double sourceToOutputRatio)
            {
                int read = _readPosition;
                int write = Volatile.Read(ref _writePosition);
                double ratio = sourceToOutputRatio > 0.0 ? sourceToOutputRatio : 1.0;
                int outputChannels = channels > 0 ? channels : 2;
                int outputFrames = destination.Length / outputChannels;

                for (int frame = 0; frame < outputFrames; frame++)
                {
                    // lerp keeps 44.1/48 kHz cores from slowly underrunning or playing at the wrong pitch
                    int availableSamples = AvailableSamples(read, write);
                    bool havePair = availableSamples >= 4;
                    float left = 0f;
                    float right = 0f;

                    if (havePair)
                    {
                        int next = (read + 2) % _capacity;
                        float leftNext = _samples[next];
                        float rightNext = _samples[(next + 1) % _capacity];
                        float mix = (float)_resamplePhase;
                        left = _samples[read] + (leftNext - _samples[read]) * mix;
                        right = _samples[(read + 1) % _capacity] +
                                (rightNext - _samples[(read + 1) % _capacity]) * mix;

                        _resamplePhase += ratio;
                        int availableFrames = availableSamples / 2;
                        int consumedFrames = Math.Min((int)_resamplePhase, availableFrames - 1);
                        _resamplePhase -= consumedFrames;
                        int consumedSamples = consumedFrames * 2;
                        for (int i = 0; i < consumedSamples; i++)
                            read = (read + 1) % _capacity;
                    }

                    int destinationIndex = frame * outputChannels;
                    if (outputChannels == 1)
                    {
                        destination[destinationIndex] = (left + right) * 0.5f;
                    }
                    else
                    {
                        destination[destinationIndex] = left;
                        destination[destinationIndex + 1] = right;
                        for (int channel = 2; channel < outputChannels; channel++)
                            destination[destinationIndex + channel] = 0f;
                    }
                }

                // handle an unusual partial DSP buffer without leaving stale samples in it
                for (int i = outputFrames * outputChannels; i < destination.Length; i++)
                    destination[i] = 0f;

                Volatile.Write(ref _readPosition, read);
            }

            private int AvailableSamples(int read, int write)
            {
                return write >= read
                    ? write - read
                    : _capacity - (read - write);
            }
        }

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
            if (!_inputEnabled)
                return 0;

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
                    if (_inpBtnY)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_Y;
                    if (_inpCoin)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_SELECT;
                    if (_inpStart) mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_START;
                    if (_inpUp)    mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_UP;
                    if (_inpDown)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_DOWN;
                    if (_inpLeft)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_LEFT;
                    if (_inpRight) mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_RIGHT;
                    if (_inpBtnA)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_A;
                    if (_inpBtnX)  mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_X;
                    if (_inpL)     mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_L;
                    if (_inpR)     mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_R;
                    if (_inpL2)    mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_L2;
                    if (_inpR2)    mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_R2;
                    if (_inpL3)    mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_L3;
                    if (_inpR3)    mask |= 1 << (int)RETRO_DEVICE_ID_JOYPAD_R3;
                    result = unchecked((short)mask);
                }
                else
                {
                    result = id switch
                    {
                        RETRO_DEVICE_ID_JOYPAD_B => _inpBtnB ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_Y => _inpBtnY ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_SELECT => _inpCoin ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_START => _inpStart ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_UP => _inpUp ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_DOWN => _inpDown ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_LEFT => _inpLeft ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_RIGHT => _inpRight ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_A => _inpBtnA ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_X => _inpBtnX ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_L => _inpL ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_R => _inpR ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_L2 => _inpL2Value,
                        RETRO_DEVICE_ID_JOYPAD_R2 => _inpR2Value,
                        RETRO_DEVICE_ID_JOYPAD_L3 => _inpL3 ? (short)1 : (short)0,
                        RETRO_DEVICE_ID_JOYPAD_R3 => _inpR3 ? (short)1 : (short)0,
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
                // Libretro analog queries use index 0=left stick, 1=right
                // stick and id 0=X, 1=Y. Return the same signed range as
                // RetroArch rather than reducing sticks to digital buttons.
                if (index == 0)
                {
                    result = id == 0 ? _inpAnalogLeftX : id == 1 ? _inpAnalogLeftY : (short)0;
                }
                else if (index == 1)
                {
                    result = id == 0 ? _inpAnalogRightX : id == 1 ? _inpAnalogRightY : (short)0;
                }
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
