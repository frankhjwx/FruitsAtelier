using FruitsAtelier.Core;

internal static class PlaybackLineTests
{
    public static void Run()
    {
        var ui = new Ui(overview: false);
        string folder = Path.GetFullPath("artifacts/tests/playback-line-settings");
        string settingsPath = Path.Combine(folder, "settings.json");
        var settings = new LibrarySettings { Workspace = folder };
        ui.View.InitializeLibrary(false, settings);
        int saves = 0;
        ui.View.RequestViewPreference = () => { settings.Save(settingsPath); saves++; };
        ui.View.LoadDocument(new MapDocument { DurationMs = 10000, IsDemo = false });
        ui.View.UpdateTransport(0, 10000, true, false, false, null, "fixture.wav");
        ui.View.UpdateTransport(3000, 10000, true, false, false, null, "fixture.wav");
        ui.Paint();
        var original = ui.View.Document.DeepClone();
        int seeks = 0;
        ui.View.RequestSeek = _ => seeks++;
        double Y(double time) => ui.Plot.Bottom - (time - ui.View.ViewStartMs) * ui.View.PixelsPerMs;
        void Near(double expected, double actual)
        { if (Math.Abs(expected - actual) > .002) throw new Exception($"Expected {expected}, got {actual}"); }
        void Height(double fraction) => Near(ui.Plot.Y + ui.Plot.Height * fraction, Y(ui.View.PlayheadMs));
        void Begin()
        {
            var handle = ui.View.PlaybackLineHandleBounds;
            ui.View.PointerDown(ui.View.CanvasPlotBounds.X + 2, handle.Y + 8, 0, false, false);
        }
        void Move(float y) { ui.View.PointerMove(ui.View.CanvasPlotBounds.X + 2, y, false, false); ui.Paint(); }
        void End(float y) { ui.View.PointerUp(ui.View.CanvasPlotBounds.X + 2, y, 0); ui.Paint(); }
        Height(.75);
        double noteBefore = Y(3500), lineBefore = Y(3000);
        Begin();
        if (!ui.View.WantsCapture) throw new Exception("Handle did not capture pointer");
        float middle = ui.Plot.Y + ui.Plot.Height * .5f;
        Move(middle); Height(.5); Near(3000, ui.View.PlayheadMs);
        if (saves != 0) throw new Exception("Unfinished drag saved its height");
        Near(Y(3000) - lineBefore, Y(3500) - noteBefore);
        End(middle);
        Begin(); Move(ui.Plot.Y - 100); Height(.05); End(ui.Plot.Y - 100);
        Begin(); Move(ui.Plot.Bottom + 100); Height(.95); End(ui.Plot.Bottom + 100);
        Begin(); Move(middle); ui.Key(27); Height(.95);
        Begin(); Move(middle); ui.View.CancelInteraction(); ui.Paint(); Height(.95);
        ui.Resize(980, 620); Height(.95);
        ui.View.UpdateTransport(3500, 10000, true, true, false, null, "fixture.wav"); ui.Paint(); Height(.95);
        Begin();
        if (ui.View.WantsCapture) throw new Exception("Playing handle accepted drag");
        Move(ui.Plot.Y); End(ui.Plot.Y); Height(.95); Near(3500, ui.View.PlayheadMs);
        ui.View.UpdateTransport(4000, 10000, true, false, false, null, "fixture.wav"); ui.Paint();
        Begin(); Move(ui.Plot.Y + ui.Plot.Height * .3f);
        ui.View.UpdateTransport(4100, 10000, true, true, false, null, "fixture.wav"); ui.Paint(); Height(.95);
        if (ui.View.WantsCapture) throw new Exception("Playback start retained handle capture");
        ui.View.Wheel(ui.Plot.X + 30, ui.Plot.Y + 30, 120, true, true); ui.Paint(); Height(.95);
        if (seeks != 0 || !original.ContentEquals(ui.View.Document)) throw new Exception("Handle drag sought time or edited map content");
        if (saves != 3) throw new Exception("Cancelled or disabled drag saved its height");
        var reopened = new Ui(overview: false);
        reopened.View.InitializeLibrary(false, LibrarySettings.Load(settingsPath));
        reopened.View.LoadDocument(original);
        reopened.Resize(1100, 700);
        Near(reopened.Plot.Y + reopened.Plot.Height * .95, reopened.View.PlaybackLineHandleBounds.Y + 8);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<LibrarySettings>("{}")!;
        Near(.25, legacy.PlaybackLineFromBottom);
        legacy.PlaybackLineFromBottom = -1; Near(.05, legacy.PlaybackLineFromBottom);
        legacy.PlaybackLineFromBottom = 2; Near(.95, legacy.PlaybackLineFromBottom);
        legacy.PlaybackLineFromBottom = double.NaN; Near(.25, legacy.PlaybackLineFromBottom);
    }
}
