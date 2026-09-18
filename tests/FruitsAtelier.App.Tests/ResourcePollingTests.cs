using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;

internal static class ResourcePollingTests
{
    public static void RefreshAndStaleResults()
    {
        string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "atelier-poll-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "map.osu"), audio = Path.Combine(root, "audio.mp3"), image = Path.Combine(root, "background.jpg");
            File.WriteAllText(source, "source"); File.WriteAllText(audio, "audio"); File.WriteAllText(image, "image");
            var map = new MapDocument { SourcePath = source, AudioPath = audio };
            var events = new OsuSection { Name = "Events" }; events.Lines.Add("0,0,\"background.jpg\",0,0"); map.OriginalSections.Add(events);
            var project = BeatmapProject.FromDocuments([map]);
            var view = new EditorView();
            view.LoadWorkspace(new(root, new WorkspaceManifest { Name = project.Name }, project));
            var canvas = new RecordingCanvas();
            void Paint() { canvas.Clear(); view.Render(canvas, 1440, 900); }
            bool Shows(string path) => canvas.Texts.Any(t => t.Value.Contains(path));
            Task Queue()
            {
                typeof(EditorView).GetField("nextResourceCheck", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, DateTime.MinValue);
                Paint();
                return (Task)typeof(EditorView).GetField("resourceCheckTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            }
            void Finish(Task task) { Check(task.Wait(TimeSpan.FromSeconds(5)), "Resource poll did not finish."); Paint(); }
            Paint(); Check(!Shows(image), "Existing background reported missing.");
            File.Delete(image); Finish(Queue()); Check(Shows(image), "Deletion was not detected from cached references.");
            File.WriteAllText(image, "restored"); Finish(Queue()); Check(!Shows(image), "Restored background remained missing.");

            string oldAudio = Path.Combine(root, "old-missing.mp3"), newAudio = Path.Combine(root, "new-missing.mp3");
            view.ChangeAudioPath(oldAudio);
            var stale = Queue(); Check(stale.Wait(TimeSpan.FromSeconds(5)), "Old resource poll did not finish.");
            view.ChangeAudioPath(newAudio); Paint();
            Check(!Shows(oldAudio), "A result for an older edit was published.");
            var pending = (Task)typeof(EditorView).GetField("resourceCheckTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            Finish(pending); Check(Shows(newAudio), "The current edit was not checked.");

            stale = Queue(); Check(stale.Wait(TimeSpan.FromSeconds(5)), "Project resource poll did not finish.");
            view.LoadWorkspace(new(root, new WorkspaceManifest { Name = project.Name }, project)); Paint();
            Check(!Shows(newAudio), "An old project result was published after switching projects.");
            pending = (Task)typeof(EditorView).GetField("resourceCheckTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            Finish(pending); Check(!view.IsDirty && !Shows(newAudio), "Resource checking changed the new project.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
