namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckPaintLifecycle()
    {
        var document = view.Document.DeepClone();
        Native.GetClientRect(hwnd, out var size);
        canvas!.Begin(); canvas.Clip(new(0, 0, 50, 50)); canvas.AbortDraw();
        canvas.Begin(); canvas.End();

        // Simulate messages dispatched by a nested STA pump while an outer frame is drawing.
        int before = frames;
        painting = true;
        try
        {
            canvas.Begin();
            WndProc(hwnd, 0x000F, 0, 0);
            WndProc(hwnd, 0x0113, 1, 0);
            canvas.End();
        }
        finally { painting = false; }
        if (frames != before) throw new InvalidOperationException("Nested paint submitted an extra frame.");

        using (var outer = new NativeModalScope(hwnd))
        {
            using (var inner = new NativeModalScope(hwnd)) { }
            if (!NativeModalScope.Active) throw new InvalidOperationException("Nested modal scope resumed its owner early.");
            canvas.Begin();
            WndProc(hwnd, 0x000F, 0, 0);
            WndProc(hwnd, 0x0113, 1, 0);
            canvas.End();
            if (frames != before) throw new InvalidOperationException("Modal owner continued rendering.");
        }
        if (NativeModalScope.Active) throw new InvalidOperationException("Modal scope did not release its owner.");

        // Two BeginDraw calls reproduce the live D2DERR_WRONG_STATE at EndDraw.
        var previous = canvas;
        canvas.Begin();
        WndProc(hwnd, 0x000F, 0, 0);
        if (ReferenceEquals(previous, canvas) || failed || !view.ErrorVisible)
            throw new InvalidOperationException("Paint failure did not rebuild the renderer and expose its error.");
        WndProc(hwnd, 0x000F, 0, 0);
        if (recoveringRenderer || failed) throw new InvalidOperationException("Recovered renderer could not draw its error.");
        view.KeyDown(13, false, false); view.KeyUp(13);
        WndProc(hwnd, 0x000F, 0, 0);
        if (view.ErrorVisible || !document.ContentEquals(view.Document))
            throw new InvalidOperationException("Paint recovery lost document content or trapped input.");
        AppLog.Write("Paint lifecycle check passed: nested paint, modal owner, abandoned frame, Direct2D recovery.");
    }
}
