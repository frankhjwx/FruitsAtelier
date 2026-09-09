using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FruitsAtelier.App.Platform;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow : Window
{
    private readonly EditorControl editor = new();
    private readonly MacAudio audio;
    private readonly MacHitsoundPlayer hitsounds;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private string? projectPath;
    private bool busy, allowClose;
    private FruitsAtelier.App.Editor.EditorView View => editor.View;
    public MacWindow(string? initialPath, bool smokeCheck)
    {
        audio = new(smokeCheck);
        hitsounds = new(smokeCheck);
        View.RequestScheduleHitsound = (sound, time) =>
        {
            if (audio.HitsoundDeviceTime(time) is { } deadline) hitsounds.Schedule(sound, deadline);
        };
        View.RequestPrepareHitsound = hitsounds.Prepare;
        View.RequestStopHitsounds = hitsounds.Stop;
        Width = 1440; Height = 900; MinWidth = 980; MinHeight = 620;
        Content = editor; Title = L.Get("window.initialTitle");
        string icon = Path.Combine(AppContext.BaseDirectory, "assets", "branding", "app-icon.png");
        if (File.Exists(icon)) Icon = new WindowIcon(icon);
        editor.Changed = UpdateTitle;
        View.RequestClose = Close;
        View.RequestLanguagePreference = language => RunFile(() => { FruitsAtelier.Localization.LanguagePreference.SaveLanguage(language); return Task.CompletedTask; });
        View.RequestOpen = () => RunFile(async () => { if (await ConfirmDiscard()) { var path = await Pick(L.Get("files.open"), ["*.osz", "*.osu", "*.catchproj", "*.catchdiff"]); if (path is not null) await OpenPath(path); } });
        View.RequestNewProject = () => RunFile(async () =>
        {
            if (!await ConfirmDiscard()) return;
            await audio.LoadAsync(null); projectPath = null; View.NewProject(); View.CloseLibrary();
        });
        View.RequestImportDifficulty = () => RunFile(async () =>
        {
            if (!View.PrepareFileOperation()) return;
            var path = await Pick(L.Get("project.import"), ["*.osu"]);
            if (path is not null)
            {
                var document = OsuBeatmapReader.ReadFile(path);
                LibraryOperations.ImportFolder(Path.GetDirectoryName(path)!, View.LibrarySettings);
                if (View.AddDifficulty(document))
                { await audio.LoadAsync(null); await audio.LoadAsync(View.Document.AudioPath); audio.Seek(View.PlayheadMs); PollAudio(); }
            }
        });
        View.RequestDifficultyChanged = () => RunFile(async () =>
        {
            await audio.LoadAsync(null);
            await audio.LoadAsync(View.Document.AudioPath);
            audio.Seek(View.PlayheadMs);
            PollAudio();
        });
        View.RequestSave = () => RunFile(async () => { await Save(false); });
        View.RequestSaveAs = () => RunFile(async () => { await Save(true); });
        View.RequestExport = View.ShowWorkspaceExport;
        ConfigureLibrary(initialPath is null && !smokeCheck, smokeCheck);
        View.RequestAudio = () => RunFile(async () =>
        {
            if (!View.PrepareFileOperation()) return;
            var path = await Pick(L.Get("files.audio"), ["*.mp3", "*.ogg", "*.wav"]);
            if (path is not null) { View.ChangeAudioPath(path); await audio.LoadAsync(path); }
        });
        View.RequestLoadSkin = () => RunFile(async () =>
        {
            View.CancelInteraction(); var path = await Pick(L.Get("files.skin"), ["*.osk"]);
            if (path is not null) View.LoadSkin(SkinArchive.Import(path, Path.Combine(MacPaths.Artifacts, "skins")));
        });
        View.RequestResetDemo = () => RunFile(async () => { if (await ConfirmDiscard()) { await audio.LoadAsync(null); projectPath = null; View.LoadDocument(DemoMap.Create()); } });
        View.RequestTogglePlayback = () => { if (audio.State.IsPlaying) audio.Pause(); else { var state = audio.State; View.StartHitsounds(state.PositionMs >= state.DurationMs - 1 ? 0 : state.PositionMs); audio.Play(); } PollAudio(); };
        View.RequestSeek = time => { audio.Seek(time); PollAudio(); };
        timer.Tick += (_, _) => PollAudio();
        Opened += async (_, _) =>
        {
            MacPaths.Log($"Native macOS window opened: {Bounds}, scaling={RenderScaling}");
            timer.Start(); editor.Focus();
            string defaultSkin = Path.Combine(AppContext.BaseDirectory, "assets", "skins", "default.osk");
            if (File.Exists(defaultSkin))
            {
                try { View.LoadSkin(SkinArchive.Import(defaultSkin, Path.Combine(MacPaths.Artifacts, "skins"))); }
                catch (Exception error) { View.SetNotice(L.Get("window.defaultSkinFailed", error.Message)); }
            }
            if (initialPath is not null) RunFile(() => OpenPath(initialPath));
            if (smokeCheck)
            {
                try
                {
                    await Task.Delay(700);
                    await SmokeCheck();
                    MacPaths.Log("SMOKE PASS");
                }
                catch (Exception ex) { MacPaths.Log("SMOKE FAIL " + ex); Environment.ExitCode = 1; }
                allowClose = true; Close();
            }
        };
        Closing += (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            RunFile(async () => { if (await ConfirmDiscard()) { allowClose = true; Close(); } });
        };
        Closed += (_, _) => { timer.Stop(); hitsounds.Dispose(); audio.Dispose(); editor.Dispose(); };
        Deactivated += (_, _) => { View.CancelInteraction(); editor.Refresh(); };
    }
    private void UpdateTitle() => Title = L.Get("window.title", View.ProjectName, View.IsDirty ? " *" : "", L.Get(View.Document.IsDemo ? "window.demo" : "window.milestone"));
    private void PollAudio()
    {
        var state = audio.State;
        if (!string.Equals(state.FilePath, View.Document.AudioPath, StringComparison.Ordinal))
        {
            _ = audio.LoadAsync(View.Document.AudioPath); state = audio.State;
        }
        View.UpdateTransport(state.PositionMs, state.DurationMs, state.CanPlay, state.IsPlaying, state.IsLoading, state.Error is null ? null : L.Reformat(state.Error), state.FilePath);
        if (View.LibraryVisible || View.WorkspaceSession is not null || View.SliderConversionBusy || View.StarRatingsRefreshing || state.IsPlaying || state.IsLoading || View.AudioReady != lastReady || Math.Abs(state.PositionMs - lastPosition) > 0.1 || state.Error != lastError)
            editor.Refresh();
        lastReady = state.CanPlay; lastPosition = state.PositionMs; lastError = state.Error;
    }
    private bool lastReady;
    private double lastPosition;
    private string? lastError;
    private async void RunFile(Func<Task> operation)
    {
        if (busy) return;
        busy = true; editor.IsEnabled = false;
        try { await operation(); }
        catch (Exception error) { MacPaths.Log(error.ToString()); View.SetNotice(L.Get("files.failed", error.Message)); await Message(L.Get("files.incomplete"), error.Message); }
        finally { busy = false; editor.IsEnabled = true; editor.Refresh(); editor.Focus(); }
    }
    private async Task<bool> ConfirmDiscard()
    {
        if (!View.PrepareFileOperation()) return false;
        if (!View.IsDirty) return true;
        int answer = await Message(L.Get("app.name"), L.Get("window.confirmDiscard"), true);
        return answer == 2 || answer == 1 && await Save(false);
    }
    private async Task<int> Message(string title, string text, bool confirm = false)
    {
        var dialog = new Window { Title = title, Width = 500, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        void Button(string key, int result) { var button = new Button { Content = L.Get(key) }; button.Click += (_, _) => dialog.Close(result); buttons.Children.Add(button); }
        if (confirm) { Button("mac.save", 1); Button("mac.discard", 2); Button("mac.cancel", 0); }
        else Button("mac.ok", 0);
        dialog.Content = new StackPanel { Margin = new Thickness(24), Spacing = 20, Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 400 }, buttons } };
        return await dialog.ShowDialog<int>(this);
    }
    private async Task<string?> Pick(string title, string[] patterns, string? directory = null)
    {
        var folder = directory is null ? null : await StorageProvider.TryGetFolderFromPathAsync(directory);
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = false, SuggestedStartLocation = folder, FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> SavePicker(string title, string extension, string filename)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = title, SuggestedFileName = filename, DefaultExtension = extension, ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(title) { Patterns = ["*." + extension] }] });
        return file?.TryGetLocalPath();
    }
    private async Task OpenPath(string path)
    {
        if (Path.GetFileName(path).Equals(WorkspaceProject.ManifestName, StringComparison.OrdinalIgnoreCase))
        {
            View.LoadWorkspace(WorkspaceProject.Open(Path.GetDirectoryName(path)!));
            await audio.LoadAsync(View.Document.AudioPath); PollAudio(); return;
        }
        if (Path.GetExtension(path).Equals(".catchdiff", StringComparison.OrdinalIgnoreCase))
        {
            View.LoadWorkspace(WorkspaceProject.Open(Path.GetDirectoryName(path)!));
            await audio.LoadAsync(View.Document.AudioPath); PollAudio(); return;
        }
        var session = await Task.Run(() => LibraryOperations.ImportPath(path, View.LibrarySettings));
        View.LoadWorkspace(session);
        View.RefreshLibrary();
        projectPath = Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase) ? path : null;
        await audio.LoadAsync(View.Document.AudioPath);
        PollAudio();
    }

    private async Task<bool> Save(bool saveAs)
    {
        if (!View.PrepareFileOperation()) return false;
        await Task.CompletedTask;
        return View.SaveWorkspace(saveAs);
    }

    private static string SafeName(string name) => string.IsNullOrWhiteSpace(name) ? L.Get("files.untitled") : new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != ':').Take(100).ToArray());
    private async Task SmokeCheck()
    {
        string folder = Path.Combine(MacPaths.Artifacts, "macos-check"); Directory.CreateDirectory(folder);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
        bitmap.Render(editor); bitmap.Save(Path.Combine(folder, "editor.png"));
        using (var cache = new ImageCache())
        {
            var originalImage = (WriteableBitmap)cache.Get(Path.Combine(folder, "editor.png"), 0xFFFFFF)!;
            var tintedImage = (WriteableBitmap)cache.Get(Path.Combine(folder, "editor.png"), 0x804020)!;
            using var originalPixels = originalImage.Lock();
            using var tintedPixels = tintedImage.Lock();
            for (int channel = 0; channel < 4; channel++)
            {
                int factor = new[] { 32, 64, 128, 255 }[channel];
                int expected = System.Runtime.InteropServices.Marshal.ReadByte(originalPixels.Address, channel) * factor / 255;
                if (System.Runtime.InteropServices.Marshal.ReadByte(tintedPixels.Address, channel) != expected)
                    throw new InvalidOperationException("PNG tint or alpha changed unexpectedly");
            }
        }
        int original = View.Document.Fruits.Count;
        View.KeyDown(70, false, false);
        var bounds = View.PlayfieldBounds;
        View.PointerDown(bounds.Right - 10, bounds.Y + bounds.Height / 2, 0, false, false);
        View.PointerUp(bounds.Right - 10, bounds.Y + bounds.Height / 2, 0);
        if (View.Document.Fruits.Count != original + 1) throw new InvalidOperationException("Fruit placement failed");
        View.KeyDown(90, true, false);
        if (View.Document.Fruits.Count != original) throw new InvalidOperationException("Undo failed");
        ProjectSerializer.WriteFile(View.Document, Path.Combine(folder, "smoke.catchproj"));
        var restored = ProjectSerializer.ReadFile(Path.Combine(folder, "smoke.catchproj"));
        if (!View.Document.ContentEquals(restored)) throw new InvalidOperationException("Project round-trip failed");
        View.AddDifficulty();
        ProjectSerializer.WriteFile(View.CaptureProject(), Path.Combine(folder, "multi.catchproj"));
        View.LoadProject(ProjectSerializer.ReadProjectFile(Path.Combine(folder, "multi.catchproj")));
        if (View.DifficultyCount != 2 || !View.SwitchDifficulty(1) || View.Document.Fruits.Count != 0)
            throw new InvalidOperationException("Multi-difficulty project round-trip failed");
        var tabPreview = View.CaptureProject();
        tabPreview.Difficulties[0].Name = "Rain";
        tabPreview.Difficulties[1].Name = "Cup";
        foreach (int interval in new[] { 300, 150 })
        {
            var fixture = new MapDocument { Name = tabPreview.Name, IsDemo = false };
            for (int index = 0; index < 80; index++)
                fixture.Fruits.Add(new Fruit { TimeMs = 1000 + index * interval, X = index % 2 == 0 ? 90 : 422 });
            tabPreview.Difficulties.Add(new ProjectDifficulty { Name = interval == 300 ? "Platter" : "A very long difficulty name", Document = fixture });
        }
        View.LoadProject(tabPreview);
        editor.Refresh();
        using (var multi = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96)))
        {
            multi.Render(editor);
            View.SwitchDifficulty(2);
            editor.Refresh();
            multi.Render(editor); multi.Save(Path.Combine(folder, "project-difficulties.png"));
            View.PointerDown(700, 500, 0, false, false);
        }
        L.SetLanguage("en");
        editor.Refresh();
        using var english = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
        english.Render(editor); english.Save(Path.Combine(folder, "editor-en.png"));
        Width = 980;
        await Task.Delay(150);
        editor.Refresh();
        using var narrow = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
        narrow.Render(editor); narrow.Save(Path.Combine(folder, "project-tabs-narrow.png"));
        L.SetLanguage("zh-CN");
        await WorkspaceSmoke(folder);
        await SliderConversionSmoke(folder);
    }
}
