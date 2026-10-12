using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class MappingCanvasBackgroundTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/mapping-background", Guid.NewGuid().ToString("N")));
        string map = Path.Combine(root, "map"), input = Path.Combine(root, "input");
        Directory.CreateDirectory(map); Directory.CreateDirectory(input);
        string old = Path.Combine(map, "background.png"), image = Path.Combine(input, "background.png");
        File.WriteAllText(old, "old image"); File.WriteAllText(image, "new image");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var document = new MapDocument { SourcePath = Path.Combine(map, "map.osu") };
                var events = new OsuSection { Name = "Events" };
                events.Lines.Add("// preserved events"); events.Lines.Add("2,5000,7000");
                document.OriginalSections.Add(events);
                var ui = new Ui(false); ui.LoadDocument(document); ui.Resize(1440, 900);
                ui.Canvas.AcceptBackgrounds = true;
                Check(ui.View.CanDropFile(image), "Editor rejected an image drop.");
                ui.View.DropLibraryFiles([image]); ui.Paint();
                Check(!ui.View.DiscardConfirmationVisible, "First background required replacement confirmation.");
                string first = OsuTimeline.BackgroundFilename(ui.View.Document)!;
                Check(first != "background.png" && File.ReadAllText(old) == "old image", "Name collision overwrote the old image.");
                Check(File.ReadAllText(Path.Combine(map, first)) == "new image", "Dropped image was not copied.");
                var texture = ui.Canvas.Images.Single(t => t.Path == Path.Combine(map, first));
                ui.View.Wheel(ui.Plot.X + 10, ui.Plot.Y + 10, 120, false, false, true); ui.Paint();
                Check(ui.Canvas.Images.Single(t => t.Path == texture.Path).Bounds == texture.Bounds, "Zoom resized the background.");
                ui.View.Wheel(ui.Plot.X + 10, ui.Plot.Y + 10, 120, false); ui.Paint();
                Check(ui.Canvas.Images.Single(t => t.Path == texture.Path).Bounds == texture.Bounds, "Scrolling moved the background.");
                ui.View.DropLibraryFiles([image]); ui.Paint();
                Check(ui.View.DiscardConfirmationVisible, "Replacement omitted confirmation.");
                ui.Key(27);
                Check(OsuTimeline.BackgroundFilename(ui.View.Document) == first, "Cancel replaced the background.");
                ui.View.DropLibraryFiles([image]); ui.Paint(); ui.ClickText(L.Get("background.replace"));
                string second = OsuTimeline.BackgroundFilename(ui.View.Document)!;
                Check(second != first && File.Exists(Path.Combine(map, first)), "Replacement removed the old image.");
                ui.Key('Z', ctrl: true);
                Check(OsuTimeline.BackgroundFilename(ui.View.Document) == first, "Undo did not restore the background.");
                ui.Key('Y', ctrl: true);
                Check(OsuTimeline.BackgroundFilename(ui.View.Document) == second, "Redo did not restore replacement.");
                Check(OsuTimeline.Breaks(ui.View.Document).Count == 1 && ui.View.Document.OriginalSections.Single(s => s.Name == "Events").Lines.Contains("// preserved events"), "Background replacement changed other events.");
                Check(OsuTimeline.BackgroundFilename(ProjectSerializer.Read(ProjectSerializer.Serialize(ui.View.Document))) == second, "Project persistence lost the background.");
                Check(OsuBeatmapWriter.Serialize(ui.View.Document).Text.Contains($"0,0,\"{second}\",0,0"), "Export lost the background.");
                var snapshot = ui.View.Document.DeepClone();
                ui.View.LibrarySettings.Workspace = root;
                ui.View.LibrarySettings.BackgroundDim = 35;
                ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.mappingCanvas"));
                var minus = ui.Canvas.Texts.Where(t => t.Value == "−" && t.X > ui.View.SettingsBounds.X + 230).OrderBy(t => t.Y).First();
                ui.Click(minus.X + 2, minus.Y + 2);
                Check(ui.View.LibrarySettings.MappingCanvasBackgroundDim == 90, "Draft applied before Apply.");
                ui.Key(27); ui.View.OpenSettings(); ui.Paint(); ui.ClickText(L.Get("settings.mappingCanvas"));
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("settings.backgroundDim", 90)), "Cancel retained a draft.");
                var dim = ui.Canvas.Texts.Single(t => t.Value == L.Get("settings.backgroundDim", 90));
                ui.View.PointerDown(dim.X + dim.MaxWidth / 2, dim.Y + 5, 0, false, false);
                ui.View.PointerUp(dim.X + dim.MaxWidth / 2, dim.Y + 5, 0); ui.Paint();
                string settings = Path.Combine(root, "settings.json");
                ui.View.ApplySettings(settings); ui.Key(27);
                Check(LibrarySettings.Load(settings).MappingCanvasBackgroundDim == 50 && ui.View.LibrarySettings.BackgroundDim == 35, "Canvas dim was not saved independently.");
                Check(snapshot.ContentEquals(ui.View.Document), "Settings changed map content.");
                Check(ui.Canvas.PaintCalls.Any(p => p.Color == 0 && p.FillBounds == texture.Bounds && Math.Abs(p.Opacity - .5f) < .001), "Canvas did not apply its dim.");
            }

            var local = new Ui(false); local.LoadDocument(new MapDocument());
            local.View.LibrarySettings.Workspace = Path.Combine(root, "workspace");
            local.View.LibrarySettings.OsuRoot = "";
            local.View.DropLibraryFiles([image]);
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (OsuTimeline.BackgroundFilename(local.View.Document) is null && timeout.Elapsed.TotalSeconds < 10)
            { Thread.Sleep(10); local.Paint(); }
            Check(local.View.WorkspaceSession is not null && local.View.IsDirty, "New map did not acquire a background in its workspace.");
            local.View.SaveWorkspace();
            var session = WorkspaceProject.Open(local.View.WorkspaceSession!.Directory);
            local.View.LoadWorkspace(session); local.Canvas.AcceptBackgrounds = true; local.Paint();
            Check(local.Canvas.Images.Any(t => t.Path == Path.Combine(session.Directory, "background.png")), "Local map lost its background after reopening.");
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}


