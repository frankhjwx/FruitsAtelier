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

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
