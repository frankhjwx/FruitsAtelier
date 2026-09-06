using Avalonia.Platform.Storage;
using FruitsAtelier.Core;
using FruitsAtelier.App.Platform;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private async Task WorkspaceSmoke(string folder)
    {
        string fixture = Path.Combine(folder, "library-fixture");
        string songs = Path.Combine(fixture, "Songs"), workspace = Path.Combine(fixture, "Workspace");
        Directory.CreateDirectory(songs);
        for (int i = 0; i < 6; i++)
        {
            string set = Path.Combine(songs, "Set " + i); Directory.CreateDirectory(set);
            var document = new MapDocument { Name = "Library song " + i, IsDemo = false };
            var metadata = new OsuSection { Name = "Metadata" };
            metadata.Lines.AddRange(["Title:Library song " + i, "TitleUnicode:曲库示例 " + i, "Artist:Fruits Atelier", "ArtistUnicode:果实工坊", "Creator:Mapper", "Version:Rain", "Tags:electronic piano"]);
            document.OriginalSections.Add(metadata);
            document.Fruits.Add(new Fruit { TimeMs = 1000, X = 120 });
            OsuBeatmapWriter.WriteFile(document, Path.Combine(set, "Rain.osu"));
        }
        View.LibrarySettings.Workspace = workspace; View.LibrarySettings.Songs = songs;
        View.ShowLibrary();
        View.SetLibraryFolder(true, workspace); View.SetLibraryFolder(false, songs);
        // Test scanning without changing the user's global settings.
        View.RefreshLibrary();
        for (int attempt = 0; attempt < 30; attempt++) { editor.Refresh(); await Task.Delay(50); }
        Capture("library-zh.png");
        View.KeyDown(70, true, false);
        foreach (char c in "果实 piano") View.TextInput(c);
        for (int attempt = 0; attempt < 8; attempt++) { editor.Refresh(); await Task.Delay(50); }
        Capture("library-search.png");
        var session = LibraryOperations.Open(new LibraryDatabase(workspace, songs).Search("").First(), View.LibrarySettings);
        View.LoadWorkspace(session);
        View.ShowWorkspaceExport(); editor.Refresh(); await Task.Delay(50); Capture("library-export.png");
        View.CloseLibrary();
        View.ChangeAudioPath(Path.Combine(songs, "missing.mp3")); View.SaveWorkspace();
        editor.Refresh(); await Task.Delay(50); Capture("workspace-missing-reference.png");
        void Capture(string name)
        {
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Avalonia.Vector(96, 96));
            bitmap.Render(editor); bitmap.Save(Path.Combine(folder, name));
        }
    }
    private void ConfigureLibrary(bool show)
    {
        View.InitializeLibrary(show);
        View.RequestLibraryFolder = workspace => RunFile(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = L.Get(workspace ? "library.workspace" : "library.songs"), AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) View.SetLibraryFolder(workspace, path);
        });
        View.RequestLibraryOpen = map => RunFile(async () =>
        {
            if (View.WorkspaceSession?.Directory == map.ProjectPath) { View.CloseLibrary(); return; }
            if (!await ConfirmDiscard()) return;
            var session = await Task.Run(() => LibraryOperations.Open(map, View.LibrarySettings));
            View.LoadWorkspace(session);
            await audio.LoadAsync(View.Document.AudioPath); PollAudio();
        });
        View.RequestWorkspaceExport = (overwrite, name) => RunFile(async () =>
        {
            if (View.WorkspaceSession is null || !View.SaveWorkspace()) return;
            var project = View.CaptureProject();
            var plan = WorkspaceExport.Plan(View.WorkspaceSession, project.Difficulties[View.ActiveDifficultyIndex], View.LibrarySettings.Songs, overwrite, name, View.CompensateTinyDroplets);
            await Task.Run(() => LibraryOperations.Export(View.WorkspaceSession, project, plan));
            View.LibraryExportFinished(); View.SetNotice(L.Get("library.exported", plan.Target));
        });
    }
}
