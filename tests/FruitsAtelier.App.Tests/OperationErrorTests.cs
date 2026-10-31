using L = FruitsAtelier.Localization.Strings;

internal static class OperationErrorTests
{
    public static void Recovery()
    {
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(false);
            var map = new FruitsAtelier.Core.MapDocument();
            map.Fruits.Add(new() { TimeMs = 1000 });
            var other = map.DeepClone(); other.Name = "Retained inactive difficulty";
            other.Fruits[0].X = 456;
            ui.View.LoadProject(new FruitsAtelier.Core.BeatmapProject
            {
                Name = "Recovery fixture", Notes = "Unsaved project notes",
                Difficulties = [new() { Name = "Active", Document = map }, new() { Name = "Inactive", Document = other }]
            });
            ui.PaintUntilReady();
            ui.View.Document.Fruits.Add(new() { TimeMs = 2000, X = 123 });
            var retained = ui.View.CaptureProject();
            bool dirty = ui.View.IsDirty;
            if (!dirty) throw new Exception("The recovery fixture must contain unsaved authoring edits.");
            string folder = Path.GetFullPath("artifacts/tests/render-recovery/" + Guid.NewGuid());
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "unsaved.catchproj");
            var pending = typeof(FruitsAtelier.App.Editor.EditorView).GetField("difficultyGraphicsReleasePending",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            pending.SetValue(ui.View, true);
            var canvas = new FailingReleaseCanvas();
            ui.View.RequestPausePlayback = () => throw new InvalidOperationException("Injected playback cleanup failure");
            FailOnce();
            ui.View.RequestPausePlayback = null;
            for (int i = 0; i < 20; i++) ui.View.Render(canvas, 980, 620);
            if (canvas.Failures != 1 || !ui.View.RecoveryActive) throw new Exception("Recovery re-entered failed drawing without a retry.");
            ui.Key(27);
            if (!ui.View.RecoveryActive) throw new Exception("Escape resumed a failed renderer.");
            ui.View.RequestRecoverySave = () => ui.View.SaveRecoveryCopy(path);
            ui.Key('S', ctrl: true);
            var restored = FruitsAtelier.Core.ProjectSerializer.ReadProjectFile(path);
            if (restored.Difficulties.Count != retained.Difficulties.Count || restored.Notes != retained.Notes
                || retained.Difficulties.Where((difficulty, index) => !difficulty.Document.ContentEquals(restored.Difficulties[index].Document)).Any()
                || !ui.View.RecoveryActive || ui.View.IsDirty != dirty)
                throw new Exception("Recovery save changed the dirty baseline or lost unsaved content.");
            string savedText = File.ReadAllText(path);
            ui.Key('S', ctrl: true);
            if (!ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("recovery.failed", "").TrimEnd())))
                throw new Exception("Recovery copy failure was not visible.");
            if (File.ReadAllText(path) != savedText || Directory.GetFiles(folder, "*.tmp").Length != 0)
                throw new Exception("An existing recovery copy was overwritten or a failed write left temporary output.");
            ui.View.RetryRender();
            FailOnce();
            if (canvas.Failures != 2) throw new Exception("Explicit retry did not attempt rendering exactly once.");
            pending.SetValue(ui.View, false);
            ui.View.RetryRender(); ui.Paint();
            if (ui.View.ErrorVisible || !retained.Difficulties[0].Document.ContentEquals(ui.View.Document))
                throw new Exception("A repaired renderer did not resume with the retained content.");

            void FailOnce()
            {
                try { ui.View.Render(canvas, 980, 620); throw new Exception("Injected renderer did not fail."); }
                catch (InvalidOperationException error) { ui.View.ShowRenderError(error.Message); }
            }
        }
        Console.WriteLine("PASS bounded render recovery and retained unsaved project copies");
    }

    private sealed class FailingReleaseCanvas : FruitsAtelier.App.Rendering.ICanvas
    {
        public int Failures;
        public void ReleaseDifficultyResources() { Failures++; throw new InvalidOperationException("Injected difficulty resource failure"); }
        public void Fill(FruitsAtelier.App.Rendering.Rect r, uint color, float radius = 0, float opacity = 1) { }
        public void Stroke(FruitsAtelier.App.Rendering.Rect r, uint color, float width = 1, float radius = 0) { }
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1) { }
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1) { }
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false) { }
        public bool Image(string path, FruitsAtelier.App.Rendering.Rect destination, uint tint = 0xFFFFFF,
            FruitsAtelier.App.Rendering.Rect? source = null, float opacity = 1) => false;
        public void Clip(FruitsAtelier.App.Rendering.Rect r) { }
        public void Unclip() { }
    }

    public static void Run()
    {
        StaleSelection();
        foreach (bool library in new[] { false, true })
        foreach (string language in new[] { "en", "zh-CN" })
        {
            L.SetLanguage(language);
            var ui = new Ui(false); ui.Resize(980, 620);
            if (library)
            {
                ui.View.LibrarySettings.Workspace = Path.GetFullPath("artifacts/tests/error-dialog/" + Guid.NewGuid());
                ui.View.LibrarySettings.Songs = "";
                ui.View.ShowLibrary(); ui.Paint();
            }
            var before = ui.View.Document.DeepClone();
            double time = ui.View.PlayheadMs, zoom = ui.View.CanvasZoom;
            string message = "bad-map.osu\n" + string.Join('\n', Enumerable.Range(0, 30).Select(i => $"Error line {i}: 文件无法加载。"));
            ui.View.ShowError(message); ui.Paint();
            if (!ui.Canvas.Texts.Any(t => t.Value == "bad-map.osu") || !ui.Canvas.Texts.Any(t => t.Value == L.Get("files.incomplete")))
                throw new Exception("File errors must be visible inside either editor page.");
            ui.View.ShowError("secondary error"); ui.Paint();
            if (ui.Canvas.Texts.Any(t => t.Value == "secondary error")) throw new Exception("Repeated errors replaced the active message.");
            ui.Click(40, 40); ui.Key(46); ui.Key('Z', ctrl: true); ui.Type("x");
            ui.View.PasteLibraryText("paste"); ui.View.PointerDoubleClick(300, 200, false, false);
            ui.View.Wheel(400, 300, -120, false); ui.Paint();
            if (ui.Canvas.Texts.Any(t => t.Value == "bad-map.osu") || ui.View.PrepareFileOperation())
                throw new Exception("Error text did not scroll or file actions remained enabled.");
            if (!before.ContentEquals(ui.View.Document) || ui.View.PlayheadMs != time || ui.View.CanvasZoom != zoom || !ui.View.ErrorVisible)
                throw new Exception("Error input leaked through to the document.");
            ui.ClickText(L.Get("mac.ok"));
            if (ui.View.ErrorVisible || ui.View.LibraryVisible != library) throw new Exception("Dismissal did not restore the original page.");
            ui.View.ShowError("again"); ui.Key(27);
            if (ui.View.ErrorVisible) throw new Exception("Escape did not dismiss a later error.");
            ui.View.ShowError("again"); ui.Key(13);
            if (ui.View.ErrorVisible) throw new Exception("Enter did not dismiss the error.");
        }
    }

    internal static void StaleSelection()
    {
        var ui = new Ui(false);
        var map = new FruitsAtelier.Core.MapDocument();
        var fruit = new FruitsAtelier.Core.Fruit { TimeMs = 1000 }; map.Fruits.Add(fruit);
        ui.LoadDocument(map);
        typeof(FruitsAtelier.App.Editor.EditorView).GetMethod("SelectObjects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(ui.View, [new[] { Guid.NewGuid(), fruit.Id }, Guid.Empty]);
        ui.View.ShowError("Selection lookup failed."); ui.Paint();
        ui.ClickText(L.Get("mac.ok"));
        Check(); ui.Paint(); Check();

        void Check()
        {
            if (ui.View.ErrorVisible || !ui.View.SelectedObjectIds.SequenceEqual(new[] { fruit.Id })
                || !map.ContentEquals(ui.View.Document) || ui.View.IsDirty || !ui.View.PrepareFileOperation())
                throw new Exception("Error recovery must discard missing selection targets and restore usable file operations.");
        }
    }
}
