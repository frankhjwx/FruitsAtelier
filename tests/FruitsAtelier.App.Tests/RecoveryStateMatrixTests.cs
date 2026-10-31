using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class RecoveryStateMatrixTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo Field(EditorView view, string name) => view.GetType().GetField(name, Private)!;
    private static EditorHistory History(EditorView view) => (EditorHistory)view.GetType().GetProperty("history", Private)!.GetValue(view)!;

    internal static void Run()
    {
        var failures = new List<Exception>();
        foreach (var (name, check) in new (string, Action)[] {
            ("incomplete cancellation backup", IncompleteCancellationBackup),
            ("reentrant recovery", ReentrantRecovery),
            ("pattern return failure", PatternReturnFailure),
            ("retained project notes", RetainedProjectNotes),
            ("same-frame package failure", SameFrameFazFailure),
            ("modal input matrix", ModalInputMatrix),
            ("Mac native recovery episodes", MacRecoveryEpisodes),
            ("small recovery viewport", SmallRecoveryViewport),
            ("slider diagnostic budget", SliderDiagnosticBudget),
            ("diagnostic preview caps", DiagnosticPreviewCaps) })
        {
            try { check(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { Console.WriteLine("FAIL " + name + ": " + error.Message); failures.Add(error); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
        Console.WriteLine("PASS recovery state matrix and independent transition checks");
    }

    private static void IncompleteCancellationBackup()
    {
        var ui = new Ui(false);
        var history = History(ui.View);
        history.Begin("committed unsaved edit");
        ui.View.Document.Fruits.Add(new() { TimeMs = 2345, X = 123 });
        history.Commit();
        var committed = ui.View.Document.DeepClone();
        if (!history.IsDirty) throw new Exception("The cancellation fixture must retain committed unsaved edits.");
        history.Begin("unfinished invalid gesture");
        ui.View.Document.Fruits[0].X = double.NaN;
        var drag = Field(ui.View, "drag");
        drag.SetValue(ui.View, Enum.Parse(drag.FieldType, "Objects"));
        Field(ui.View, "volumeDrag").SetValue(ui.View, 0);
        ui.View.RequestAudioPreference = () => throw new IOException("Injected early cleanup failure");
        ui.View.ShowRenderError("Injected failure during unfinished gesture");
        if (!history.HasActiveTransaction) throw new Exception("The injected cleanup failure did not leave a pending gesture.");
        string path = Artifact("unfinished.catchproj");
        ui.View.SaveRecoveryCopy(path);
        var copy = ProjectSerializer.ReadProjectFile(path);
        if (!committed.ContentEquals(copy.Difficulties[0].Document) || !history.IsDirty
            || !double.IsNaN(ui.View.Document.Fruits[0].X) || !ui.View.RecoveryActive)
            throw new Exception("Recovery failed to save the last committed unsaved content independently of an invalid pending gesture.");
        ui.View.RequestAudioPreference = null;
        history.Cancel();
    }

    private static void ReentrantRecovery()
    {
        var ui = new Ui(false);
        int callbacks = 0, blockedSaves = 0;
        string path = Artifact("reentrant.catchproj");
        ui.View.RequestPausePlayback = () =>
        {
            callbacks++;
            ui.View.KeyDown(13, false, false);
            ui.View.RetryRender();
            try { ui.View.SaveRecoveryCopy(path); }
            catch (InvalidOperationException) { blockedSaves++; }
            if (callbacks == 1) ui.View.ShowRenderError("Nested recovery notification");
        };
        ui.View.ShowRenderError("Original recovery notification");
        if (!ui.View.RecoveryActive || callbacks != 1 || blockedSaves != 1 || File.Exists(path))
            throw new Exception("Nested input or notifications re-entered recovery preparation.");
        ui.View.RequestPausePlayback = null;
        ui.View.RetryRender();
        if (ui.View.ErrorVisible) throw new Exception("Recovery preparation permanently blocked a later explicit retry.");
    }

    private static void PatternReturnFailure()
    {
        foreach (bool lateFailure in new[] { false, true })
        {
            var ui = new Ui(false);
            var map = new MapDocument { Name = "Original retained draft" };
            map.Fruits.Add(new() { TimeMs = 1000, X = 123 });
            ui.LoadDocument(map);
            ui.View.Document.Fruits.Add(new() { TimeMs = 2000, X = 234 });
            Field(ui.View, "projectNotes").SetValue(ui.View, "Original unsaved retained notes");
            var original = ui.View.CaptureProject();
            var pattern = FruitsAtelier.Core.Patterns.PatternSnapshot.Capture(map, [map.Fruits[0].Id], "Pattern restoration failure");
            if (!ui.View.BeginPatternEditing(pattern)) throw new Exception("Pattern fixture did not start.");
            var drag = Field(ui.View, "drag");
            if (lateFailure) ui.View.RequestDifficultyChanged = () => throw new IOException("Injected late restoration callback failure");
            else
            {
                drag.SetValue(ui.View, Enum.Parse(drag.FieldType, "SnapDivisor"));
                ui.View.RequestViewPreference = () => throw new IOException("Injected failure before original project restoration");
            }
            try { ui.View.DiscardPatternEditing(); throw new Exception("The injected restoration failure did not occur."); }
            catch (IOException) { }
            ui.View.ShowRenderError("Restoring original project failed");
            string[] paths = ui.View.SaveRecoveryCopy(Artifact("pattern-return.catchproj"));
            var restored = paths.Select(ProjectSerializer.ReadProjectFile).Single(p => p.Name == original.Name);
            if (restored.Notes != original.Notes || !original.Difficulties[0].Document.ContentEquals(restored.Difficulties[0].Document)
                || ui.View.PatternEditingActive || paths.Length != (lateFailure ? 1 : 2))
                throw new Exception("Failed pattern restoration lost the retained original or left the wrong editing context active.");
            ui.View.RequestViewPreference = null; ui.View.RequestDifficultyChanged = null;
            drag.SetValue(ui.View, Enum.Parse(drag.FieldType, "None"));
        }
    }

    private static void RetainedProjectNotes()
    {
        var ui = new Ui(false);
        var map = new MapDocument { Name = "Same retained project" };
        map.Fruits.Add(new() { TimeMs = 1000 });
        ui.LoadDocument(map);
        var pattern = FruitsAtelier.Core.Patterns.PatternSnapshot.Capture(map, [map.Fruits[0].Id], "Notes fixture");
        if (!ui.View.BeginPatternEditing(pattern)) throw new Exception("Pattern notes fixture did not start.");
        ui.View.RequestExitPatternEditing(() => { }, retainOriginalForNavigation: true);
        ui.Paint(); ui.ClickText(L.Get("mac.discard"));
        Field(ui.View, "projectNotes").SetValue(ui.View, "Accepted newer original-project notes");
        if (!ui.View.RestoreRetainedPatternProject() || ui.View.CaptureProject().Notes != "Accepted newer original-project notes" || !ui.View.IsDirty)
            throw new Exception("Returning to the same retained project reset accepted newer notes or their dirty baseline.");
    }

    private static void SameFrameFazFailure()
    {
        var ui = new Ui(false);
        var before = ui.View.Document.DeepClone();
        int publications = 0;
        Field(ui.View, "fazSearch").SetValue(ui.View, Task.FromException<FazAssociations>(new IOException("Injected association scan failure")));
        Field(ui.View, "fazTask").SetValue(ui.View, Task.CompletedTask);
        Field(ui.View, "fazCompletion").SetValue(ui.View, (Action)(() =>
        {
            publications++;
            ui.View.LoadDocument(new MapDocument { Name = "Prepared imported project" });
        }));
        ui.Paint();
        if (!ui.View.ErrorVisible || publications != 0 || !before.ContentEquals(ui.View.Document))
            throw new Exception("A same-frame package completion replaced content behind its association error.");
        ui.View.KeyDown(13, false, false);
        ui.Paint();
        if (publications != 1 || ui.View.Document.Name != "Prepared imported project")
            throw new Exception("A deferred package completion was lost or repeated after error dismissal.");
    }

    private static void ModalInputMatrix()
    {
        foreach (bool recovery in new[] { false, true })
        foreach (string state in new[] { "plain", "startup", "save", "settings", "pattern-name", "language" })
        {
            var ui = new Ui(false);
            switch (state)
            {
                case "startup": ui.View.GetType().GetProperty("AcknowledgementsVisible")!.SetValue(ui.View, true); break;
                case "save":
                    Field(ui.View, "workspaceSaveTask").SetValue(ui.View, new TaskCompletionSource<WorkspaceSession>().Task);
                    Field(ui.View, "workspaceSaveBlocksInput").SetValue(ui.View, true); break;
                case "settings": Field(ui.View, "librarySettingsOpen").SetValue(ui.View, true); break;
                case "pattern-name": Field(ui.View, "patternNameCompletion").SetValue(ui.View, (Action<string?>)(_ => throw new Exception("Underlying pattern dialog consumed input"))); break;
                case "language": Field(ui.View, "pendingLanguage").SetValue(ui.View, "zh-CN"); break;
            }
            var before = ui.View.Document.DeepClone();
            if (recovery) ui.View.ShowRenderError("State matrix " + state); else ui.View.ShowError("State matrix " + state);
            ui.Paint();
            int saves = 0;
            ui.View.RequestRecoverySave = () => saves++;
            for (int repetition = 0; repetition < 3; repetition++)
            {
                ui.View.PointerMove(300, 200, true, true);
                ui.View.PointerDown(3, 3, 2, true, true);
                ui.View.PointerUp(3, 3, 2);
                ui.View.PointerDoubleClick(300, 200, false, false);
                ui.View.PointerLeave();
                ui.View.Wheel(300, 200, -120, false);
                ui.View.SetModifiers(true, true);
                ui.View.KeyDown(46, false, false);
                ui.View.KeyDown('Z', true, false);
                ui.View.KeyUp(46);
                ui.View.TextInput('x');
                ui.View.PasteFieldText("123");
                if (ui.View.PrepareFileOperation() || !ui.View.ErrorVisible || !before.ContentEquals(ui.View.Document))
                    throw new Exception($"Error input escaped the {state}/{recovery} matrix state.");
            }
            ui.View.KeyDown('S', true, false);
            if (saves != (recovery ? 1 : 0)) throw new Exception("Recovery save shortcut lost modal ownership.");
            var text = ui.Canvas.Texts.Single(t => t.Value == L.Get(recovery ? "testplay.retry" : "mac.ok"));
            ui.View.PointerDown(text.X + 4, text.Y + 5, 0, false, false);
            if (ui.View.ErrorVisible || !before.ContentEquals(ui.View.Document))
                throw new Exception("Visible error controls failed to own their input.");
            Field(ui.View, "workspaceSaveTask").SetValue(ui.View, null);
        }
    }

    private static void MacRecoveryEpisodes()
    {
        var ui = new Ui(false);
        int refreshes = 0, nestedInputs = 0, nativePanels = 0;
        FruitsAtelier.Mac.MacUiBoundary? boundary = null;
        boundary = new(ui.View, _ => { }, () => refreshes++);
        ui.View.RequestPausePlayback = () => boundary.Run(() => nestedInputs++);
        var renderer = new FruitsAtelier.Mac.MacRenderRecovery(boundary.Report, _ => nativePanels++);
        for (int episode = 1; episode <= 3; episode++)
        {
            renderer.BeginFrame(false);
            renderer.Fail(new IOException("Injected outer render failure"), false);
            renderer.BeginFrame(true);
            for (int repeatedFrame = 0; repeatedFrame < 10; repeatedFrame++) renderer.Fail(new IOException("Injected recovery drawing failure"), true);
            if (nestedInputs != 0 || refreshes != episode || nativePanels != episode || !ui.View.RecoveryActive)
                throw new Exception("Mac recovery allowed nested input or repeated its native fallback within one episode.");
            ui.View.RetryRender();
        }
        ui.View.RequestPausePlayback = null;
    }

    private static void SmallRecoveryViewport()
    {
        var ui = new Ui(false);
        ui.View.ShowRenderError("Transient small drawing surface");
        var canvas = new NonnegativeCanvas();
        foreach (var size in new[] { (0f, 0f), (40f, 40f), (200f, 100f), (980f, 620f) })
            ui.View.Render(canvas, size.Item1, size.Item2);
    }

    private static void SliderDiagnosticBudget()
    {
        var ui = new Ui(false);
        Field(ui.View, "sliderBatchErrors").SetValue(ui.View, new[] { new string('z', 16000) });
        var draw = ui.View.GetType().GetMethod("DrawSliderDialog", Private)!;
        var canvas = new NonnegativeCanvas();
        draw.Invoke(ui.View, [canvas]);
        int first = canvas.DiagnosticMeasurements;
        for (int frame = 0; frame < 20; frame++) draw.Invoke(ui.View, [canvas]);
        if (canvas.DiagnosticMeasurements != first)
            throw new Exception("Unchanged slider conversion diagnostics were wrapped on each frame.");
        canvas.MeasurementLimit = first + 500;
        Field(ui.View, "sliderBatchErrors").SetValue(ui.View, new[] { new string('z', 500) });
        draw.Invoke(ui.View, [canvas]);
        if (canvas.DiagnosticMeasurements <= first || canvas.DiagnosticMeasurements > first + 500)
            throw new Exception("Changed slider conversion diagnostics did not rebuild within their bounded budget.");
    }

    private static void DiagnosticPreviewCaps()
    {
        var canvas = new NonnegativeCanvas();
        var wrap = typeof(EditorView).GetMethod("WrapDiagnosticPreview", BindingFlags.Static | BindingFlags.NonPublic)!;
        string[] One(int maximumLines) => (string[])wrap.Invoke(null, [canvas, new[] { new string('z', 1000) }, 13f, 120f, 8192, maximumLines])!;
        var oneLine = One(1);
        if (oneLine.Length != 1 || oneLine[0] != "…" || One(0).Length != 0)
            throw new Exception("Diagnostic line caps hid their truncation marker outside the permitted viewport.");
        var unicode = (string[])wrap.Invoke(null, [canvas, new[] { "😀" }, 13f, 120f, 1, 2])!;
        if (unicode.Any(text => text.Any(char.IsSurrogate)) || unicode.LastOrDefault() != "…")
            throw new Exception("Diagnostic character caps split a Unicode scalar or omitted the truncation marker.");
    }

    private static string Artifact(string name)
    {
        string directory = Path.GetFullPath("artifacts/tests/recovery-state-matrix/" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }

    private sealed class NonnegativeCanvas : ICanvas
    {
        public int DiagnosticMeasurements;
        public int MeasurementLimit = 8500;
        public float MeasureText(string text, float size, bool bold = false)
        {
            if (text.Length > 0 && text[0] == 'z' && ++DiagnosticMeasurements > MeasurementLimit)
                throw new InvalidOperationException("Slider diagnostic wrapping exceeded its bounded measurement budget.");
            return text.Length * size * .6f;
        }
        private static void Check(Rect r) { if (r.Width < 0 || r.Height < 0) throw new InvalidOperationException("Diagnostic layout submitted a negative drawing size."); }
        public void Fill(Rect r, uint color, float radius = 0, float opacity = 1) => Check(r);
        public void Stroke(Rect r, uint color, float width = 1, float radius = 0) => Check(r);
        public void Line(float x1, float y1, float x2, float y2, uint color, float width = 1, float opacity = 1) { }
        public void Circle(float x, float y, float radius, uint color, bool filled = true, float width = 1, float opacity = 1) { }
        public void Text(string text, float x, float y, float size, uint color, float maxWidth = 10000, bool bold = false) { if (maxWidth < 0) throw new InvalidOperationException("Negative text width."); }
        public bool Image(string path, Rect destination, uint tint = 0xFFFFFF, Rect? source = null, float opacity = 1) => false;
        public void Clip(Rect r) => Check(r);
        public void Unclip() { }
    }
}
