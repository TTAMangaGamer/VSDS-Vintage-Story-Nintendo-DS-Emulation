using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Vintagestory.API.Client;
using VSDS.Render;

namespace VSDS.Emu
{
    internal static class MelonNativeResolver
    {
        private static bool _registered;
        private static readonly object _lock = new object();
        private static string _extraSearchRoot;

        public static void EnsureRegistered(string extraSearchRoot = null)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(extraSearchRoot))
                {
                    _extraSearchRoot = extraSearchRoot;
                }

                if (_registered) return;
                NativeLibrary.SetDllImportResolver(typeof(MelonDSCore).Assembly, ResolveDllImport);
                _registered = true;
            }
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, "melonds_libretro", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            string[] baseNames = { "melonds_libretro", "melondsds_libretro" };
            var searchDirs = new List<string>();

            string asmDir = Path.GetDirectoryName(assembly.Location);
            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                searchDirs.Add(asmDir);
                searchDirs.Add(Path.Combine(asmDir, "native"));
                searchDirs.Add(Path.Combine(asmDir, isWindows ? "native/win-x64" : "native/linux-x64"));
            }

            if (!string.IsNullOrWhiteSpace(_extraSearchRoot))
            {
                searchDirs.Add(Path.Combine(_extraSearchRoot, "native"));
                searchDirs.Add(Path.Combine(_extraSearchRoot, isWindows ? "native/win-x64" : "native/linux-x64"));
            }

            foreach (string dir in searchDirs)
            {
                foreach (string baseName in baseNames)
                {
                    string candidate = isWindows
                        ? Path.Combine(dir, baseName + ".dll")
                        : Path.Combine(dir, baseName + ".so");

                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                    {
                        return handle;
                    }

                    if (isLinux)
                    {
                        string libCandidate = Path.Combine(dir, "lib" + baseName + ".so");
                        if (File.Exists(libCandidate) && NativeLibrary.TryLoad(libCandidate, out IntPtr libHandle))
                        {
                            return libHandle;
                        }
                    }
                }
            }

            foreach (string baseName in baseNames)
            {
                if (NativeLibrary.TryLoad(baseName, assembly, searchPath, out IntPtr fallback))
                {
                    return fallback;
                }
            }

            return IntPtr.Zero;
        }
    }

    public sealed class CrossPlatformDsAudioPlayer : IDisposable
    {
        private const int SampleRate = 32768;
        private const ushort Channels = 2;
        private const ushort BitsPerSample = 16;
        private const int BufferCount = 8;
        private const int FramesPerBuffer = 1024;
        private const int BufferByteSize = FramesPerBuffer * Channels * (BitsPerSample / 8);

        private readonly object _lock = new object();
        private readonly List<short> _pendingSamples = new List<short>(SampleRate);

        private WindowsWaveOutBackend _winBackend;
        private LinuxPipeBackend _linuxBackend;
        private bool _disposed;

        public CrossPlatformDsAudioPlayer()
        {
            if (OperatingSystem.IsWindows())
            {
                _winBackend = new WindowsWaveOutBackend();
            }
            else if (OperatingSystem.IsLinux())
            {
                _linuxBackend = new LinuxPipeBackend();
            }
        }

        public void PushSample(short left, short right)
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_pendingSamples.Count > SampleRate * 2) return;
                _pendingSamples.Add(left);
                _pendingSamples.Add(right);
            }
        }

        public void PushBatch(IntPtr data, nuint frames)
        {
            if (_disposed || data == IntPtr.Zero || frames == 0) return;

            int totalShorts = (int)(frames * 2);
            short[] temp = new short[totalShorts];
            Marshal.Copy(data, temp, 0, totalShorts);

            lock (_lock)
            {
                if (_pendingSamples.Count > SampleRate * 2) return;
                _pendingSamples.AddRange(temp);
            }
        }

        public void Pump()
        {
            if (_disposed) return;

            const int shortsPerChunk = FramesPerBuffer * 2;
            short[] chunk = null;

            lock (_lock)
            {
                if (_pendingSamples.Count >= shortsPerChunk)
                {
                    chunk = new short[shortsPerChunk];
                    _pendingSamples.CopyTo(0, chunk, 0, shortsPerChunk);
                    _pendingSamples.RemoveRange(0, shortsPerChunk);
                }
            }

            if (chunk == null) return;

            _winBackend?.WriteChunk(chunk);
            _linuxBackend?.WriteChunk(chunk);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _winBackend?.Dispose();
            _winBackend = null;

            _linuxBackend?.Dispose();
            _linuxBackend = null;
        }

        private sealed class WindowsWaveOutBackend : IDisposable
        {
            private const uint MMSYSERR_NOERROR = 0;
            private const uint CALLBACK_NULL = 0;
            private const uint WAVE_MAPPER = 0xFFFFFFFF;
            private const uint WHDR_DONE = 0x00000001;

            [StructLayout(LayoutKind.Sequential)]
            private struct WAVEFORMATEX
            {
                public ushort wFormatTag;
                public ushort nChannels;
                public uint nSamplesPerSec;
                public uint nAvgBytesPerSec;
                public ushort nBlockAlign;
                public ushort wBitsPerSample;
                public ushort cbSize;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WAVEHDR
            {
                public IntPtr lpData;
                public uint dwBufferLength;
                public uint dwBytesRecorded;
                public IntPtr dwUser;
                public uint dwFlags;
                public uint dwLoops;
                public IntPtr lpNext;
                public IntPtr reserved;
            }

            private sealed class AudioSlot : IDisposable
            {
                public readonly IntPtr UnmanagedData;
                public readonly IntPtr HeaderPtr;
                public bool InFlight;

                public AudioSlot()
                {
                    UnmanagedData = Marshal.AllocHGlobal(BufferByteSize);
                    var hdr = new WAVEHDR
                    {
                        lpData = UnmanagedData,
                        dwBufferLength = BufferByteSize,
                        dwFlags = 0
                    };
                    HeaderPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
                    Marshal.StructureToPtr(hdr, HeaderPtr, false);
                }

                public WAVEHDR GetHeader() => Marshal.PtrToStructure<WAVEHDR>(HeaderPtr);
                public void SetHeader(WAVEHDR hdr) => Marshal.StructureToPtr(hdr, HeaderPtr, false);

                public void Dispose()
                {
                    if (HeaderPtr != IntPtr.Zero) Marshal.FreeHGlobal(HeaderPtr);
                    if (UnmanagedData != IntPtr.Zero) Marshal.FreeHGlobal(UnmanagedData);
                }
            }

            [DllImport("winmm.dll")]
            private static extern uint waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

            [DllImport("winmm.dll")]
            private static extern uint waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

            [DllImport("winmm.dll")]
            private static extern uint waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

            [DllImport("winmm.dll")]
            private static extern uint waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

            [DllImport("winmm.dll")]
            private static extern uint waveOutReset(IntPtr hwo);

            [DllImport("winmm.dll")]
            private static extern uint waveOutClose(IntPtr hwo);

            private readonly AudioSlot[] _slots = new AudioSlot[BufferCount];
            private IntPtr _hWaveOut = IntPtr.Zero;
            private bool _initialized;

            public WindowsWaveOutBackend()
            {
                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = 1,
                    nChannels = Channels,
                    nSamplesPerSec = SampleRate,
                    wBitsPerSample = BitsPerSample,
                    nBlockAlign = Channels * (BitsPerSample / 8),
                    nAvgBytesPerSec = SampleRate * Channels * (BitsPerSample / 8),
                    cbSize = 0
                };

                for (int i = 0; i < BufferCount; i++)
                {
                    _slots[i] = new AudioSlot();
                }

                if (waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL) == MMSYSERR_NOERROR)
                {
                    for (int i = 0; i < BufferCount; i++)
                    {
                        waveOutPrepareHeader(_hWaveOut, _slots[i].HeaderPtr, (uint)Marshal.SizeOf<WAVEHDR>());
                    }
                    _initialized = true;
                }
            }

            public void WriteChunk(short[] chunk)
            {
                if (!_initialized || _hWaveOut == IntPtr.Zero) return;

                for (int i = 0; i < BufferCount; i++)
                {
                    AudioSlot slot = _slots[i];
                    WAVEHDR hdr = slot.GetHeader();

                    if (!slot.InFlight || (hdr.dwFlags & WHDR_DONE) != 0)
                    {
                        Marshal.Copy(chunk, 0, slot.UnmanagedData, chunk.Length);
                        hdr.dwBufferLength = BufferByteSize;
                        hdr.dwFlags &= ~WHDR_DONE;
                        slot.SetHeader(hdr);

                        slot.InFlight = true;
                        waveOutWrite(_hWaveOut, slot.HeaderPtr, (uint)Marshal.SizeOf<WAVEHDR>());
                        break;
                    }
                }
            }

            public void Dispose()
            {
                if (_initialized && _hWaveOut != IntPtr.Zero)
                {
                    waveOutReset(_hWaveOut);
                    uint hdrSize = (uint)Marshal.SizeOf<WAVEHDR>();
                    for (int i = 0; i < BufferCount; i++)
                    {
                        if (_slots[i] != null)
                        {
                            waveOutUnprepareHeader(_hWaveOut, _slots[i].HeaderPtr, hdrSize);
                        }
                    }
                    waveOutClose(_hWaveOut);
                    _hWaveOut = IntPtr.Zero;
                }

                for (int i = 0; i < BufferCount; i++)
                {
                    _slots[i]?.Dispose();
                    _slots[i] = null;
                }
            }
        }

        private sealed class LinuxPipeBackend : IDisposable
        {
            [DllImport("libc.so.6", EntryPoint = "popen")]
            private static extern IntPtr Popen([MarshalAs(UnmanagedType.LPStr)] string command, [MarshalAs(UnmanagedType.LPStr)] string type);

            [DllImport("libc.so.6", EntryPoint = "fwrite")]
            private static extern nuint Fwrite(byte[] ptr, nuint size, nuint nmemb, IntPtr stream);

            [DllImport("libc.so.6", EntryPoint = "fflush")]
            private static extern int Fflush(IntPtr stream);

            [DllImport("libc.so.6", EntryPoint = "pclose")]
            private static extern int Pclose(IntPtr stream);

            private readonly byte[] _byteBuffer = new byte[BufferByteSize];
            private IntPtr _pipeStream = IntPtr.Zero;

            public LinuxPipeBackend()
            {
                string pwCmd = $"pw-cat --playback --raw --format s16 --rate {SampleRate} --channels {Channels} - 2>/dev/null";
                string alsaCmd = $"aplay -q -f S16_LE -r {SampleRate} -c {Channels} -t raw - 2>/dev/null";

                _pipeStream = Popen(pwCmd, "w");
                if (_pipeStream == IntPtr.Zero)
                {
                    _pipeStream = Popen(alsaCmd, "w");
                }
            }

            public void WriteChunk(short[] chunk)
            {
                if (_pipeStream == IntPtr.Zero) return;

                Buffer.BlockCopy(chunk, 0, _byteBuffer, 0, BufferByteSize);
                nuint written = Fwrite(_byteBuffer, 1, (nuint)BufferByteSize, _pipeStream);
                if (written > 0)
                {
                    Fflush(_pipeStream);
                }
            }

            public void Dispose()
            {
                if (_pipeStream != IntPtr.Zero)
                {
                    Pclose(_pipeStream);
                    _pipeStream = IntPtr.Zero;
                }
            }
        }
    }

    public class MelonDSCore : IDisposable
    {
        private const string NativeLibName = "melonds_libretro";

        public const int ScreenWidth = 256;
        public const int ScreenHeight = 192;
        public const int ScreenPixelCount = ScreenWidth * ScreenHeight;

        private const double FrameIntervalMs = 1000.0 / 59.8261;
        private const double AutoSaveIntervalSeconds = 15.0;

        private const uint RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 9;
        private const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10;
        private const uint RETRO_ENVIRONMENT_GET_VARIABLE = 15;
        private const uint RETRO_ENVIRONMENT_SET_VARIABLES = 16;
        private const uint RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE = 17;
        private const uint RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY = 31;

        private const uint RETRO_DEVICE_NONE = 0;
        private const uint RETRO_DEVICE_JOYPAD = 1;
        private const uint RETRO_DEVICE_MOUSE = 2;
        private const uint RETRO_DEVICE_POINTER = 5;

        private const uint RETRO_DEVICE_ID_MOUSE_LEFT = 2;

        private const uint RETRO_DEVICE_ID_POINTER_X = 0;
        private const uint RETRO_DEVICE_ID_POINTER_Y = 1;
        private const uint RETRO_DEVICE_ID_POINTER_PRESSED = 2;

        private const uint RETRO_MEMORY_SAVE_RAM = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RetroVariable
        {
            public IntPtr Key;
            public IntPtr Value;
        }

        private readonly Dictionary<string, IntPtr> _coreOptionPtrs = new Dictionary<string, IntPtr>();

        private readonly ICoreClientAPI _capi;
        private readonly DSScreenRenderer _renderer;
        private readonly string _systemDir;
        private readonly string _saveDir;
        private readonly CrossPlatformDsAudioPlayer _audioPlayer = new CrossPlatformDsAudioPlayer();

        private readonly int[] _topBuffer = new int[ScreenPixelCount];
        private readonly int[] _bottomBuffer = new int[ScreenPixelCount];
        private readonly object _emuLock = new object();

        private readonly object _inputLock = new object();
        private readonly bool[] _buttons = new bool[16];
        private bool _touchPressed;
        private short _pointerX;
        private short _pointerY;

        private CancellationTokenSource _cts;
        private Thread _emuThread;
        private volatile bool _isRunning;
        private bool _disposed;
        private bool _initialized;
        private DateTime _lastAutoSaveUtc = DateTime.UtcNow;

        public string CurrentRomPath { get; private set; }
        public bool IsRunning => _isRunning;
        public bool IsFocused { get; private set; }

        public bool ToggleFocus()
        {
            bool nextState = !IsFocused;
            IsFocused = nextState;

            if (_renderer != null)
            {
                if (nextState)
                {
                    _renderer.SetFocused(true);
                    IsFocused = _renderer.IsFocused;
                }
                else
                {
                    _renderer.SetFocused(false);
                    IsFocused = false;
                }
            }

            return IsFocused;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VideoRefreshDelegate(IntPtr data, uint width, uint height, nuint pitch);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AudioSampleDelegate(short left, short right);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nuint AudioSampleBatchDelegate(IntPtr data, nuint frames);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InputPollDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate short InputStateDelegate(uint port, uint device, uint index, uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool EnvironmentDelegate(uint cmd, IntPtr data);

        private readonly VideoRefreshDelegate _videoCb;
        private readonly AudioSampleDelegate _audioCb;
        private readonly AudioSampleBatchDelegate _audioBatchCb;
        private readonly InputPollDelegate _inputPollCb;
        private readonly InputStateDelegate _inputStateCb;
        private readonly EnvironmentDelegate _envCb;

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_init();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_deinit();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_environment(EnvironmentDelegate cb);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_video_refresh(VideoRefreshDelegate cb);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_audio_sample(AudioSampleDelegate cb);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_audio_sample_batch(AudioSampleBatchDelegate cb);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_input_poll(InputPollDelegate cb);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_input_state(InputStateDelegate cb);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_set_controller_port_device(uint port, uint device);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern bool retro_load_game(ref RetroGameInfo game);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_unload_game();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void retro_run();

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr retro_get_memory_data(uint id);

        [DllImport(NativeLibName, CallingConvention = CallingConvention.Cdecl)]
        private static extern nuint retro_get_memory_size(uint id);

        [StructLayout(LayoutKind.Sequential)]
        private struct RetroGameInfo
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string Path;
            public IntPtr Data;
            public nuint Size;
            [MarshalAs(UnmanagedType.LPStr)]
            public string Meta;
        }

        public MelonDSCore(ICoreClientAPI capi, DSScreenRenderer renderer, string modDataPath = null)
        {
            _capi = capi;
            _renderer = renderer;

            string baseDataDir = !string.IsNullOrWhiteSpace(modDataPath)
                ? modDataPath
                : capi.GetOrCreateDataPath("vsds");

            _systemDir = Path.Combine(baseDataDir, "system");
            _saveDir = Path.Combine(baseDataDir, "saves");
            Directory.CreateDirectory(_systemDir);
            Directory.CreateDirectory(_saveDir);

            MelonNativeResolver.EnsureRegistered(baseDataDir);

            _videoCb = OnVideoRefresh;
            _audioCb = OnAudioSample;
            _audioBatchCb = OnAudioSampleBatch;
            _inputPollCb = OnInputPoll;
            _inputStateCb = OnInputState;
            _envCb = OnEnvironment;

            try
            {
                retro_set_environment(_envCb);
                retro_set_video_refresh(_videoCb);
                retro_set_audio_sample(_audioCb);
                retro_set_audio_sample_batch(_audioBatchCb);
                retro_set_input_poll(_inputPollCb);
                retro_set_input_state(_inputStateCb);
                retro_init();
                retro_set_controller_port_device(0, RETRO_DEVICE_POINTER);
                _initialized = true;
            }
            catch (Exception ex)
            {
                _capi.Logger.Warning("[vsds] Failed to initialize native MelonDS core ({0}): {1}", NativeLibName, ex.Message);
            }
        }

        private IntPtr GetOrAllocOptionValue(string value)
        {
            if (!_coreOptionPtrs.TryGetValue(value, out IntPtr ptr))
            {
                ptr = Marshal.StringToCoTaskMemAnsi(value);
                _coreOptionPtrs[value] = ptr;
            }
            return ptr;
        }

        public string GetSaveFilePath(string romPath)
        {
            string stem = Path.GetFileNameWithoutExtension(romPath);
            return Path.Combine(_saveDir, stem + ".sav");
        }

        private void ImportSaveRamUnlocked(string romPath)
        {
            if (string.IsNullOrWhiteSpace(romPath) || string.IsNullOrWhiteSpace(_saveDir)) return;

            string savPath = GetSaveFilePath(romPath);
            if (!File.Exists(savPath)) return;

            IntPtr dst = retro_get_memory_data(RETRO_MEMORY_SAVE_RAM);
            nuint size = retro_get_memory_size(RETRO_MEMORY_SAVE_RAM);
            if (dst == IntPtr.Zero || size == 0) return;

            byte[] savedBytes = File.ReadAllBytes(savPath);
            int copyLen = Math.Min(savedBytes.Length, (int)size);
            Marshal.Copy(savedBytes, 0, dst, copyLen);
        }

        public void ExportSaveRam()
        {
            if (!_initialized || string.IsNullOrWhiteSpace(CurrentRomPath) || string.IsNullOrWhiteSpace(_saveDir)) return;

            lock (_emuLock)
            {
                ExportSaveRamUnlocked(CurrentRomPath);
            }
        }

        private void ExportSaveRamUnlocked(string romPath)
        {
            if (!_initialized || string.IsNullOrWhiteSpace(romPath) || string.IsNullOrWhiteSpace(_saveDir)) return;

            IntPtr src = retro_get_memory_data(RETRO_MEMORY_SAVE_RAM);
            nuint size = retro_get_memory_size(RETRO_MEMORY_SAVE_RAM);
            if (src == IntPtr.Zero || size == 0) return;

            Directory.CreateDirectory(_saveDir);
            byte[] ramBytes = new byte[(int)size];
            Marshal.Copy(src, ramBytes, 0, ramBytes.Length);
            File.WriteAllBytes(GetSaveFilePath(romPath), ramBytes);
            _lastAutoSaveUtc = DateTime.UtcNow;
        }

        public void SetButtonState(uint joypadId, bool pressed)
        {
            lock (_inputLock)
            {
                if (joypadId < _buttons.Length)
                {
                    _buttons[joypadId] = pressed;
                }
            }
        }

        public void SetTouchState(bool pressed, float u = 0.5f, float v = 0.5f)
        {
            lock (_inputLock)
            {
                _touchPressed = pressed;
                float clampedU = Math.Clamp(u, 0f, 1f);
                float clampedV = Math.Clamp(v, 0f, 1f);
                _pointerX = (short)((clampedU * 2f - 1f) * 32767f);
                _pointerY = (short)((clampedV * 2f - 1f) * 32767f);
            }
        }

        public bool LoadRom(string romPath)
        {
            if (_disposed || !_initialized || string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
            {
                return false;
            }

            StopEmulationLoop();

            byte[] romBytes = File.ReadAllBytes(romPath);
            GCHandle pinned = GCHandle.Alloc(romBytes, GCHandleType.Pinned);

            try
            {
                lock (_emuLock)
                {
                    if (!string.IsNullOrEmpty(CurrentRomPath))
                    {
                        ExportSaveRamUnlocked(CurrentRomPath);
                        retro_unload_game();
                        CurrentRomPath = null;
                    }

                    var info = new RetroGameInfo
                    {
                        Path = romPath,
                        Data = pinned.AddrOfPinnedObject(),
                        Size = (nuint)romBytes.Length,
                        Meta = string.Empty
                    };

                    if (!retro_load_game(ref info))
                    {
                        _capi.Logger.Error("[vsds] MelonDS failed to load ROM: {0}", romPath);
                        return false;
                    }

                    CurrentRomPath = romPath;
                    ImportSaveRamUnlocked(romPath);
                    _lastAutoSaveUtc = DateTime.UtcNow;
                }

                StartEmulationLoop();
                _capi.ShowChatMessage($"[VSDS] Booted {Path.GetFileName(romPath)}");
                return true;
            }
            finally
            {
                if (pinned.IsAllocated)
                {
                    pinned.Free();
                }
            }
        }

        public void StopEmulationLoop()
        {
            _isRunning = false;
            _cts?.Cancel();

            if (_emuThread != null && _emuThread.IsAlive && Thread.CurrentThread != _emuThread)
            {
                _emuThread.Join(1000);
            }

            _cts?.Dispose();
            _cts = null;
            _emuThread = null;
        }

        private void StartEmulationLoop()
        {
            StopEmulationLoop();

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _isRunning = true;

            _emuThread = new Thread(() => RunLoop(token))
            {
                Name = "VSDS-MelonDS-Loop",
                IsBackground = true
            };
            _emuThread.Start();
        }

        private void RunLoop(CancellationToken token)
        {
            double nextFrameTime = Environment.TickCount64;

            while (!token.IsCancellationRequested && _isRunning)
            {
                double now = Environment.TickCount64;
                if (now >= nextFrameTime)
                {
                    lock (_emuLock)
                    {
                        if (_isRunning && !_disposed && _initialized)
                        {
                            retro_run();

                            if (!string.IsNullOrEmpty(CurrentRomPath) &&
                                (DateTime.UtcNow - _lastAutoSaveUtc).TotalSeconds >= AutoSaveIntervalSeconds)
                            {
                                ExportSaveRamUnlocked(CurrentRomPath);
                            }
                        }
                    }

                    _audioPlayer.Pump();

                    nextFrameTime += FrameIntervalMs;
                    if (nextFrameTime < now - FrameIntervalMs * 3)
                    {
                        nextFrameTime = now + FrameIntervalMs;
                    }
                }
                else
                {
                    int sleepMs = (int)Math.Max(1, nextFrameTime - now);
                    Thread.Sleep(Math.Min(sleepMs, 10));
                }
            }
        }

        private bool OnEnvironment(uint cmd, IntPtr data)
        {
            if (cmd == RETRO_ENVIRONMENT_SET_PIXEL_FORMAT && data != IntPtr.Zero)
            {
                int fmt = Marshal.ReadInt32(data);
                return fmt == 1;
            }

            if (cmd == RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY && data != IntPtr.Zero)
            {
                IntPtr strPtr = Marshal.StringToCoTaskMemAnsi(_systemDir);
                Marshal.WriteIntPtr(data, strPtr);
                return true;
            }

            if (cmd == RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY && data != IntPtr.Zero)
            {
                IntPtr strPtr = Marshal.StringToCoTaskMemAnsi(_saveDir);
                Marshal.WriteIntPtr(data, strPtr);
                return true;
            }

            if (cmd == RETRO_ENVIRONMENT_GET_VARIABLE && data != IntPtr.Zero)
            {
                RetroVariable varStruct = Marshal.PtrToStructure<RetroVariable>(data);
                string key = varStruct.Key != IntPtr.Zero ? Marshal.PtrToStringAnsi(varStruct.Key) : null;

                string val = key switch
                {
                    "melonds_touch_mode" => "Touch",
                    "melonds_show_cursor" => "Always",
                    "melonds_screen_layout" => "Top/Bottom",
                    _ => null
                };

                if (val != null)
                {
                    varStruct.Value = GetOrAllocOptionValue(val);
                    Marshal.StructureToPtr(varStruct, data, false);
                    return true;
                }

                return false;
            }

            if (cmd == RETRO_ENVIRONMENT_SET_VARIABLES || cmd == RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE)
            {
                return true;
            }

            return false;
        }

        private void OnVideoRefresh(IntPtr data, uint width, uint height, nuint pitch)
        {
            if (data == IntPtr.Zero || width < ScreenWidth || height < ScreenHeight * 2) return;

            int strideInts = Math.Max(ScreenWidth, (int)(pitch / sizeof(int)));
            int[] rowBuf = new int[strideInts];

            for (int y = 0; y < ScreenHeight; y++)
            {
                IntPtr topRowPtr = IntPtr.Add(data, y * (int)pitch);
                Marshal.Copy(topRowPtr, rowBuf, 0, Math.Min(strideInts, ScreenWidth));
                CopyConvertedRow(rowBuf, _topBuffer, y * ScreenWidth);

                IntPtr botRowPtr = IntPtr.Add(data, (y + ScreenHeight) * (int)pitch);
                Marshal.Copy(botRowPtr, rowBuf, 0, Math.Min(strideInts, ScreenWidth));
                CopyConvertedRow(rowBuf, _bottomBuffer, y * ScreenWidth);
            }

            int[] topSnapshot = new int[ScreenPixelCount];
            int[] botSnapshot = new int[ScreenPixelCount];
            Array.Copy(_topBuffer, topSnapshot, ScreenPixelCount);
            Array.Copy(_bottomBuffer, botSnapshot, ScreenPixelCount);

            _capi.Event.EnqueueMainThreadTask(() =>
            {
                if (!_disposed && _renderer != null)
                {
                    _renderer.UpdateScreenPixels(topSnapshot, botSnapshot);
                }
            }, "vsds_render_screens");
        }

        private static void CopyConvertedRow(int[] srcRow, int[] dstBuffer, int dstOffset)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                uint raw = unchecked((uint)srcRow[x]);
                uint r = (raw >> 16) & 0xFFu;
                uint g = (raw >> 8) & 0xFFu;
                uint b = raw & 0xFFu;
                dstBuffer[dstOffset + x] = unchecked((int)(0xFF000000u | (r << 16) | (g << 8) | b));
            }
        }

        private void OnAudioSample(short left, short right)
        {
            _audioPlayer.PushSample(left, right);
        }

        private nuint OnAudioSampleBatch(IntPtr data, nuint frames)
        {
            _audioPlayer.PushBatch(data, frames);
            return frames;
        }

        private void OnInputPoll() { }

        private short OnInputState(uint port, uint device, uint index, uint id)
        {
            if (port != 0) return 0;

            lock (_inputLock)
            {
                if (device == RETRO_DEVICE_JOYPAD)
                {
                    if (id < _buttons.Length && _buttons[id])
                    {
                        return 1;
                    }
                    return 0;
                }

                if (device == RETRO_DEVICE_POINTER)
                {
                    return id switch
                    {
                        RETRO_DEVICE_ID_POINTER_X => _pointerX,
                        RETRO_DEVICE_ID_POINTER_Y => _pointerY,
                        RETRO_DEVICE_ID_POINTER_PRESSED => (short)(_touchPressed ? 1 : 0),
                        _ => 0
                    };
                }

                if (device == RETRO_DEVICE_MOUSE)
                {
                    if (id == RETRO_DEVICE_ID_MOUSE_LEFT)
                    {
                        return (short)(_touchPressed ? 1 : 0);
                    }
                    return 0;
                }
            }

            return 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsFocused = false;

            StopEmulationLoop();
            _audioPlayer?.Dispose();

            if (_initialized)
            {
                lock (_emuLock)
                {
                    if (!string.IsNullOrEmpty(CurrentRomPath))
                    {
                        ExportSaveRamUnlocked(CurrentRomPath);
                        retro_unload_game();
                        CurrentRomPath = null;
                    }
                    retro_deinit();
                }
            }

            foreach (IntPtr ptr in _coreOptionPtrs.Values)
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            _coreOptionPtrs.Clear();
        }
    }
}