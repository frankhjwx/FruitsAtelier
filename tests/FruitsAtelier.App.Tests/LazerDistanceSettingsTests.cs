using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LazerDistanceSettingsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/lazer-distance-settings", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        try
        {
            var sanitized = new LibrarySettings { LazerDistanceCustomSpeeds = new[] { -1d, 0, .5, 1, 1.25, 1.25, 3, 4, double.NaN } };
            Check(sanitized.LazerDistanceCustomSpeeds.SequenceEqual([1.25, 3d]), "Custom preferences must reject invalid, duplicate and fixed speeds.");
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                ui.View.LibrarySettings.LazerEditing = true;
                ui.View.LibrarySettings.Workspace = Path.Combine(root, locale);
                ui.View.LibrarySettings.OsuRoot = "";
                int saves = 0;
                ui.View.RequestViewPreference = () => { saves++; ui.View.LibrarySettings.Save(path); };
                var map = new MapDocument { DurationMs = 4000, IsDemo = false, BeatLengthMs = 500, CircleSize = 5 };
                map.DistanceSnapRatios.AddRange([.75, 1.5]);
                ui.LoadDocument(map); ui.SetSnapDivisor(8);
                var before = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                var cached = ui.View.LazerDistanceSpeeds;
                Check(cached.SequenceEqual([.5, 1d]) && ReferenceEquals(cached, ui.View.LazerDistanceSpeeds), "Speed reads must reuse the fixed-guide snapshot.");
                ui.View.OpenDistanceSnapDialog(); ui.Paint();
                Check((int)typeof(EditorView).GetField("dsSnap", Private)!.GetValue(ui.View)! == 4,
                    "Lazer configuration defaults to quarter-beat preview independently of editor snap.");
                Check(ui.View.LibrarySettings.LazerDistanceGuidesVisible, "DS guide visibility defaults to on.");
                ui.ClickText(L.Get("ds.showGuides"));
                ui.ClickText(L.Get("mac.cancel"));
                Check(ui.View.LibrarySettings.LazerDistanceGuidesVisible, "Cancel preserves guide visibility.");
                ui.View.OpenDistanceSnapDialog(); ui.Paint();
                var track = ui.View.DistanceSnapTrackBounds;
                var preview = ui.View.DistanceSnapPreviewBounds;
                Check(ui.View.DistanceSnapPointerBounds.Count == 2 && ui.View.DistanceSnapBaseTrackBounds.Y < track.Y
                    && preview.X > track.Right && preview.Height > 0, "Lazer must reuse the SV, segmented preset and interactive preview layout.");
                foreach (string key in new[] { "ds.baseTitle", "movement.stand", "movement.walk", "movement.dash", "movement.hyperdash" })
                    Check(ui.Canvas.Texts.Any(text => text.Value == L.Get(key)), "Shared DS layout is missing " + key);
                double[] limits = Limits();
                var oracle = HyperDashCalculator.Evaluate(0, 0, 125, 512, HyperDashCalculator.CreateContext(map.CircleSize)).State.Movement!.Value;
                Check(Math.Abs(limits[1] - oracle.StandLimit / 125) < .000001 && Math.Abs(limits[2] - oracle.WalkLimit / 125) < .000001
                    && Math.Abs(limits[3] - oracle.DashLimit / 125) < .000001, "Speed bands must use the current map's actual movement thresholds.");
                foreach (var fixedArrow in ui.View.DistanceSnapPointerBounds.ToArray())
                    RightClick(fixedArrow.X + 8, fixedArrow.Y + 8);
                Check(ui.View.DistanceSnapPointerBounds.Count == 2, "Fixed guide arrows cannot be removed.");
                var fixedPointer = ui.View.DistanceSnapPointerBounds[0];
                ui.View.PointerDown(fixedPointer.X + 8, fixedPointer.Y + 8, 0, false, false);
                ui.View.PointerMove(track.Right, track.Y + 8, false, false); ui.View.PointerUp(track.Right, track.Y + 8, 0); ui.Paint();
                Check(ui.Canvas.Texts.Any(text => text.Value == L.Get("ds.lazerFixed", .5)), "Fixed guide arrows cannot be moved.");
                ui.ClickText(L.Get("ds.add"));
                Check(ui.View.DistanceSnapPointerBounds.Count == 3 && ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1d]), "Add must create a local custom speed draft.");
                Drag(1.25, cancel: true);
                Check(ui.View.DistanceSnapDialogVisible && ui.Canvas.Texts.Any(text => text.Value == L.Get("ds.lazerValue", .1)), "Escape must restore a custom speed before dragging.");
                ui.ClickText(L.Get("mac.cancel"));
                Check(ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1d]) && saves == 0, "Cancel must discard custom speed changes.");
                ui.View.OpenDistanceSnapDialog(); ui.Paint(); ui.ClickText(L.Get("ds.add")); Drag(1.25);
                ui.ClickText(L.Get("ds.showValues"));
                ui.Click(X(200), Y(0)); ui.Click(X(265), Y(.25));
                ui.View.PointerMove(preview.X - 10, preview.Y, false, false); ui.Paint();
                Check(ui.View.DistanceSnapPreviewFruits.Count == 2
                    && Math.Abs(ui.View.DistanceSnapPreviewFruits[1].X - 262.5) < .001
                    && ui.Canvas.Texts.Any(text => text.Value == L.Get("ds.lazerSpeed", .5)), "Shared preview must snap and label using px/ms speeds independently of base SV.");
                ui.ClickText(L.Get("library.apply"));
                Check(saves == 1 && ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1, 1.25]), "Apply must save global custom speeds.");
                Check(!ReferenceEquals(cached, ui.View.LazerDistanceSpeeds) && LibrarySettings.Load(path).LazerDistanceCustomSpeeds.SequenceEqual([1.25]), "Speed cache must refresh and preferences must survive restart.");
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty, "Speed-only configuration must preserve map content and dirty state.");
                ui.LoadDocument(new MapDocument { DurationMs = 3000, IsDemo = false });
                Check(ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1, 1.25]), "Speeds must apply globally across maps.");
                ui.LoadDocument(map);
                ui.View.OpenDistanceSnapDialog(); ui.Paint();
                var field = ui.View.DistanceSnapBaseFieldBounds;
                ui.Click(field.X + 10, field.Y + 10); ui.Key('A', ctrl: true); ui.View.PasteFieldText("200"); ui.Paint();
                ui.ClickText(L.Get("library.apply")); WaitBase(ui);
                Check(ui.View.Document.DistancePerBeat == 200 && ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1, 1.25])
                    && ui.View.Document.DistanceSnapRatios.SequenceEqual([.75, 1.5]), "Base SV edits must retain custom speeds and mapping DS presets.");
                ui.Key('Z', ctrl: true);
                Check(before.ContentEquals(ui.View.Document) && ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1, 1.25]), "Base SV undo must preserve global speed preferences.");
                ui.View.OpenSettings(); ui.Paint(); ui.View.ApplySettings(path); ui.Paint(); ui.Key(27);
                Check(ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1, 1.25]), "Unrelated settings must preserve global speeds.");
                ui.View.OpenDistanceSnapDialog(); ui.Paint(); ui.ClickText(L.Get("ds.showGuides"));
                ui.ClickText(L.Get("library.apply"));
                Check(!ui.View.LibrarySettings.LazerDistanceGuidesVisible && !LibrarySettings.Load(path).LazerDistanceGuidesVisible,
                    "Guide visibility persists globally.");
                ui.View.OpenSettings(); ui.Paint(); ui.View.ApplySettings(path); ui.Paint(); ui.Key(27);
                Check(!ui.View.LibrarySettings.LazerDistanceGuidesVisible, "Unrelated settings preserve hidden guides.");
                ui.View.OpenDistanceSnapDialog(); ui.Paint();
                var arrow = ui.View.DistanceSnapPointerBounds[2]; RightClick(arrow.X + 8, arrow.Y + 8);
                ui.ClickText(L.Get("library.apply"));
                Check(ui.View.LazerDistanceSpeeds.SequenceEqual([.5, 1d]), "Removing a custom arrow must retain fixed guides.");
                ui.View.LibrarySettings.LazerEditing = false;
                ui.View.OpenDistanceSnapDialog(); ui.Paint();
                Check(ui.View.DistanceSnapPointerBounds.Count == 2 && ui.View.Document.DistanceSnapRatios.SequenceEqual([.75, 1.5]), "Mapping DS presets remain independent.");
                ui.Key(27);
                ui.Resize(980, 620);
                ui.View.LibrarySettings.LazerEditing = true;
                double[] crowded = [.01, .11, .21, .31, 1.23, 1.99, 2.22, 2.99];
                ui.View.LibrarySettings.LazerDistanceCustomSpeeds = crowded;
                ui.View.OpenDistanceSnapDialog(); ui.Paint();
                Check(ui.View.DistanceSnapPointerBounds.Count == 10, "Compact shared dialog must retain all eight custom and two fixed arrows.");
                var badgeRows = ui.Canvas.Texts.Where(text => text.Y > ui.View.DistanceSnapTrackBounds.Y + 140
                    && crowded.Concat([.5, 1d]).Any(speed => text.Value == L.Get("ds.lazerValue", speed))).ToArray();
                Check(badgeRows.Length == 10 && badgeRows.Select(text => text.Y).Distinct().Count() == 1,
                    "All speed badges must fit one row at 980 by 620 without pushing lower controls beyond the dialog.");
                ui.Key(27);

                float X(double x) => preview.X + 10 + (float)(x / 512) * (preview.Width - 20);
                float Y(double beat) => preview.Bottom - 12 - (float)(beat / 4) * (preview.Height - 24);
                double[] Limits() => (double[])typeof(EditorView).GetMethod("LazerDistanceSnapLimits", Private)!.Invoke(ui.View, null)!;
                void RightClick(float x, float y)
                { ui.View.PointerDown(x, y, 2, false, false); ui.View.PointerUp(x, y, 2); ui.Paint(); }
                void Drag(double speed, bool cancel = false)
                {
                    var pointer = ui.View.DistanceSnapPointerBounds[2];
                    float fraction = (float)typeof(EditorView).GetMethod("DistanceSnapFraction", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [speed, Limits()])!;
                    float x = track.X + fraction * track.Width;
                    ui.View.PointerDown(pointer.X + 8, pointer.Y + 8, 0, false, false);
                    ui.View.PointerMove(x, track.Y + 8, true, false);
                    if (cancel) ui.Key(27);
                    ui.View.PointerUp(x, track.Y + 8, 0); ui.Paint();
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void WaitBase(Ui ui)
    {
        long now = ui.View.SliderMultiplierClock(); ui.View.SliderMultiplierClock = () => now + 1000;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (ui.View.SliderMultiplierValidationBusy && watch.ElapsedMilliseconds < 10000)
        { ui.Paint(); Thread.Sleep(1); }
        ui.Paint();
        Check(!ui.View.SliderMultiplierValidationBusy, "Base SV validation must finish.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
