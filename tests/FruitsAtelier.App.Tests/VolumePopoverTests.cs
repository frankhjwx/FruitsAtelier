using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class VolumePopoverTests
{
    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ticks;
        public void Advance(int ms) => ticks += ms;
    }

    public static void Run()
    {
        HoverAndWheel(false);
        HoverAndWheel(true);
        var clock = new ManualClock();
        var ui = new Ui(timeProvider: clock);
        var settings = new LibrarySettings();
        ui.View.InitializeLibrary(false, settings);
        ui.LoadDocument(new MapDocument { DurationMs = 5000 });
        var original = ui.View.Document.DeepClone();
        float song = -1, effect = -1;
        int saves = 0;
        ui.View.RequestAudioVolume = (s, e) => { song = s; effect = e; };
        ui.View.RequestAudioPreference = () => saves++;
        ui.Paint();
        var button = ui.View.VolumeButtonBounds;
        ui.Click(button.X + button.Width / 2, button.Y + button.Height / 2);
        Check(ui.View.VolumePopoverVisible, "Permanent volume button did not open the popover.");

        clock.Advance(60); ui.Paint();
        float opening = Opacity();
        Check(opening > .45f && opening < .55f, "Volume popover did not fade in over 120 ms.");
        clock.Advance(60); ui.Paint();
        Check(Opacity() > .99f, "Volume popover did not finish fading in.");
        var labels = new[] { L.Get("volume.master"), L.Get("volume.music"), L.Get("volume.effect") }
            .Select(label => ui.Canvas.Texts.Single(text => text.Value == label)).ToArray();
        Check(labels[0].X < labels[1].X && labels[1].X < labels[2].X,
            "Volume bars are not ordered Master, Music, Effect.");
        clock.Advance(500); ui.Paint();
        Check(Opacity() > .99f, "Hovering over the volume button did not keep the popover open.");

        var master = ui.View.VolumeBarBounds(0);
        ui.View.PointerDown(master.X + master.Width / 2, master.Bottom - 1, 0, false, false);
        Check(ui.View.WantsCapture, "Vertical volume bar did not capture dragging.");
        clock.Advance(500); ui.Paint();
        Check(Opacity() > .99f, "Holding a volume bar did not keep the popover open.");
        ui.View.PointerMove(master.X + master.Width / 2, master.Y + master.Height / 2, false, false);
        ui.View.PointerUp(master.X + master.Width / 2, master.Y + master.Height / 2, 0);
        Check(settings.MasterVolume == 50 && Math.Abs(song - .5f) < .001f
            && Math.Abs(effect - .5f) < .001f && saves == 1, "Master bar did not apply and persist the live gain.");

        ui.View.SetModifiers(true, false);
        ui.Key(39); ui.View.KeyUp(39);
        ui.Key(40); ui.View.KeyUp(40);
        Check(settings.SongVolume == 95 && Math.Abs(song - .475f) < .001f && saves == 2,
            "Keyboard volume change did not apply and persist Music.");
        ui.Key(39); ui.View.KeyUp(39);
        ui.Key(40); ui.View.KeyUp(40);
        ui.View.SetModifiers(false, false);
        Check(settings.HitsoundVolume == 95 && Math.Abs(effect - .475f) < .001f,
            "Alt+Right and Alt+Down did not reach Effect.");
        int beforePlainArrow = settings.HitsoundVolume;
        ui.Key(38);
        Check(settings.HitsoundVolume == beforePlainArrow, "Plain arrow changed volume.");

        ui.View.PointerMove(ui.Plot.X, ui.Plot.Y, false, false);
        clock.Advance(799); ui.Paint();
        Check(Opacity() > .99f, "Popover faded before 0.8 seconds of inactivity.");
        clock.Advance(76); ui.Paint();
        Check(Opacity() > .45f && Opacity() < .55f, "Popover did not fade out over 150 ms.");
        clock.Advance(75); ui.Paint();
        Check(!ui.View.VolumePopoverVisible && !ui.View.IsDirty && original.ContentEquals(ui.View.Document),
            "Popover failed to close without changing map content.");

        ui.Key('B'); ui.ClickMap(1000, 100);
        var draft = ui.View.Document.DeepClone();
        Check(draft.Tracks.Single().Nodes.Count == 1, "Draft fixture did not create one slider point.");
        ui.Click(button.X + button.Width / 2, button.Y + button.Height / 2);
        Check(ui.View.VolumePopoverVisible && draft.ContentEquals(ui.View.Document),
            "Opening volume finished or discarded a slider draft.");
        ui.View.SetModifiers(true, false);
        ui.Key(39); ui.View.KeyUp(39);
        ui.View.SetModifiers(false, false);
        Check(draft.ContentEquals(ui.View.Document), "Alt volume shortcut changed a slider draft.");
        ui.Key(27);
        Check(draft.ContentEquals(ui.View.Document) && !ui.View.VolumePopoverVisible,
            "Closing volume discarded a slider draft.");

        var modal = new Ui(); modal.View.OpenVolumeDialog(); modal.Paint();
        Check(modal.View.VolumeDialogVisible, "Modal fixture did not open.");
        var modalButton = modal.View.VolumeButtonBounds;
        modal.Click(modalButton.X + modalButton.Width / 2, modalButton.Y + modalButton.Height / 2);
        Check(!modal.View.VolumePopoverVisible && modal.View.VolumeDialogVisible,
            "Volume popover opened behind a modal dialog.");

        var menuUi = new Ui();
        menuUi.ClickText(L.Get("ui.view"));
        var menuVolume = menuUi.Canvas.Texts.Single(text => text.Value == L.Get("volume.title")
            && text.Y < menuUi.View.VolumeButtonBounds.Y);
        menuUi.Click(menuVolume.X + 4, menuVolume.Y + 5);
        Check(menuUi.View.VolumePopoverVisible, "View menu did not open the volume popover.");

        float Opacity() => ui.Canvas.PaintCalls.Last(call => call.FillBounds == ui.View.VolumePopoverBounds).Opacity;
    }

    public static void TestplayShortcuts()
    {
        var clock = new ManualClock();
        var ui = new Ui(timeProvider: clock);
        var settings = new LibrarySettings();
        ui.View.InitializeLibrary(false, settings);
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new Fruit { TimeMs = 3000, X = 256 });
        ui.LoadDocument(map);
        int saves = 0;
        ui.View.RequestAudioPreference = () => saves++;
        var hiddenButton = ui.View.VolumeButtonBounds;
        ui.View.PointerMove(hiddenButton.X + 4, hiddenButton.Y + 4, false, false);
        ui.View.StartTestplay();
        Check(ui.View.IsTestplaying, "Testplay did not start.");

        ui.View.SetModifiers(true, false);
        ui.Key(39);
        ui.View.KeyUp(39);
        ui.Key(40);
        ui.View.KeyUp(40);
        clock.Advance(60); ui.Paint();
        Check(ui.View.IsTestplaying && ui.View.VolumePopoverVisible
            && ui.Canvas.Texts.Any(text => text.Value == L.Get("volume.music")),
            "Alt+arrows did not show the volume controls over testplay.");
        Check(settings.SongVolume == 95 && saves == 1 && ui.View.TestplayCatcherX == 256,
            "Testplay volume shortcuts changed gameplay movement or failed to save Music volume.");

        ui.View.SetModifiers(false, false);
        clock.Advance(950); ui.Paint();
        Check(!ui.View.VolumePopoverVisible, "Testplay volume controls did not fade after key release.");
        ui.View.Wheel(ui.Plot.X + 20, ui.Plot.Y + 20, 120, false, false, true);
        clock.Advance(60); ui.Paint();
        Check(ui.View.VolumePopoverVisible && ui.Canvas.Texts.Any(text => text.Value == L.Get("volume.music"))
            && settings.SongVolume == 100 && saves == 2,
            "Alt+wheel did not show the volume controls and adjust Music over testplay.");
        ui.View.Wheel(ui.Plot.X + 20, ui.Plot.Y + 20, -120, false, false, true);
        ui.Paint();
        Check(settings.SongVolume == 95 && saves == 3 && ui.View.VolumePopoverVisible,
            "Consecutive Alt+wheel did not adjust the selected volume channel.");

        ui.Key(37);
        clock.Advance(100); ui.Paint();
        Check(ui.View.TestplayCatcherX < 256, "Plain arrow stopped controlling the catcher.");
        ui.View.KeyUp(37);
        clock.Advance(950); ui.Paint();
        Check(!ui.View.VolumePopoverVisible, "Testplay volume controls did not fade after key release.");
        ui.View.StopTestplay();
        Check(map.ContentEquals(ui.View.Document), "Testplay volume adjustment changed beatmap content.");
    }

    private static void HoverAndWheel(bool testplay)
    {
        var clock = new ManualClock();
        var ui = new Ui(timeProvider: clock);
        var settings = new LibrarySettings { MasterVolume = 50, SongVolume = 50, HitsoundVolume = 50 };
        ui.View.InitializeLibrary(false, settings);
        var map = new MapDocument { DurationMs = 5000 };
        map.Fruits.Add(new Fruit { TimeMs = 3000, X = 256 });
        ui.LoadDocument(map);
        if (testplay) ui.View.StartTestplay();
        ui.View.SetModifiers(true, false);
        ui.Key(38); ui.View.KeyUp(38);
        ui.View.SetModifiers(false, false);
        clock.Advance(120); ui.Paint();

        string[] labels = ["volume.master", "volume.music", "volume.effect"];
        for (int channel = 0; channel < 3; channel++)
        {
            var bar = ui.View.VolumeBarBounds(channel);
            float x = bar.X + bar.Width / 2, y = bar.Y + bar.Height / 2;
            var before = Values();
            ui.View.PointerMove(x, y, false, false);
            clock.Advance(1000); ui.Paint();
            Check(ui.View.VolumePopoverVisible, "Hover did not keep volume controls visible.");
            Check(Values().SequenceEqual(before), "Hover changed volume without an adjustment.");
            var activeLabel = ui.Canvas.Texts.Single(t => t.Value == L.Get(labels[channel]));
            Check(labels.Where((_, i) => i != channel).All(label =>
                ui.Canvas.Texts.Single(t => t.Value == L.Get(label)).Color != activeLabel.Color),
                "Hovered volume channel did not have a distinct highlight.");
            ui.View.Wheel(x, y, -120, false);
            before[channel] -= 5;
            Check(Values().SequenceEqual(before), "Plain wheel did not adjust only the hovered channel.");
            ui.View.SetModifiers(true, false);
            ui.Key(40); ui.View.KeyUp(40);
            before[channel] -= 5;
            Check(Values().SequenceEqual(before), "Alt+Down did not adjust the hovered channel.");
            ui.Key(37); ui.View.KeyUp(37);
            ui.Key(38); ui.View.KeyUp(38);
            before[Math.Max(0, channel - 1)] += 5;
            Check(Values().SequenceEqual(before), "Alt+Left did not switch the active channel after hover.");
            ui.View.SetModifiers(false, false);
        }

        var master = ui.View.VolumeBarBounds(0);
        ui.View.Wheel(master.X + 4, master.Y + 4, -120, false);
        int masterAfterWheel = settings.MasterVolume;
        ui.View.SetModifiers(true, false);
        ui.Key(38); ui.View.KeyUp(38);
        ui.View.SetModifiers(false, false);
        Check(settings.MasterVolume == masterAfterWheel + 5, "Wheel coordinates did not select the active bar.");
        ui.View.PointerMove(ui.Plot.X, ui.Plot.Y, false, false);
        clock.Advance(950); ui.Paint();
        Check(!ui.View.VolumePopoverVisible, "Leaving the controls did not allow them to fade.");
        if (testplay) ui.View.StopTestplay();
        Check(map.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Volume hover or wheel changed map content.");

        int[] Values() => [settings.MasterVolume, settings.SongVolume, settings.HitsoundVolume];
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
