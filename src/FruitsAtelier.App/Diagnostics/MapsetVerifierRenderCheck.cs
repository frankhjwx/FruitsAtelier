using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Diagnostics;

internal static class MapsetVerifierRenderCheck
{
    internal static void Run(D2DCanvas canvas, int width, int height, int dpi)
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var view = new EditorView(false); view.LoadDocument(new() { IsDemo = false, DurationMs = 300000 }); view.CloseLibrary();
                view.VerifierAnalyze = (inputs, _, _) => Task.FromResult(new MapsetVerifierReport(1,
                    [],
                    [new(inputs[0].Id, inputs[0].Name, "Expert", 6.68,
                        [new("Overlap", "Concurrent hit objects.", "Problem", "00:01:001 (2,3) - Concurrent hit objects."),
                         new("Combo", "Too high combo.", "Warning", "04:43:381 (1) - The amount of combo exceeds the guideline of 16 significantly, currently it has 26."),
                         new("Snap", "Wrongly or inconsistently snapped hit objects.", "Warning", "00:31:193 - 04:03:100 - 04:03:736 - 04:45:399 - 1/16 makes out 0.5% or less of snappings, ensure this makes sense."),
                         .. Enumerable.Range(0, 32).Select(i => new MapsetVerifierIssue("Timing", "Concurrent or conflicting timing lines.", "Minor", $"00:03:{i:000} - Conflicting line settings. Green overrides red."))])]) { UpstreamTag = "v2.1.1" });
                string captureName = "panel";
                Paint(); view.ShowMapsetVerifier();
                var deadline = DateTime.UtcNow.AddSeconds(5);
                do { Paint(); Thread.Sleep(1); } while (view.MapsetVerifierBusy && DateTime.UtcNow < deadline);
                if (view.VerifierReport is null) throw new Exception("Native verifier report did not settle");
                Paint(true);
                string? copied = null; view.RequestCopyText = value => copied = value;
                var copy = view.VerifierCopyBounds.First();
                view.PointerMove(copy.X + 4, copy.Y + 5, false, false); Paint();
                if (!view.MapsetVerifierHandCursor) throw new Exception("Native Copy all hand cursor missing");
                view.PointerDown(copy.X + 4, copy.Y + 5, 0, false, false);
                view.PointerUp(copy.X + 4, copy.Y + 5, 0);
                captureName = "copy"; Paint(true);
                if (copied is null || view.VerifierCopyOpacity <= 0) throw new Exception("Native Copy all confirmation missing");
                var filter = view.VerifierFilterBounds("Warning");
                view.PointerDown(filter.X + 8, filter.Y + 8, 0, false, false);
                view.PointerUp(filter.X + 8, filter.Y + 8, 0);
                captureName = "filtered"; Paint(true);
                if (view.VerifierTimestampBounds.Any(t => t.TimeMs == 1001)) throw new Exception("Native exclusive severity filter failed");
                view.PointerDown(filter.X + 8, filter.Y + 8, 0, false, false);
                view.PointerUp(filter.X + 8, filter.Y + 8, 0); Paint();
                double seek = -1; view.RequestSeek = t => seek = t;
                var link = view.VerifierTimestampBounds.First();
                view.PointerDown(link.Bounds.X + 3, link.Bounds.Y + 5, 0, false, false);
                view.PointerUp(link.Bounds.X + 3, link.Bounds.Y + 5, 0); Paint();
                if (seek != link.TimeMs || view.IsDirty) throw new Exception("Native verifier timestamp click failed");
                var thumb = view.VerifierScrollbarThumb;
                if (thumb.Height <= 0) throw new Exception("Native verifier scrollbar missing");
                view.PointerDown(thumb.X + 3, thumb.Y + 4, 0, false, false);
                view.PointerMove(thumb.X + 3, view.VerifierScrollbarTrack.Bottom, false, false); Paint();
                view.PointerUp(thumb.X + 3, view.VerifierScrollbarTrack.Bottom, 0);
                if (view.WantsCapture || view.VerifierScrollOffset <= 0) throw new Exception("Native verifier scrollbar drag failed");
                var general = view.VerifierGeneralTabBounds;
                captureName = "general";
                view.PointerDown(general.X + 20, general.Y + 12, 0, false, false);
                view.PointerUp(general.X + 20, general.Y + 12, 0); Paint(true);
                view.StopMapsetVerifier();

