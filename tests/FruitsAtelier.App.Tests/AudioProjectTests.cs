using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class AudioProjectTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/audio-project", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "input.MP3");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        string ogg = Path.Combine(root, "input.OGG");
        File.Copy("tests/FruitsAtelier.Audio.Tests/Fixtures/quiet-tone.ogg", ogg);
        string wav = Path.Combine(root, "input.WAV");
        using (var writer = new NAudio.Wave.WaveFileWriter(wav, new NAudio.Wave.WaveFormat(44100, 16, 2)))
            for (int i = 0; i < 44100; i++)
            {
                float sample = (float)(Math.Sin(2 * Math.PI * 440 * i / 44100) * .02);
                writer.WriteSample(sample); writer.WriteSample(sample);
            }
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (bool library in new[] { false, true })
            foreach (int mode in new[] { 0, 1, 2 })
            foreach (string input in new[] { source, ogg, wav })
            {
                L.SetLanguage(locale);
                string workspace = Path.Combine(root, $"{locale}-{library}-{mode}-{Path.GetExtension(input)}");
                string osu = Path.Combine(root, $"osu-{locale}-{library}-{mode}-{Path.GetExtension(input)}");
                var ui = new Ui(false); ui.Resize(library ? 980 : 1440, library ? 620 : 900);
                ui.View.LoadDocument(new MapDocument { Name = "Previous", IsDemo = false });
                ui.View.LibrarySettings.Workspace = workspace;
                ui.View.LibrarySettings.DerandomizeNewProjects = mode != 1;
                ui.View.LibrarySettings.OsuRoot = mode == 0 ? "" : osu;
                if (mode != 0) Directory.CreateDirectory(ui.View.LibrarySettings.Songs);
                if (library) ui.View.ShowLibrary();
                var before = ui.View.Document.DeepClone();
                int requests = 0;
                ui.View.RequestAudioProject = path => { requests++; ui.View.BeginAudioProject(path); };
                Check(ui.View.CanDropFile(input), "Audio is accepted by the native drop filter");
                ui.View.DropLibraryFiles([input]); ui.Paint();
                Check(ui.View.SongSetupVisible && requests == 1, "Audio opens setup from Library and editor");
                ui.View.DropLibraryFiles([input]);
                Check(requests == 1, "Setup rejects additional drops");
                ui.Key(13);
                Check(!ui.View.AudioProjectCreating && ui.View.SongSetupVisible && NoProject(workspace), "Empty fields cannot publish a project");
                ui.Key('S', ctrl: true); ui.Key(116);
                Check(ui.View.Document.ContentEquals(before) && !ui.View.IsTestplaying, "Setup isolates editor shortcuts");
                Set(ui, "TitleUnicode", "Cancelled"); ui.Key(27);
                Check(!ui.View.SongSetupVisible && ui.View.Document.ContentEquals(before) && NoProject(workspace), "Cancel leaves original project and filesystem intact");
                ui.View.DropLibraryFiles([input]); ui.Paint();
                Set(ui, "TitleUnicode", "歌曲 / Song"); Set(ui, "ArtistUnicode", "艺术家"); Set(ui, "Creator", "Mapper");
                Set(ui, "Version", "   "); ui.Key(13);
                Check(!ui.View.AudioProjectCreating && NoProject(workspace), "Whitespace difficulty is rejected");
                Set(ui, "Version", "Hard");
                if (mode == 2) ui.ClickText(L.Get("audioProject.songs"));
                ui.Key(13);
                Check(ui.View.AudioProjectCreating, "Creation runs asynchronously");
                ui.Key(27); ui.Key('S', ctrl: true);
                Check(ui.View.SongSetupVisible, "Pending publication cannot be cancelled or saved again");
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (ui.View.AudioProjectCreating && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
                ui.Paint();
                Check(!ui.View.AudioProjectCreating && !ui.View.SongSetupVisible && !ui.View.LibraryVisible, "Successful publication opens the editor");
                var session = ui.View.WorkspaceSession ?? throw new Exception("No audio project was created");
                Check(session.Project.Difficulties.Count == 1 && ui.View.CurrentDifficultyName == "Hard" && !ui.View.IsDirty, "One saved difficulty is created");
                var reopened = WorkspaceProject.Open(session.Directory);
                var map = reopened.Project.Difficulties.Single().Document;
                Check(map.RandomizeNewSliders == (mode == 1) && map.RandomizeDropletStrength == 20 && map.RandomizeDropletSeed == 1337,
                    "audio-created catchproject persists its independent droplet defaults");
                Check(map.Name == "歌曲 / Song" && SongSetup.Get(map, "Metadata", "Artist") == "艺术家"
                    && SongSetup.Get(map, "Metadata", "Creator") == "Mapper", "Metadata survives reopening");
                Check(map.AudioPath != input && File.ReadAllBytes(map.AudioPath!).SequenceEqual(File.ReadAllBytes(input)), "Local audio is an independent copy");
                Check(Path.GetExtension(map.AudioPath!).Equals(Path.GetExtension(input), StringComparison.OrdinalIgnoreCase)
                    && SongSetup.Get(map, "General", "AudioFilename") == Path.GetFileName(map.AudioPath), "Audio format and reference survive reopening");
                var entry = reopened.Manifest.Difficulties.Single();
                Check((entry.ExportTarget is not null) == (mode == 1), "Songs creation follows the toggle");
                if (mode == 1)
                {
                    Check(Directory.GetFiles(ui.View.LibrarySettings.Songs, "*.osu", SearchOption.AllDirectories).Length == 1, "Songs has exactly one difficulty");
                    var output = OsuBeatmapReader.ReadFile(entry.ExportTarget!);
                    Check(File.Exists(output.AudioPath) && output.Fruits.Count == 0 && output.Tracks.Count == 0
                        && SongSetup.Get(output, "General", "Mode") == "2", "Songs contains playable Catch metadata and copied audio");
                    Check(entry.Sync is not null && entry.Source == entry.ExportTarget, "Export is linked for later saves");
                    Check(ui.View.SaveWorkspace(), "Created project saves again");
                    Check(File.Exists(WorkspaceProject.Open(session.Directory).Project.Difficulties.Single().Document.AudioPath), "Snapshot save retains copied audio");
                }
                else if (mode == 2) Check(!Directory.EnumerateFileSystemEntries(ui.View.LibrarySettings.Songs).Any(), "Disabled toggle leaves Songs empty");
            }

            var retry = new Ui(false); retry.Resize(980, 620);
            retry.View.LibrarySettings.Workspace = Path.Combine(root, "retry");
            retry.View.LibrarySettings.OsuRoot = Path.Combine(root, "unavailable-osu");
            retry.View.BeginAudioProject(source); retry.Paint();
            Set(retry, "TitleUnicode", "Retry"); Set(retry, "ArtistUnicode", "Artist"); Set(retry, "Creator", "Mapper"); Set(retry, "Version", "Easy");
            retry.Key(13);
            Wait(retry);
            Check(retry.View.SongSetupVisible && retry.View.WorkspaceSession is null && NoProject(retry.View.LibrarySettings.Workspace), "Unavailable Songs keeps the draft without a partial project");
            retry.ClickText(L.Get("audioProject.songs")); retry.Key(13); Wait(retry);
            Check(retry.View.WorkspaceSession is not null, "Failed creation can retry locally with the same fields");
            string copiedAudio = retry.View.Document.AudioPath!;
            File.Delete(source);
            Check(File.Exists(copiedAudio), "Removing the source MP3 does not break the project");
        }
        finally { L.SetLanguage(language); }
    }

    private static void Wait(Ui ui)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (ui.View.AudioProjectCreating && DateTime.UtcNow < deadline) { ui.Paint(); Thread.Sleep(10); }
        ui.Paint();
    }
    private static void Set(Ui ui, string key, string value)
    {
        var r = ui.View.SongSetupFieldBounds[key]; ui.Click(r.X + 5, r.Y + 5); ui.Key('A', ctrl: true);
        ui.View.PasteSongSetupText(value, ui.View.SongSetupInputSession); ui.Paint();
    }
    private static bool NoProject(string workspace) => !Directory.Exists(workspace) || !Directory.EnumerateFiles(workspace, "*.catchdiff", SearchOption.AllDirectories).Any();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
