using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FruitsAtelier.App.Audio;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed class TestplayInputThread : IDisposable
{
    private const uint checkKeyMessage = 0x8001;
    private readonly nint owner;
    private readonly CatchTestplaySession session;
    private readonly Func<AudioState> audio;
    private readonly bool diagnostic;
    private readonly double updatesPerSecond;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly Native.WindowProc procedure;
    private readonly Dictionary<(nint Device, ushort Scan, ushort Extended), int> pressed = [];
    private readonly HashSet<(nint Device, ushort Scan, ushort Extended)> altPressed = [];
    private nint window;
    private Exception? startupError;
    private volatile bool stopping;
    internal Action<double>? CheckKeyProcessed { get; set; }
    internal Action? CheckTick { get; set; }

    public TestplayInputThread(nint owner, CatchTestplaySession session, Func<AudioState> audio, bool diagnostic = false, double updatesPerSecond = 2000)
    {
        this.owner = owner; this.session = session; this.audio = audio; this.diagnostic = diagnostic;
        this.updatesPerSecond = updatesPerSecond;
        procedure = HandleMessage;
        thread = new Thread(Run) { IsBackground = true, Name = "Catch testplay input", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        ready.Wait();
        if (startupError is not null) { thread.Join(); ready.Dispose(); throw new InvalidOperationException("Testplay input initialization failed.", startupError); }
    }

    private void Run()
    {
        string className = "FruitsAtelier.TestplayInput." + Guid.NewGuid().ToString("N");
        nint instance = Native.GetModuleHandle(null);
        bool registered = false;
        try
        {
            var cls = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Procedure = procedure, Instance = instance, ClassName = className };
            if (Native.RegisterClassEx(ref cls) == 0) throw new Win32Exception();
            window = Native.CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, (nint)(-3), 0, instance, 0);
            if (window == 0) throw new Win32Exception();
            var device = new RawDevice { UsagePage = 1, Usage = 6, Flags = 0x100, Target = window };
            if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawDevice>())) throw new Win32Exception();
            registered = true;
            using var pacer = new TestplayFramePacer(updatesPerSecond, updatesPerSecond > 1000 ? .2 : 0);
            ready.Set();
            while (!stopping && !session.Ended)
            {
                // Input wakes the wait immediately; the timer only advances held movement and judgements.
                int count = 0;
                while (count++ < 256 && Native.PeekMessage(out var message, window, 0, 0, 1))
                    Native.DispatchMessage(ref message);
                if (!diagnostic && Native.GetForegroundWindow() != owner)
                {
                    pressed.Clear(); altPressed.Clear();
                    session.ReleaseKeys();
                }
                if (pacer.FrameDue)
                {
                    UpdateAudio(); session.Tick(); CheckTick?.Invoke(); pacer.FrameStarted();
                }
                pacer.Wait();
            }
        }
        catch (Exception error)
        {
            startupError = error; session.Cancel(error); AppLog.Write(error.ToString());
        }
        finally
        {
            ready.Set();
            if (registered) RegisterRawInputDevices([new RawDevice { UsagePage = 1, Usage = 6, Flags = 1 }], 1, (uint)Marshal.SizeOf<RawDevice>());
            if (window != 0) Native.DestroyWindow(window);
            UnregisterClass(className, instance);
        }
    }

    private unsafe nint HandleMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == 0x00FF && Native.GetForegroundWindow() == owner)
            {
                RawKeyboardInput input = default;
                uint size = (uint)sizeof(RawKeyboardInput);
                if (GetRawInputData(lParam, 0x10000003, &input, ref size, (uint)sizeof(RawHeader)) == uint.MaxValue)
                    throw new Win32Exception();
                if (input.Header.Type == 1 && input.VirtualKey != 255)
                {
                    int key = input.VirtualKey;
                    if (key is 0xA0 or 0xA1) key = 0x10;
                    if (key is 0xA2 or 0xA3) key = 0x11;
                    if (key is 0xA4 or 0xA5) key = 0x12;
                    bool down = (input.Flags & 1) == 0;
                    var physical = (input.Header.Device, input.MakeCode, (ushort)(input.Flags & 6));
                    if (key == 0x12)
                    {
                        if (down)
                        {
                            altPressed.Add(physical);
                            foreach (var held in pressed.Where(pair => pair.Value is 37 or 38 or 39 or 40).Select(pair => pair.Key).ToArray())
                                pressed.Remove(held);
                            foreach (int arrow in new[] { 37, 38, 39, 40 })
                                if (session.UsesKey(arrow)) session.SetKey(arrow, pressed.ContainsValue(arrow));
                        }
                        else altPressed.Remove(physical);
                    }
                    // Navigation belongs to WM_KEYDOWN on the UI thread, avoiding a second Escape after return.
                    if (key != 27 && session.UsesKey(key))
                    {
                        if (key is 37 or 38 or 39 or 40 && altPressed.Count > 0) pressed.Remove(physical);
                        else if (down) pressed.TryAdd(physical, key);
                        else pressed.Remove(physical);
                        UpdateAudio(); session.SetKey(key, pressed.ContainsValue(key));
                    }
                }
            }
            else if (diagnostic && message == checkKeyMessage)
            {
                UpdateAudio(); session.SetKey((int)(wParam & 255), (wParam & 256) == 0);
                CheckKeyProcessed?.Invoke((Stopwatch.GetTimestamp() - (long)lParam) * 1000d / Stopwatch.Frequency);
            }
        }
        catch (Exception error) { session.Cancel(error); AppLog.Write(error.ToString()); }
        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void UpdateAudio()
    {
        if (!session.WithAudio) return;
        var state = audio();
        session.UpdateAudio(state.PositionMs, state.PositionTimestampMs, state.DurationMs, state.CanPlay,
            state.IsPlaying, state.IsLoading, state.Error is not null, state.OutputBufferAheadMs);
    }
    internal void PostCheckKey(int key, bool down)
    {
        if (!diagnostic) throw new InvalidOperationException();
        if (!Native.PostMessage(window, checkKeyMessage, (nuint)(key | (down ? 0 : 256)), (nint)Stopwatch.GetTimestamp()))
            throw new Win32Exception();
    }
    public void Dispose()
    {
        stopping = true;
        Native.PostMessage(window, 0, 0, 0);
        thread.Join(); ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawHeader { public uint Type, Size; public nint Device; public nuint WParam; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboardInput { public RawHeader Header; public ushort MakeCode, Flags, Reserved, VirtualKey; public uint Message, ExtraInformation; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern unsafe uint GetRawInputData(nint input, uint command, void* data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, nint instance);
}
