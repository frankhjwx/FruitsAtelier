using FruitsAtelier.Core;

internal static class ScreenshotTests
{
    private sealed class Clock : TimeProvider { public override long GetTimestamp() => 0; }
    public static void Shortcut()
    {
        var legacySession = new CatchTestplaySession(new CatchTestplay(
            [new(Guid.NewGuid(), 0, CatchObjectKind.Fruit, 15000, 256, 256, 256, 0)], 5, 0),
            new(0, 1, 0, false), 0, false, false, 123, 39, 16, new Clock(), 5, []);
        legacySession.ToggleAutoplay(); legacySession.SetKey(123, true);
        Check(legacySession.Capture().Autoplay, "An older F12 movement binding must not interrupt autoplay during screenshots.");
        var ui = new Ui(false);
        var map = new MapDocument { IsDemo = false, DurationMs = 20000 };
        map.Fruits.Add(new() { TimeMs = 15000, X = 256 });
        ui.LoadDocument(map);
        var before = ui.View.Document.DeepClone();
        bool dirty = ui.View.IsDirty;
        int requests = 0;
        ui.View.RequestScreenshot = () => requests++;
        ui.Key(123); ui.Key(123);
        Check(requests == 1, "F12 repeat must request only one screenshot.");
        ui.View.KeyUp(123); ui.Key(123);
        Check(requests == 2, "F12 must rearm on release.");
        ui.View.CancelInteraction(preserveTestplay: true); ui.Key(123);
        Check(requests == 3, "Focus cancellation must rearm F12.");
        ui.View.KeyUp(123); ui.Key(123, ctrl: true); ui.Key(123, shift: true);
        ui.View.SetModifiers(true, false); ui.Key(123); ui.View.SetModifiers(false, false);
        Check(requests == 3, "Modified F12 must not capture.");
        ui.View.OpenSongSetup(); ui.Paint();
        ui.Key(123); ui.View.KeyUp(123);
        Check(requests == 4 && ui.View.SongSetupVisible, "F12 must capture without dismissing the modal.");
        ui.Key(27);
        ui.View.StartTestplay(); ui.Paint();
        double position = ui.View.PlayheadMs;
        ui.Key(123); ui.View.KeyUp(123);
        Check(requests == 5 && ui.View.IsTestplaying && !ui.View.TestplayPaused && ui.View.PlayheadMs >= position,
            "F12 must preserve running testplay.");
        ui.Key(27); ui.View.KeyUp(27);
        position = ui.View.PlayheadMs;
        ui.Key(123); ui.View.KeyUp(123);
        Check(requests == 6 && ui.View.TestplayPauseMenuVisible && ui.View.PlayheadMs == position,
            "F12 must preserve the paused frame and menu.");
        ui.View.StopTestplay();
        ui.View.ShowError("Screenshot fixture"); ui.Paint(); ui.Key(123); ui.View.KeyUp(123);
        Check(requests == 7 && ui.View.ErrorVisible, "F12 must work above error dialogs.");
        ui.Key(27);
        Check(ui.View.Document.ContentEquals(before) && ui.View.IsDirty == dirty, "Screenshots must preserve beatmap content and history.");
        ui.View.MarkSaved(); ui.View.ShowLibrary(); ui.Paint();
        var libraryDocument = ui.View.Document.DeepClone();
        ui.Key(123);
        Check(requests == 8 && ui.View.LibraryVisible, "F12 must work in Library.");
        Check(ui.View.Document.ContentEquals(libraryDocument), "Library screenshots must preserve its document state.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
