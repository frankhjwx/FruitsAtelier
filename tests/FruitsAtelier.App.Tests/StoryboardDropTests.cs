using System.Reflection;
using FruitsAtelier.App.Audio;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;

internal static class StoryboardDropTests
{
    public static void Run()
    {
        FractionalTimes();
        NativeFileDrop();
        string root = Path.GetFullPath("artifacts/tests/storyboard-drop/" + Guid.NewGuid().ToString("N"));
        string songs = Path.Combine(root, "Songs"), mapRoot = Path.Combine(songs, "map");
        Directory.CreateDirectory(mapRoot);
        string source = Path.Combine(root, "sample,one.wav");
        byte[] data = HitsoundSamples.CreateWave(CatchObjectKind.Fruit);
        File.WriteAllBytes(source, data);
        CleanRoundtrip(false);
        CleanRoundtrip(true);
        var map = new MapDocument { SourcePath = Path.Combine(mapRoot, "map.osu"), DurationMs = 6000, BeatLengthMs = 500 };
        map.Fruits.Add(new() { TimeMs = 1000, X = 256 });
        File.WriteAllText(map.SourcePath, OsuBeatmapWriter.Serialize(map).Text);
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
        ui.View.LibrarySettings.Workspace = Path.Combine(root, "workspace"); ui.View.LibrarySettings.Songs = songs;
        ui.View.ShowHitsoundingPage(); ui.Paint();
        var grid = (Rect)typeof(EditorView).GetField("hitsoundingGridBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        float x = grid.X + grid.Width * .53f, y = grid.Bottom - grid.Height / 14;
        ui.View.RequestWaveform = (_, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 1200).ToArray(), 1));
        ui.View.RequestMemoryWaveform = (_, _, _) => Task.FromResult(new AudioWaveform(Enumerable.Repeat(.5f, 1200).ToArray(), 1));
        ui.ClickText(FruitsAtelier.Localization.Strings.Get("hitsounding.precise"));
        var before = ui.View.Document.DeepClone();
        ui.View.PreviewFileDrop([source], x, y); ui.Paint(); ui.Paint();
        Check(Blocks().Length == 1 && Blocks()[0].Width > 100 && before.ContentEquals(ui.View.Document), "Single-file hover previews a full precise purple sound without an edit");
        ui.View.PreviewFileDrop([source, source], x, y); ui.Paint();
        Check(Blocks().Length == 0, "Multi-file hover has no placement preview");
        ui.View.DropLibraryFiles([source, source], x, y);
        Check(before.ContentEquals(ui.View.Document), "Multiple dropped paths are ignored without deduplication");
        ui.View.DropLibraryFiles([source], x, y); Wait(1);
        ClickSample(); ui.Key(46); Wait(0);
        Check(before.ContentEquals(ui.View.Document) && !ui.View.IsDirty, "Dropping then deleting restores clean content without an empty Events section");
        ui.View.DropLibraryFiles([source], x, y); Wait(1);
        var sample = Samples().Single();
        Check(sample.TimeMs % 125 == 0, "Drop snaps to the timing grid");
        Check(!File.Exists(sample.Path), "Drop does not write to Songs");
        var placed = Blocks().Single(); var placedDocument = ui.View.Document.DeepClone();
        ui.View.PointerDown(placed.X + 5, placed.Y + 10, 0, false, false);
        ui.View.PointerMove(placed.X + 35, placed.Y + 10, false, false); ui.Paint();
        Check(Blocks().Length == 1 && Math.Abs(Blocks()[0].Width - placed.Width) < .1f && Blocks()[0].X > placed.X
            && placedDocument.ContentEquals(ui.View.Document), "Precise storyboard move carries the full-width sound and retains committed content");
        ui.Key(27); ui.View.PointerUp(placed.X + 35, placed.Y + 10, 0); Wait(1);
        Check(ui.View.Document.StoryboardAudio.Single().Value.Data!.SequenceEqual(data), "Drop retains encoded bytes");
        File.Delete(source);
        var wave = WaveformDecoder.LoadMemory(sample.Path, ui.View.Document.StoryboardAudio.Single().Value.Data!, default).GetAwaiter().GetResult();
        Check(wave.DurationMs > 0, "Memory audio decodes after source deletion");
        ClickSample(); ui.Key('C', ctrl: true);
        ui.View.UpdateTransport(sample.TimeMs + 500, 6000, true, false, false, null, null); ui.Key('V', ctrl: true); Wait(2);
        Check(Samples().Select(s => s.Path).Distinct().Count() == 1, "Copy/paste reuses the audio reference");
        Check(ui.View.SaveWorkspace(), "Workspace save succeeds");
        Check(File.ReadAllBytes(sample.Path).SequenceEqual(data), "Save materializes the in-memory audio");
        ClickSample(); ui.Key('X', ctrl: true); Wait(1);
        Check(ui.View.SaveWorkspace() && File.Exists(sample.Path), "One remaining reference protects the file");
        ClickSample(); ui.Key('X', ctrl: true); Wait(0);
        Check(ui.View.SaveWorkspace() && !File.Exists(sample.Path), "Save deletes the final unreferenced copy");
        ui.Key('V', ctrl: true); Wait(1);
        Check(!File.Exists(sample.Path), "Cut/paste remains in memory until save");
        Check(ui.View.SaveWorkspace() && File.ReadAllBytes(sample.Path).SequenceEqual(data), "Cut clipboard restores bytes on save");
        ui.Key('Z', ctrl: true); Wait(0); ui.Key('Y', ctrl: true); Wait(1);
        Check(ui.View.Document.StoryboardAudio.Single().Value.Data!.SequenceEqual(data), "Undo/redo shares the retained audio");
        var roundtrip = ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document));
        Check(roundtrip.ContentEquals(ui.View.Document), "Resource metadata roundtrips without embedding bytes");
        Check(roundtrip.StoryboardAudio.Single().Value.Data is null, "Serialized document does not embed audio");
        roundtrip.StoryboardAudio.Single().Value.Cache(sample.Path);
        Check(roundtrip.StoryboardAudio.Single().Value.Data!.SequenceEqual(data), "Reopen caches saved audio");
        var empty = ui.View.Document.DeepClone();
        foreach (var events in empty.OriginalSections.Where(s => s.Name == "Events")) events.Lines.Clear();
        Check(HitsoundResourcePlan.StoryboardCopies([empty, ui.View.Document]).DisplayNames.Count == 0, "Sibling difficulty protects shared audio");
        string outside = Path.Combine(mapRoot, "other.osu");
        File.WriteAllText(outside, OsuBeatmapWriter.Serialize(ui.View.Document).Text);
        Check(HitsoundResourcePlan.StoryboardCopies([empty]).DisplayNames.Count == 0, "Unloaded difficulty protects comma-containing sample paths");
        File.Delete(outside);
        string shared = Path.Combine(mapRoot, "shared.osb");
        File.WriteAllText(shared, "[Events]\nSample,0,0,\"" + Path.GetRelativePath(mapRoot, sample.Path).Replace('\\', '/') + "\",100\n");
        Check(HitsoundResourcePlan.StoryboardCopies([empty]).DisplayNames.Count == 0, "Shared storyboard protects imported samples");
        File.Delete(shared);
        string export = Path.Combine(root, "export"); Directory.CreateDirectory(export);
        FruitsAtelier.App.Platform.BeatmapResources.Copy(ui.View.Document, export, ui.View.Document);
        Check(File.ReadAllBytes(Path.Combine(export, Path.GetRelativePath(mapRoot, sample.Path))).SequenceEqual(data), "Export copies retained audio into its destination");
        using (var mixer = new HitsoundPlayer())
        {
            var pcm = new float[4096];
            mixer.PlayImmediate(new(CatchObjectKind.Fruit, "missing-memory.wav", 1, "storyboard", Data: data)); mixer.Read(pcm, 0, pcm.Length);
            Check(pcm.Any(v => v != 0), "Memory sample reaches the mixer without a source file or output device");
            mixer.Stop(); Array.Clear(pcm);
            mixer.PlayImmediate(new(CatchObjectKind.Fruit, "invalid-memory.wav", 1, "storyboard", Data: [1, 2, 3])); mixer.Read(pcm, 0, pcm.Length);
            Check(pcm.All(v => v == 0), "Invalid memory audio is silent");
        }
        ui.View.DropLibraryFiles([Path.Combine(root, "missing.wav")], x + 30, y); Wait(2);
        Check(ui.View.Document.StoryboardAudio.Values.Any(a => a.Data is { Length: 0 }), "Failed capture retains a silent event");
        string library = Path.Combine(ui.View.LibrarySettings.Workspace, "HitsoundLibrary");
        Directory.CreateDirectory(library); string librarySample = Path.Combine(library, "library.wav");
        File.WriteAllBytes(librarySample, data);
        typeof(EditorView).GetMethod("RefreshHitsoundingLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ui.View, null); ui.Paint();
        var row = ui.Canvas.Texts.Single(t => t.Value == "library.wav");
        ui.View.PointerDown(row.X + 8, row.Y + 8, 0, false, false);
        ui.View.PointerMove(x + 60, y, false, false); ui.View.PointerUp(x + 60, y, 0); Wait(3);
        Check(File.Exists(librarySample), "Library drag retains its original file");
        var libraryAudio = ui.View.Document.StoryboardAudio.Single(p => p.Value.SourcePath == librarySample);
        Check(!File.Exists(Path.Combine(mapRoot, libraryAudio.Key)), "Library drag defers copying until save");
        Console.WriteLine("PASS storyboard drop, memory capture, source deletion, snap, clipboard, save, cleanup and undo");

        StoryboardSample[] Samples() => StoryboardReader.Read(string.Join("\n", ui.View.Document.OriginalSections
            .SelectMany(s => new[] { "[" + s.Name + "]" }.Concat(s.Lines))), null, mapRoot).Samples.ToArray();
        Rect[] Blocks() => ui.Canvas.Outlines.Where(o => o.Color == 0xA57CC7).Select(o => o.Bounds).OrderBy(r => r.X).ToArray();
        void ClickSample() { var b = Blocks().First(); ui.Click(b.X + 5, b.Y + 10); }
        void Wait(int count)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            do { ui.Paint(); if (Blocks().Length == count && Samples().Length == count) return; Thread.Sleep(5); } while (watch.Elapsed.TotalSeconds < 10);
            throw new InvalidOperationException($"Expected {count} storyboard sounds; got {Samples().Length}/{Blocks().Length}: {ui.View.StatusMessage}");
        }
        void CleanRoundtrip(bool emptyEvents)
        {
            var document = new MapDocument { DurationMs = 6000, BeatLengthMs = 500 };
            if (emptyEvents) document.OriginalSections.Add(new() { Name = "Events" });
            var test = new Ui(false); test.LoadDocument(document); test.Resize(1440, 900);
            test.View.LibrarySettings.Workspace = Path.Combine(root, "new-" + emptyEvents);
            test.View.ShowHitsoundingPage(); test.Paint();
            var baseline = test.View.Document.DeepClone();
            var bounds = test.View.StoryboardDropBounds;
            test.View.DropLibraryFiles([source], bounds.X + 40, bounds.Y + 10);
            Check(test.View.IsDirty, "New map sound addition is dirty");
            test.Key(46);
            Check(!test.View.IsDirty && baseline.ContentEquals(test.View.Document), "New map drop/delete restores clean state and preserves original empty sections");
            test.View.DropLibraryFiles([source], bounds.X + 40, bounds.Y + 10);
            string audioRoot = Path.GetDirectoryName(test.View.Document.SourcePath)!;
            string audioPath = Path.Combine(audioRoot, test.View.Document.StoryboardAudio.Single().Key);
            Check(!Directory.Exists(audioRoot), "New map keeps audio only in memory until save");
            Check(test.View.SaveWorkspace() && File.Exists(audioPath), "New map save retains audio outside the replaceable project directory");
            Check(test.View.SaveWorkspace() && File.Exists(audioPath), "Repeated new map saves retain audio");
        }
    }
    private static void FractionalTimes()
    {
        string root = Path.GetFullPath("artifacts/tests/storyboard-time");
        Directory.CreateDirectory(root);
        string shared = Path.Combine(root, "shared.osb");
        if (File.Exists(shared)) File.Delete(shared);
        var map = new MapDocument { SourcePath = Path.Combine(root, "map.osu"), DurationMs = 240000 };
        var ui = new Ui(false); ui.LoadDocument(map); ui.Resize(1440, 900);
        var edit = typeof(EditorView).GetMethod("EditHitsoundingStoryboard", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var source = StoryboardReader.Read("[Events]\nSample,203426.09271523159,3,\"cheer.wav\",100", null, root).Samples.Single();
        var baseline = ui.View.Document.DeepClone();
        Apply([], [source], false);
        Check(ui.View.Document.OriginalSections.Single(s => s.Name == "Events").Lines.Single() == "Sample,203426,3,\"cheer.wav\",100",
            "Storyboard edits must write integer milliseconds.");
        var selected = (HashSet<StoryboardSample>)typeof(EditorView).GetField("hitsoundingStoryboardSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ui.View)!;
        Check(selected.Single().TimeMs == 203426, "Retained selection must match the serialized timestamp.");
        var first = selected.Single();
        Apply([first], [first with { TimeMs = 203625.75 }], true);
        Check(selected.Single().TimeMs == 203625, "Consecutive moves must retain the quantized selection.");
        ui.Key(90, ctrl: true); ui.Key(90, ctrl: true);
        Check(baseline.ContentEquals(ui.View.Document), "Fractional storyboard edits must undo exactly.");
        File.WriteAllText(shared, "[Events]\nSample,203426,3,\"cheer.wav\",100\n");
        var sharedSample = StoryboardReader.Read("[Events]", File.ReadAllText(shared), root).Samples.Single();
        Apply([sharedSample], [sharedSample with { TimeMs = 203625.75 }], true);
        Check(File.ReadAllText(shared).Contains("Sample,203625,3,"), "Shared storyboard moves must write integer milliseconds.");
        WaitForStoryboard();
        ui.Key(90, ctrl: true);
        Check(File.ReadAllText(shared).Contains("Sample,203426,3,"), "Shared storyboard rounding must support undo.");
        WaitForStoryboard(); File.Delete(shared);

        void Apply(StoryboardSample[] remove, StoryboardSample[] add, bool move)
            => Check((bool)edit.Invoke(ui.View, [remove, add, move, null, true, null])!, "Storyboard edit failed.");
        void WaitForStoryboard()
        {
            var field = typeof(EditorView).GetField("storyboardLoad", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (field.GetValue(ui.View) is Task pending) Check(pending.Wait(TimeSpan.FromSeconds(10)), "Storyboard load timed out.");
            ui.Paint();
        }
    }
    private static void NativeFileDrop()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new System.Windows.Forms.Form();
                int previews = 0, drops = 0, clears = 0;
                using var target = new FruitsAtelier.App.Platform.EditorFileDropTarget(form.Handle,
                    (paths, x, y) => { previews++; return paths.Length == 1 && x == 20 && y == 30; },
                    () => clears++, (paths, x, y) => drops++);
                Check(target.Registered, "Windows accepts the OLE drop target");
                var position = form.PointToScreen(new System.Drawing.Point(20, 30));
                long point = ((long)position.Y << 32) | (uint)position.X;
                var data = new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.FileDrop, new[] { @"C:\fixture.wav" });
                var native = (System.Runtime.InteropServices.ComTypes.IDataObject)data;
                uint effect = 1; target.DragEnter(native, 0, point, ref effect);
                Check(effect == 1 && previews == 1 && drops == 0, "Native drag-enter only previews files");
                effect = 1; target.Drop(native, 0, point, ref effect);
                Check(effect == 1 && drops == 1 && clears == 1, "Native drop passes client coordinates and clears preview");
                data = new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.FileDrop, new[] { @"C:\one.wav", @"C:\two.wav" });
                effect = 1; target.DragEnter((System.Runtime.InteropServices.ComTypes.IDataObject)data, 0, point, ref effect);
                Check(effect == 0 && drops == 1, "Native multi-file drag can be rejected before drop");
                target.DragLeave();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Native file-drop regression failed", failure);
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
