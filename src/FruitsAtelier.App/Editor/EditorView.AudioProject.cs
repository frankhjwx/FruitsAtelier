using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private string? audioProjectPath;
    private bool audioProjectSongs;
    private Task<WorkspaceSession>? audioProjectTask;
    public bool AudioProjectCreating => audioProjectTask is not null;
    private static readonly string[] AudioProjectFields = ["TitleUnicode", "Title", "ArtistUnicode", "Artist", "Creator", "Version"];
    private static readonly string[] AudioProjectRequiredFields = ["TitleUnicode", "ArtistUnicode", "Creator", "Version"];

    public void BeginAudioProject(string path)
    {
        if (!CanDropAudio || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        audioProjectPath = path;
        audioProjectSongs = !string.IsNullOrWhiteSpace(LibrarySettings.Songs);
        songValues.Clear(); songInitial.Clear(); songFieldBounds.Clear(); songSliders.Clear();
        songField = "TitleUnicode"; songError = "";
        songTab = 0; songDrag = -1; songCountdownOpen = false;
        foreach (string key in AudioProjectFields) songValues[key] = "";
        SongSetupVisible = true; SongSetupInputSession++;
        FocusInput("song:" + songField, "", 0);
        hits.Clear(); fields.Clear();
    }

    private void DrawAudioProjectSetup(ICanvas c)
    {
        hits.Clear(); fields.Clear(); songFieldBounds.Clear(); songSliders.Clear();
        var r = SongSetupBounds;
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("audioProject.title"), r.X + 22, r.Y + 16, 19, Foreground, r.Width - 80, true);
        Button(c, new(r.Right - 54, r.Y + 10, 32, 28), "×", CloseSongSetup, enabled: !AudioProjectCreating);
        c.Text(Path.GetFileName(audioProjectPath) ?? "", r.X + 22, r.Y + 55, 13, Muted, r.Width - 44);
        c.Text(L.Get("audioProject.required"), r.X + 22, r.Y + 82, 12, Muted, r.Width - 44);
        for (int i = 0; i < AudioProjectFields.Length; i++) SongTextField(c, AudioProjectFields[i], r.Y + 116 + i * 40);
        ToggleSwitch(c, new(r.X + 22, r.Y + 364, r.Width - 44, 38), L.Get("audioProject.songs"),
            audioProjectSongs, () => audioProjectSongs = !audioProjectSongs && !string.IsNullOrWhiteSpace(LibrarySettings.Songs));
        c.Text(string.IsNullOrWhiteSpace(LibrarySettings.Songs) ? L.Get("audioProject.localOnly") : LibrarySettings.Songs,
            r.X + 22, r.Y + 416, 12, Muted, r.Width - 44);
        c.Text(AudioProjectCreating ? L.Get("audioProject.creating") : songError, r.X + 22, r.Bottom - 84, 12,
            AudioProjectCreating ? Muted : Error, r.Width - 44);
        c.Line(r.X + 22, r.Bottom - 60, r.Right - 22, r.Bottom - 60, Grid);
        Button(c, new(r.Right - 220, r.Bottom - 46, 92, 32), L.Get("mac.cancel"), CloseSongSetup, enabled: !AudioProjectCreating);
        Button(c, new(r.Right - 116, r.Bottom - 46, 94, 32), L.Get("audioProject.create"), CreateAudioProject,
            true, enabled: !AudioProjectCreating && AudioProjectRequiredFields.All(k => !string.IsNullOrWhiteSpace(songValues[k])));
    }

    private void CreateAudioProject()
    {
        if (AudioProjectCreating || audioProjectPath is null) return;
        if (AudioProjectRequiredFields.Any(k => string.IsNullOrWhiteSpace(songValues[k])))
        { songError = L.Get("audioProject.required"); return; }
        var document = NewAuthoringDocument();
        document.Name = songValues["TitleUnicode"].Trim(); document.AudioPath = audioProjectPath;
        foreach (string key in AudioProjectFields) SongSetup.Set(document, "Metadata", key, songValues[key].Trim());
        foreach (string key in new[] { "Title", "Artist" })
            if (!NeedsRomanisation(songValues[key + "Unicode"]) || string.IsNullOrWhiteSpace(songValues[key]))
                SongSetup.Set(document, "Metadata", key, songValues[key + "Unicode"].Trim());
        var project = BeatmapProject.FromDocuments([document]);
        string workspace = LibrarySettings.Workspace, songs = LibrarySettings.Songs;
        bool export = audioProjectSongs, compensate = compensateTinyDroplets;
        audioProjectTask = Task.Run(() => LibraryOperations.CreateAudioProject(project, workspace, songs, export, compensate));
        hits.Clear(); fields.Clear();
    }

    private void PumpAudioProject()
    {
        if (audioProjectTask is not { IsCompleted: true } task) return;
        audioProjectTask = null;
        WorkspaceSession session;
        try { session = task.GetAwaiter().GetResult(); }
        catch (Exception error) { songError = L.Reformat(error.Message); return; }
        LoadWorkspace(session);
        libraryProjectsNeedReindex = true; QueueLibrarySearch();
        RequestDifficultyChanged?.Invoke();
    }
}
