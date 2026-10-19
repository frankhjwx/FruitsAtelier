using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class ExperimentalSnapTests
{
    public static void Run()
    {
        foreach (string locale in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(locale);
            var ui = new Ui(false);
            ui.Resize(760, 580);
            string root = Path.GetFullPath("artifacts/tests/experimental-snap/" + locale);
            ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
            ui.View.LibrarySettings.OsuRoot = "";
            string config = Path.Combine(root, "settings.json");
            var before = ui.View.Document.DeepClone();
            void Settings()
            {
                ui.View.OpenSettings(); ui.Paint();
                var label = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.experimental") && t.X < ui.View.SettingsBounds.X + 230);
                ui.Click(label.X + 2, label.Y + 2);
            }
            Settings(); ui.ClickText(L.Get("settings.romanisedOff")); ui.Key(27);
            Check(!ui.View.LibrarySettings.ExperimentalSnapDivisions, "Cancel discards experimental snap draft");
            Settings(); ui.ClickText(L.Get("settings.romanisedOff"));
            ui.View.ApplySettings(config); ui.Paint(); ui.Key(27);
            Check(LibrarySettings.Load(config).ExperimentalSnapDivisions, "Apply persists experimental snap");
            Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Snap preference preserves map content");
            void SetSnap(int value)
            {
                int[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 24, 32, 48];
                var r = ui.View.SnapSliderBounds;
                float x = r.X + 7 + Array.IndexOf(values, value) / 15f * (r.Width - 38);
                ui.Click(x, r.Y + r.Height / 2);
                Check(ui.View.SnapDivisor == value, "Snap slider selects " + value);
            }
            void Wheel(float delta)
            {
                var r = ui.View.CanvasPlotBounds;
                ui.View.Wheel(r.X + r.Width / 2, r.Y + r.Height / 2, delta, true); ui.Paint();
            }
            SetSnap(12); Wheel(60); Check(ui.View.SnapDivisor == 12, "Partial wheel notch waits");
            Wheel(60); Check(ui.View.SnapDivisor == 24, "Triplet wheel reaches 24");
            Wheel(120); Check(ui.View.SnapDivisor == 48, "Triplet wheel reaches 48");
            Wheel(120); Check(ui.View.SnapDivisor == 48, "Wheel stops at family limit");
            Wheel(-240); Check(ui.View.SnapDivisor == 12, "Wheel halves triplet family");
            SetSnap(16); Wheel(120); Check(ui.View.SnapDivisor == 32, "Binary wheel reaches 32");
            foreach (int division in new[] { 24, 32, 48 })
            foreach (bool stack in new[] { false, true })
            {
                var map = new MapDocument { DurationMs = 4000, BeatLengthMs = 480 };
                var track = new CurveTrack { Kind = CurveKind.Linear };
                track.Nodes.AddRange([new Anchor { TimeMs = 960, X = 100 }, new Anchor { TimeMs = 1440, X = 300 }]);
                map.Tracks.Add(track); ui.LoadDocument(map); ui.Key('1'); ui.Key('A', ctrl: true);
                SetSnap(division); ui.Key('F', ctrl: true, shift: true);
                Check(!ui.View.StreamDialogVisible && ui.View.StreamSnapBounds.Width > 0 && ui.View.StreamSnapDivisor == division, $"Stream retains extended division: {division}, visible={ui.View.StreamDialogVisible}, snap={ui.View.StreamSnapDivisor}, selected={ui.View.SelectedObjectIds.Count}, status={ui.View.StatusMessage}");
                if (stack) ui.ConversionMode(true);
                var r = ui.View.StreamSnapBounds;
                int index = division == 24 ? 13 : division == 32 ? 14 : 15;
                ui.Click(r.X + 7 + index / 15f * (r.Width - 62), r.Y + r.Height / 2);
                Check(!ui.View.StreamDialogVisible && ui.View.Document.Tracks.Single().StreamSnapDivisor == division, "Extended conversion commits");
                Check((ui.View.Document.Tracks.Single().Stack is not null) == stack, "Conversion mode preserved");
                var conversion = CatchStreamConverter.Convert(ui.View.Document);
                Check(conversion.Success && conversion.Objects.Count == division + 1, "Extended conversion emits expected beat subdivisions");
                var exported = OsuBeatmapWriter.Serialize(ui.View.Document);
                Check(exported.ObjectSequenceMatches && exported.ReadBack.Fruits.Count == division + 1, "Extended export matches gameplay");
                Check(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document)).ContentEquals(ui.View.Document), "Extended snap survives project reopen");
                if (stack) { ui.Key('Z', ctrl: true); ui.Key('Z', ctrl: true); }
                ui.Key('Z', ctrl: true); Check(map.ContentEquals(ui.View.Document), "Undo restores the original slider after live mode and snap edits");
            }
            foreach (int division in new[] { 24, 32, 48 })
            {
                var map = new MapDocument { BeatLengthMs = 480 };
                var shower = new BananaShower { TimeMs = 960, EndTimeMs = 1920,
                    Pattern = new BananaShowerPattern(1, division, 1, division) };
                map.BananaShowers.Add(shower);
                var segments = BananaShowerPattern.Segments(shower, new TimingMap.Lookup(map));
                Check(segments.Count == division && segments.All(s => s.EndTimeMs - s.TimeMs >= 2), "Banana patterns accept extended snap fractions");
                Check(ProjectSerializer.Read(ProjectSerializer.Serialize(map)).ContentEquals(map), "Extended banana pattern survives reopen");
                Check(OsuBeatmapWriter.Serialize(map).ObjectSequenceMatches, "Extended banana pattern exports playable objects");
            }
            SetSnap(48); var unchanged = ui.View.Document.DeepClone();
            Settings(); ui.ClickText(L.Get("settings.romanisedOn")); ui.View.ApplySettings(config); ui.Paint(); ui.Key(27);
            Check(!LibrarySettings.Load(config).ExperimentalSnapDivisions && ui.View.SnapDivisor == 16, "Disable restores standard controls and persists");
            Wheel(120); Check(ui.View.SnapDivisor == 16 && unchanged.ContentEquals(ui.View.Document), "Disabled wheel stops at 16 without content edits");
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
