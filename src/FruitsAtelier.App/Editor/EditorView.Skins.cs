using FruitsAtelier.App.Platform;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public Action? RequestSkinPreference { get; set; }
    public Rect SkinSelectorBounds => new(HeaderLanguageBounds.X - 206, 6, 200, 28);
    public Action? RequestDefaultSkinArchive { get; set; }
    public void SetDefaultSkinArchive(string archive) => draftDefaultSkin = archive;
    private CatchSkin? defaultSkin;
    private bool ImportedSkin => skin is not null && FruitsAtelier.Core.WorkspaceProject.Within(
        Path.Combine(LibrarySettings.Workspace, "Skins", "Imported"), skin.FolderPath);

    public void InitializeSkin()
    {
        defaultSkin = null;
        string? configured = LibrarySettings.DefaultSkin;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                string folder = SkinArchive.Import(configured, Path.Combine(LibrarySettings.Workspace, "Skins", "Imported"));
                CatchSkin.TryLoad(folder, out defaultSkin, out _);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { SetNotice(L.Get("window.defaultSkinFailed", L.Localized(error.Message))); }
        }
        skin = defaultSkin;
        if (LibrarySettings.SelectedSkin is { } selected && Directory.Exists(selected))
            LoadSkin(selected);
        else RefreshSkinHitsounds();
    }

    public void ImportSkin(string archive)
        => SelectSkin(StoreSkinArchive(LibrarySettings.Workspace, archive).Folder);

    private static (string Folder, string Archive) StoreSkinArchive(string workspace, string archive)
    {
        string root = Path.Combine(workspace, "Skins");
        string folder = SkinArchive.Import(archive, Path.Combine(root, "Imported"));
        string archives = Path.Combine(root, "Archives");
        FruitsAtelier.Core.WorkspaceProject.RejectLinks(archives);
        Directory.CreateDirectory(archives);
        string destination = Path.Combine(archives, Path.GetFileName(folder) + ".osk");
        FruitsAtelier.Core.WorkspaceProject.RejectLinks(destination);
        if (!File.Exists(destination)) File.Copy(archive, destination);
        return (folder, destination);
    }

    private void SelectSkin(string? folder)
    {
        if (folder is not null) folder = UpgradeSkinFolder(folder);
        if (folder is null) skin = defaultSkin;
        else if (CatchSkin.TryLoad(folder, out var loaded, out string message, defaultSkin, allowEmpty: true)) skin = loaded;
        else { ShowError(message); return; }
        LibrarySettings.SelectedSkin = folder;
        RefreshSkinHitsounds();
        RequestSkinPreference?.Invoke();
    }

    private string UpgradeSkinFolder(string folder)
    {
        string root = Path.Combine(LibrarySettings.Workspace, "Skins");
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (!name.StartsWith("v4-", StringComparison.Ordinal) && FruitsAtelier.Core.WorkspaceProject.Within(Path.Combine(root, "Imported"), folder))
        {
            string archive = Path.Combine(root, "Archives", name + ".osk");
            if (File.Exists(archive)) return StoreSkinArchive(LibrarySettings.Workspace, archive).Folder;
        }
        return folder;
    }

    private void DrawSkinSelector(ICanvas c)
    {
        var bounds = SkinSelectorBounds;
        Button(c, bounds, "", () => OpenSkinMenu());
        c.Text(L.Get("skin.selector", SkinName ?? L.Get("skin.default")) + " ▾", bounds.X + 8, bounds.Y + 7,
            12, ImportedSkin ? Gold : Foreground, bounds.Width - 16);
    }

    private void OpenSkinMenu(int page = 0)
    {
        try
        {
            if (editField >= 0 && !CommitField()) return;
            languageMenuOpen = false; menu = -1; contextItems.Clear();
            var entries = new List<(string Folder, string Name, bool Imported)>();
            void Add(string root, bool imported)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
                foreach (string folder in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (imported && !File.Exists(Path.Combine(folder, ".complete"))) continue;
                    string name = Path.GetFileName(folder);
                    string ini = Path.Combine(folder, "skin.ini");
                    if (File.Exists(ini))
                    {
                        var line = File.ReadLines(ini).Take(500).FirstOrDefault(l => l.TrimStart().StartsWith("Name:", StringComparison.OrdinalIgnoreCase));
                        if (line is not null) name = line[(line.IndexOf(':') + 1)..].Trim();
                    }
                    entries.Add((folder, name, imported));
                }
            }
            Add(LibrarySettings.Skins, false);
            Add(Path.Combine(LibrarySettings.Workspace, "Skins", "Imported"), true);
            int size = Math.Max(1, (int)((height - 60) / 32) - 4);
            int lastPage = Math.Max(0, (entries.Count - 1) / size);
            page = Math.Clamp(page, 0, lastPage);
            contextItems.Add(new(L.Get("skin.default"), () => SelectSkin(null)));
            contextItems.Add(new(L.Get("skin.import"), () => RequestLoadSkin?.Invoke(), Color: Gold));
            foreach (var entry in entries.Skip(page * size).Take(size))
            {
                string label = (skin?.FolderPath == entry.Folder ? "✓ " : "") + entry.Name
                    + (entry.Imported ? " · " + L.Get("skin.imported") : "");
                contextItems.Add(new(label, () => SelectSkin(entry.Folder), Color: entry.Imported ? Gold : Foreground));
            }
            if (page > 0) contextItems.Add(new(L.Get("skin.previousPage"), () => OpenSkinMenu(page - 1)));
            if (page < lastPage) contextItems.Add(new(L.Get("skin.nextPage"), () => OpenSkinMenu(page + 1)));
            contextBounds = new(Math.Max(0, Math.Min(SkinSelectorBounds.X, width - 310)), 38, 310, 12 + contextItems.Count * 32);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { contextItems.Clear(); ShowError(error.Message); }
    }
}