                var offsets = new MapDocument { IsDemo = false, BeatLengthMs = 500, DurationMs = 5000 };
                offsets.Fruits.Add(new() { TimeMs = 1001, X = 100 });
                offsets.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
                    new() { TimeMs = 2001, BeatLengthMs = -100, Uninherited = false, Volume = 40 }]);
                view = new EditorView(false); view.LoadDocument(offsets); view.CloseLibrary();
                view.VerifierAnalyze = (inputs, _, _) =>
                {
                    var input = inputs[0];
                    return Task.FromResult(new MapsetVerifierReport(1, [], [new(input.Id, input.Name, "Hard", 3,
                        [.. input.Document.Fruits.Where(f => f.TimeMs == 1001).Select(_ => new MapsetVerifierIssue(
                            "MapsetVerifier.Checks.AllModes.Timing.CheckUnsnaps", "Unsnapped hit objects.", "Minor",
                            "00:01:001 - Circle unsnapped by 1 ms.")),
                         .. MapsetVerifierClient.TimingOffsetIssues(input)])]));
                };
                view.ShowMapsetVerifier();
                SettleOffsets();
                captureName = "offset-fixes"; Paint(true);
                if (view.VerifierFixBounds.Count != 2) throw new Exception("Native offset fix buttons missing");
                for (int fix = 0; fix < 2; fix++)
                {
                    var button = view.VerifierFixBounds.First();
                    view.PointerMove(button.X + 4, button.Y + 5, false, false); Paint();
                    if (!view.MapsetVerifierHandCursor) throw new Exception("Native fix hand cursor missing");
                    view.PointerDown(button.X + 4, button.Y + 5, 0, false, false);
                    view.PointerUp(button.X + 4, button.Y + 5, 0);
                    Paint();
                    if (view.VerifierFixPromptVisible)
                    {
                        captureName = "related-offsets"; Paint(true);
                        view.AnswerVerifierFix(false);
                    }
                    SettleOffsets();
                }
                if (view.Document.Fruits[0].TimeMs != 1000 || view.Document.TimingPoints[1].TimeMs != 2000
                    || !view.IsDirty || view.VerifierFixBounds.Count != 0)
                    throw new Exception("Native explicit offset fixes failed");
                view.StopMapsetVerifier();

                var kiai = new MapDocument { IsDemo = false, BeatLengthMs = 500, DurationMs = 5000 };
                kiai.Fruits.Add(new() { TimeMs = 1500, X = 100 });
                kiai.TimingPoints.AddRange([new() { TimeMs = 0, BeatLengthMs = 500 },
                    new() { TimeMs = 1001, BeatLengthMs = -100, Uninherited = false, Effects = 1 },
                    new() { TimeMs = 2001, BeatLengthMs = -100, Uninherited = false }]);
                view = new EditorView(false); view.LoadDocument(kiai); view.CloseLibrary();
                view.VerifierAnalyze = (inputs, _, _) => Task.FromResult(new MapsetVerifierReport(1, [],
                    [new(inputs[0].Id, inputs[0].Name, "Hard", 3,
                        [new("MapsetVerifier.Checks.AllModes.Timing.CheckKiaiUnsnap", "Unsnapped kiai.", "Minor",
                            "00:01:001 - Kiai is unsnapped by 1 ms."),
                         new("MapsetVerifier.Checks.AllModes.Timing.CheckUnusedLines", "Unused timing lines.", "Minor",
                            "00:02:001 - Inherited line changes sample settings, but affects nothing.")])]));
                view.ShowMapsetVerifier(); SettleOffsets();
                captureName = "kiai-fix"; Paint(true);
                if (view.VerifierFixBounds.Count != 1 || view.VerifierCopyBounds.Count != 2)
                    throw new Exception("Native Kiai and read-only unused timing actions failed");
                var kiaiButton = view.VerifierFixBounds.Single();
                view.PointerDown(kiaiButton.X + 4, kiaiButton.Y + 5, 0, false, false);
                view.PointerUp(kiaiButton.X + 4, kiaiButton.Y + 5, 0);
                Paint();
                if (!view.VerifierFixPromptVisible) throw new Exception("Native combined Kiai fix prompt missing");
                captureName = "related-kiai"; Paint(true);
                view.AnswerVerifierFix(false);
                SettleOffsets();
                if ((TimingEditing.Current(view.Document, 1000)!.Effects & 1) == 0
                    || (TimingEditing.Current(view.Document, 2000)!.Effects & 1) != 0)
                    throw new Exception("Native Kiai fix failed");
                view.StopMapsetVerifier();

                void SettleOffsets()
                {
                    var limit = DateTime.UtcNow.AddSeconds(10);
                    do { Paint(); Thread.Sleep(1); } while (view.MapsetVerifierBusy && DateTime.UtcNow < limit);
                    if (view.MapsetVerifierBusy) throw new Exception("Native offset report did not settle");
                    Paint();
                }

                void Paint(bool capture = false)
                {
                    canvas.Begin(); view.Render(canvas, width, height);
                    canvas.End(capture: !capture ? null : () =>
                    {
                        string folder = Path.GetFullPath("artifacts/tests/mapset-verifier/native"); Directory.CreateDirectory(folder);
                        var frame = canvas.CapturePixels();
                        using var writer = new BinaryWriter(File.Create(Path.Combine(folder, $"{captureName}-{locale}-{width}x{height}-{dpi}.bmp")));
                        writer.Write((ushort)0x4D42); writer.Write(54 + frame.Pixels.Length); writer.Write(0); writer.Write(54);
                        writer.Write(40); writer.Write(frame.Width); writer.Write(-frame.Height); writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(0); writer.Write(frame.Pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(frame.Pixels);
                    });
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
}
