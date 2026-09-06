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

internal sealed class MacWindow : Window
{
    private readonly EditorControl editor = new();
    private readonly MacAudio audio;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private string? projectPath;
    private bool busy, allowClose;
    private FruitsAtelier.App.Editor.EditorView View => editor.View;
    public MacWindow(string? initialPath, bool smokeCheck)
    {
        audio = new(smokeCheck);
        Width = 1440; Height = 900; MinWidth = 980; MinHeight = 620;
        View.RendererStatusKey = "mac.renderStatus";
        Content = editor; Title = L.Get("window.initialTitle");
        editor.Changed = UpdateTitle;
        View.RequestClose = Close;
        View.RequestOpen = () => RunFile(async () => { if (await ConfirmDiscard()) { var path = await Pick(L.Get("files.open"), ["*.osz", "*.osu", "*.catchproj"]); if (path is not null) await OpenPath(path); } });
        View.RequestSave = () => RunFile(async () => { await Save(false); });
        View.RequestSaveAs = () => RunFile(async () => { await Save(true); });
        View.RequestExport = () => RunFile(Export);
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
        View.RequestTogglePlayback = () => { if (audio.State.IsPlaying) audio.Pause(); else audio.Play(); PollAudio(); };
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
        Closed += (_, _) => { timer.Stop(); audio.Dispose(); editor.Dispose(); };
        Deactivated += (_, _) => { View.CancelInteraction(); editor.Refresh(); };
    }
    private void UpdateTitle() => Title = L.Get("window.title", View.Document.Name, View.IsDirty ? " *" : "", L.Get(View.Document.IsDemo ? "window.demo" : "window.milestone"));
    private void PollAudio()
    {
        var state = audio.State;
        if (!string.Equals(state.FilePath, View.Document.AudioPath, StringComparison.Ordinal))
        {
            _ = audio.LoadAsync(View.Document.AudioPath); state = audio.State;
        }
        View.UpdateTransport(state.PositionMs, state.DurationMs, state.CanPlay, state.IsPlaying, state.IsLoading, state.Error is null ? null : L.Reformat(state.Error), state.FilePath);
        if (state.IsPlaying || state.IsLoading || View.AudioReady != lastReady || Math.Abs(state.PositionMs - lastPosition) > 0.1 || state.Error != lastError)
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
        if (Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase))
        {
            var maps = BeatmapArchive.Import(path, Path.Combine(MacPaths.Artifacts, "beatmaps"));
            path = maps.Count == 1 ? maps[0] : await Pick(L.Get("files.difficulty"), ["*.osu"], Path.GetDirectoryName(maps[0])) ?? "";
            if (path.Length == 0) return;
        }
        bool project = Path.GetExtension(path).Equals(".catchproj", StringComparison.OrdinalIgnoreCase);
        var document = project ? ProjectSerializer.ReadFile(path) : OsuBeatmapReader.ReadFile(path);
        View.LoadDocument(document); projectPath = project ? path : null;
        await audio.LoadAsync(document.AudioPath);
        PollAudio();
    }
    private async Task<bool> Save(bool saveAs)
    {
        if (!View.PrepareFileOperation()) return false;
        var destination = !saveAs ? projectPath : null;
        destination ??= await SavePicker(L.Get("files.saveProject"), "catchproj", SafeName(View.Document.Name) + ".catchproj");
        if (destination is null) return false;
        ProjectSerializer.WriteFile(View.Document, destination); projectPath = destination; View.MarkSaved();
        View.SetNotice(L.Get("files.saved", destination)); return true;
    }
    private async Task Export()
    {
        if (!View.PrepareFileOperation()) return;
        var destination = await SavePicker(L.Get("files.export"), "osu", SafeName(View.Document.Name) + ".osu");
        if (destination is null) return;
        var result = OsuBeatmapWriter.Serialize(View.Document, View.CompensateTinyDroplets);
        BeatmapResources.Copy(View.Document, Path.GetDirectoryName(destination)!, result.ReadBack);
        OsuBeatmapWriter.WriteFile(View.Document, destination, View.CompensateTinyDroplets);
        View.SetNotice(result.ObjectSequenceMatches ? L.Get("files.exportMatched", result.MaxConvertedXError, result.MaxConvertedTimeErrorMs) : L.Get("files.exportChanged"));
        if (result.Diagnostics.Count > 0) await Message(L.Get("files.diagnostics"), string.Join("\n", result.Diagnostics.Take(8)));
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
        L.SetLanguage("en");
        editor.Refresh();
        using var english = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
        english.Render(editor); english.Save(Path.Combine(folder, "editor-en.png"));
        L.SetLanguage("zh-CN");
        await Task.CompletedTask;
    }
}
