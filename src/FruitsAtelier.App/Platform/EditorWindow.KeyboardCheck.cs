using FruitsAtelier.Core;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckTestplayKeyboard()
    {
        var prepare = view.RequestPrepareHitsound;
        var audition = view.RequestAuditionHitsound;
        var loop = view.RequestTestplayMenuLoop;
        var stop = view.RequestStopHitsounds;
        int clicks = 0;
        bool looping = false;
        try
        {
            view.RequestPrepareHitsound = null;
            view.RequestStopHitsounds = null;
            view.RequestAuditionHitsound = sound => { if (sound.Name == "pause-retry-click") clicks++; };
            view.RequestTestplayMenuLoop = sound => looping = sound is not null;
            var map = new MapDocument { IsDemo = false, DurationMs = 20000 };
            map.Fruits.Add(new() { TimeMs = 15000, X = 256 });
            view.LoadDocument(map); view.CloseLibrary(); view.StartTestplay();
            uint scan = Native.MapVirtualKey(192, 0);
            var down = new Native.Message { Window = hwnd, Id = 0x0100, WParam = 192, LParam = (nint)((scan << 16) | 1) };
            var up = new Native.Message { Window = hwnd, Id = 0x0101, WParam = 0xE5, LParam = (nint)((scan << 16) | 0xC0000001u) };
            if (!DispatchShortcutBeforeTranslation(down) || !looping)
                throw new InvalidOperationException("Testplay retry bypassed shortcut dispatch before IME translation.");
            Native.DispatchMessage(ref up);
            if (looping || clicks != 0)
                throw new InvalidOperationException("IME process-key release did not cancel held retry.");
            down.WParam = 0xE5;
            if (!DispatchShortcutBeforeTranslation(down) || !looping)
                throw new InvalidOperationException("IME process-key retry did not resolve its physical key.");
            Thread.Sleep(310);
            canvas!.Begin(); view.Render(canvas, 980, 620); canvas.End();
            if (clicks != 1 || looping)
                throw new InvalidOperationException("IME retry failed to complete once after 300 ms.");
            Native.DispatchMessage(ref up);
            Thread.Sleep(610);
            canvas.Begin(); view.Render(canvas, 980, 620); canvas.End();
            view.KeyDown(27, false, false); view.KeyUp(27);
            if (!DispatchShortcutBeforeTranslation(down))
                throw new InvalidOperationException("Paused testplay retry reached IME translation.");
            Thread.Sleep(310);
            canvas.Begin(); view.Render(canvas, 980, 620); canvas.End();
            if (clicks != 2 || looping || !view.TestplayPaused || view.TestplayPauseMenuVisible)
                throw new InvalidOperationException("Paused IME retry did not begin its reaction countdown.");
            Native.DispatchMessage(ref up);
            Thread.Sleep(610);
            canvas.Begin(); view.Render(canvas, 980, 620); canvas.End();
            if (view.TestplayPaused) throw new InvalidOperationException("IME retry did not resume after its reaction countdown.");
            AppLog.Write("Testplay IME shortcut dispatch, physical release and 300 ms retry passed.");
        }
        finally
        {
            view.StopTestplay();
            view.RequestPrepareHitsound = prepare;
            view.RequestAuditionHitsound = audition;
            view.RequestTestplayMenuLoop = loop;
            view.RequestStopHitsounds = stop;
        }
    }
}
