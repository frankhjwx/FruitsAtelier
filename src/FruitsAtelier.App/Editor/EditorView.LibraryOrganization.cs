using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed class LibraryCollection
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public HashSet<string> Keys { get; set; } = [];
    }

    private readonly List<(Rect Bounds, string Key)> libraryHeaders = [];
    private readonly List<(Rect Bounds, string Id)> collectionRows = [];
    private Rect collectionsBounds, collectionDialogBounds;
    private float collectionsScroll, collectionDialogScroll;
    private bool collectionDialogOpen;
    private string collectionDialogMode = "", collectionDialogText = "", collectionDialogError = "";
    private string? collectionRenameId;
    private LibraryMap? collectionDialogMap;
    private HashSet<string> collectionChoices = [];
    private string runningOrganizationKey = "";

    private void InvalidateLibraryOrganization(bool resetScroll = false)
    {
        foreach (var cached in libraryCategoryBrowsers.Values) cached.Browser?.Retire();
        libraryCategoryBrowsers.Clear();
        libraryFavouriteRevision++;
        if (resetScroll) { libraryScroll = 0; libraryResultsReady = false; }
        QueueLibrarySearch(); searchAfter = DateTime.UtcNow; RememberLibraryPosition(); SaveLibraryMemory();
    }

    private void ToggleLibraryGroup(string key)
    {
        if (!libraryExpanded.Add(key)) libraryExpanded.Remove(key);
        InvalidateLibraryOrganization();
    }

    private void RecordLibraryVisit(LibraryMap map, string project)
    {
        long now = DateTime.UtcNow.Ticks;
        libraryMemory.Visits[map.Directory] = now;
        libraryMemory.Visits[project] = now;
        foreach (var collection in libraryMemory.Collections)
            if (collection.Keys.Remove(map.Directory)) collection.Keys.Add(project);
        InvalidateLibraryOrganization();
    }

    private IReadOnlyCollection<string>? LibraryFilterKeys => !IsOsuCollection && libraryCollection is { } id
        ? libraryMemory.Collections.FirstOrDefault(c => c.Id == id)?.Keys.ToArray() ?? []
        : libraryRecentOnly ? libraryMemory.Visits.Keys.ToArray()
        : libraryFavouritesOnly ? libraryMemory.FavouriteKeys.ToArray() : null;

    private LibraryOrganization CurrentLibraryOrganization => new(libraryGrouping, librarySort, libraryDescending,
        new Dictionary<string, long>(libraryMemory.Visits, WorkspaceSynchronization.Paths), null, DateTime.Today, libraryExpanded.ToArray(), CollapseByDefault: true,
        DifficultyPaths: IsOsuCollection ? osuCollections?.Collections.FirstOrDefault(c => c.Id == libraryCollection)?.Paths ?? [] : null);

    private string LibraryGroupLabel(string key) => libraryGrouping switch
    {
        LibraryGrouping.DateAdded or LibraryGrouping.RecentVisited => L.Get("library.period." + key),
        LibraryGrouping.Mode => L.Get("library.mode." + key),
        _ => key == "unknown" ? L.Get("library.unknown") : key
    };

    private void DrawLibraryOrganization(ICanvas c, float listWidth, bool loading)
    {
        float toolbarY = 134;
        bool compact = listWidth < 630;
        c.Text(loading ? L.Get("library.scanning") : L.Get("library.results", libraryBrowser?.SetCount ?? 0), 214, 140, 12, Muted, compact ? listWidth : listWidth - 420);
        if (compact) toolbarY = 166;
        float x = compact ? 214 : 214 + listWidth - 410;
        LibraryChoice(c, new(x, toolbarY, 190, 28), L.Get("library.groupBy", L.Get("library.group." + libraryGrouping)), () =>
            ShowLibraryOrderMenu(new(x, toolbarY, 190, 28), true));
        LibraryChoice(c, new(x + 198, toolbarY, 174, 28), L.Get("library.sortBy", L.Get("library.order." + librarySort)), () =>
            ShowLibraryOrderMenu(new(x + 198, toolbarY, 174, 28), false));
        c.Fill(new(x + 378, toolbarY, 32, 28), Surface, 4);
        c.Stroke(new(x + 378, toolbarY, 32, 28), Grid, radius: 4);
        Button(c, new(x + 378, toolbarY, 32, 28), libraryDescending ? "↓" : "↑", () =>
        { libraryDescending = !libraryDescending; InvalidateLibraryOrganization(true); });
        libraryListBounds = new(214, compact ? 202 : 170, listWidth, Math.Max(86, height - (compact ? 244 : 212)));
    }

    private void LibraryChoice(ICanvas c, Rect bounds, string label, Action action)
    {
        c.Fill(bounds, Surface, 4); c.Stroke(bounds, Grid, radius: 4);
        Button(c, bounds, label, action, textRightPadding: 24);
        c.Text("▾", bounds.Right - 18, bounds.Y + 7, 12, Muted, 14);
    }

    private void ShowLibraryOrderMenu(Rect bounds, bool group)
    {
        libraryField = -1; contextItems.Clear();
        if (group)
            foreach (var value in Enum.GetValues<LibraryGrouping>())
                contextItems.Add(new(L.Get("library.group." + value), () =>
                { libraryGrouping = value; libraryExpanded.Clear(); InvalidateLibraryOrganization(true); }, Active: value == libraryGrouping));
        else
            foreach (var value in Enum.GetValues<LibraryOrder>())
                contextItems.Add(new(L.Get("library.order." + value), () =>
                { librarySort = value; InvalidateLibraryOrganization(true); }, Active: value == librarySort));
        contextBounds = new(Math.Min(bounds.X, width - 240), Math.Min(bounds.Bottom, height - ContextMenuHeight), 240, ContextMenuHeight);
    }

    private void DrawLibraryCollections(ICanvas c)
    {
        Button(c, new(16, 226, 158, 36), L.Get("library.recentVisited"), () => SwitchLibraryCategory(false, recent: true), libraryRecentOnly);
        c.Line(24, 272, 166, 272, Grid, 1);
        collectionsBounds = new(16, 282, 158, Math.Max(0, height - 437));
        collectionRows.Clear();
        var imported = osuCollections is { Available: true } ? osuCollections.Collections : [];
        float faHeight = SectionHeight(libraryMemory.Collections.Count);
        float osuTop = faHeight + 24;
        float total = imported.Count > 0 ? osuTop + SectionHeight(imported.Count) : faHeight;
        collectionsScroll = Math.Clamp(collectionsScroll, 0, Math.Max(0, total - collectionsBounds.Height));
        c.Clip(collectionsBounds);
        DrawSection(L.Get("library.collections"), 0, faHeight, addButton: true);
        int first = Math.Max(0, (int)Math.Floor((collectionsScroll - 40) / 36));
        int last = Math.Min(libraryMemory.Collections.Count, (int)Math.Ceiling((collectionsScroll + collectionsBounds.Height - 40) / 36));
        for (int i = first; i < last; i++)
        {
            var collection = libraryMemory.Collections[i];
            DrawRow(collection.Id, collection.Name, collection.Keys.Count, 40 + i * 36);
        }
        if (imported.Count > 0)
        {
            float y = collectionsBounds.Y + osuTop - collectionsScroll;
            c.Line(24, y - 12, 166, y - 12, Grid, 1);
            DrawSection(L.Get("library.osuCollections"), osuTop, SectionHeight(imported.Count));
            first = Math.Max(0, (int)Math.Floor((collectionsScroll - osuTop - 40) / 36));
            last = Math.Min(imported.Count, (int)Math.Ceiling((collectionsScroll + collectionsBounds.Height - osuTop - 40) / 36));
            for (int i = first; i < last; i++)
                DrawRow(imported[i].Id, imported[i].Name, imported[i].Hashes.Count, osuTop + 40 + i * 36);
        }
        c.Unclip();
        if (total > collectionsBounds.Height && collectionsBounds.Height > 0)
        {
            float thumb = Math.Max(20, collectionsBounds.Height * collectionsBounds.Height / total);
            float y = collectionsBounds.Y + collectionsScroll / (total - collectionsBounds.Height) * (collectionsBounds.Height - thumb);
            c.Fill(new(179, y, 3, thumb), Muted, 2);
        }
        static float SectionHeight(int count) => count == 0 ? 32 : 48 + count * 36;
        void DrawSection(string title, float offset, float sectionHeight, bool addButton = false)
        {
            var section = new Rect(16, collectionsBounds.Y + offset - collectionsScroll, 158, sectionHeight);
            c.Fill(section, Background, 5, .35f); c.Stroke(section, Grid, radius: 5);
            c.Fill(new(section.X + 1, section.Y + 1, section.Width - 2, 30), Surface, 4, .55f);
            c.Text(title, section.X + 8, section.Y + 8, 12, Muted, addButton ? 112 : 142, true);
            if (sectionHeight > 32) c.Line(section.X + 1, section.Y + 32, section.Right - 1, section.Y + 32, Grid, 1);
            if (!addButton) return;
            var add = new Rect(section.Right - 32, section.Y + 2, 30, 28);
            c.Fill(add, add.Contains(mouseX, mouseY) ? 0x3D495Au : Surface, 4);
            c.Stroke(add, add.Contains(mouseX, mouseY) ? 0x71849Au : Grid, radius: 4);
            float cx = add.X + add.Width / 2, cy = add.Y + add.Height / 2;
            c.Line(cx - 3, cy, cx + 3, cy, Foreground, 1.2f);
            c.Line(cx, cy - 3, cx, cy + 3, Foreground, 1.2f);
            var clipped = new Rect(add.X, Math.Max(add.Y, collectionsBounds.Y), add.Width,
                Math.Max(0, Math.Min(add.Bottom, collectionsBounds.Bottom) - Math.Max(add.Y, collectionsBounds.Y)));
            if (clipped.Height > 0) hits.Add(new(clipped, () => ShowCollectionNameDialog(), true));
        }
        void DrawRow(string id, string name, int count, float offset)
        {
            var bounds = new Rect(22, collectionsBounds.Y + offset - collectionsScroll, 146, 32);
            if (bounds.Bottom <= collectionsBounds.Y || bounds.Y >= collectionsBounds.Bottom) return;
            var clipped = new Rect(bounds.X, Math.Max(bounds.Y, collectionsBounds.Y), bounds.Width,
                Math.Max(0, Math.Min(bounds.Bottom, collectionsBounds.Bottom) - Math.Max(bounds.Y, collectionsBounds.Y)));
            collectionRows.Add((clipped, id));
            bool selected = libraryCollection == id;
            c.Fill(bounds, selected ? 0x31494Bu : Surface, 4, selected ? 1 : .4f);
            c.Stroke(bounds, selected ? 0x477E7Bu : Grid, radius: 4);
            c.Text(name, bounds.X + 10, bounds.Y + 8, 12, selected ? Accent : Foreground, bounds.Width - 50, selected);
            c.Text(count.ToString(System.Globalization.CultureInfo.CurrentCulture), bounds.Right - 28, bounds.Y + 8, 11, Muted, 22);
            hits.Add(new(clipped, () => SwitchLibraryCategory(false, collection: id), true));
        }
    }

    private bool ShowCollectionContext(float x, float y)
    {
        var row = collectionRows.FirstOrDefault(r => r.Bounds.Contains(x, y));
        if (row.Id is null) return false;
        var collection = libraryMemory.Collections.FirstOrDefault(c => c.Id == row.Id);
        if (collection is null) { contextItems.Clear(); return true; }
        contextItems.Clear(); libraryField = -1;
        contextItems.Add(new(L.Get("library.renameCollection"), () => ShowCollectionNameDialog(collection.Id)));
        contextItems.Add(new(L.Get("library.deleteCollection"), () =>
        {
            if (libraryCollection == collection.Id) SwitchLibraryCategory(false);
            libraryMemory.Collections.Remove(collection);
            libraryMemory.Positions.Remove(collection.Id);
            InvalidateLibraryOrganization();
        }));
        contextBounds = new(Math.Clamp(x, 0, width - 250), Math.Clamp(y, 64, Math.Max(64, height - ContextMenuHeight)), 250, ContextMenuHeight);
        return true;
    }

    private static bool CollectionContains(LibraryCollection collection, LibraryMap map)
        => collection.Keys.Contains(map.Directory) || map.ProjectPath is { } project && collection.Keys.Contains(project);

    private void SetCollectionMember(string id, LibraryMap map, bool member, bool refresh = true)
    {
        var collection = libraryMemory.Collections.FirstOrDefault(c => c.Id == id);
        if (collection is null) return;
        if (member) { if (!CollectionContains(collection, map)) collection.Keys.Add(map.ProjectPath ?? map.Directory); }
        else { collection.Keys.Remove(map.Directory); if (map.ProjectPath is { } project) collection.Keys.Remove(project); }
        if (refresh) InvalidateLibraryOrganization();
    }

    private void ShowCollectionDialog(LibraryMap map)
    {
        collectionDialogMap = map; collectionDialogOpen = true; collectionDialogMode = "members";
        collectionChoices = libraryMemory.Collections.Where(c => CollectionContains(c, map)).Select(c => c.Id).ToHashSet();
        collectionDialogText = collectionDialogError = ""; collectionDialogScroll = 0; libraryField = 6;
        SelectInput("library:6", collectionDialogText);
    }

    private void ShowCollectionNameDialog(string? id = null, bool fromMembers = false)
    {
        if (!fromMembers) collectionDialogMap = null;
        collectionRenameId = id; collectionDialogOpen = true; collectionDialogMode = id is null ? "create" : "rename";
        collectionDialogText = libraryMemory.Collections.FirstOrDefault(c => c.Id == id)?.Name ?? "";
        collectionDialogError = ""; libraryField = 6; SelectInput("library:6", collectionDialogText);
    }

    private void CloseCollectionDialog()
    { collectionDialogOpen = false; collectionDialogMap = null; libraryField = -1; }

    private void CommitCollectionDialog()
    {
        if (collectionDialogMode == "members")
        {
            if (collectionDialogMap is { } map)
                foreach (var collection in libraryMemory.Collections) SetCollectionMember(collection.Id, map, collectionChoices.Contains(collection.Id), refresh: false);
            InvalidateLibraryOrganization();
            CloseCollectionDialog(); return;
        }
        string name = collectionDialogText.Trim();
        if (name.Length == 0 || name.Length > 100)
        { collectionDialogError = L.Get("library.collectionNameInvalid"); return; }
        if (libraryMemory.Collections.Any(c => c.Id != collectionRenameId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        { collectionDialogError = L.Get("library.collectionNameExists"); return; }
        if (collectionRenameId is { } id)
        { var collection = libraryMemory.Collections.FirstOrDefault(c => c.Id == id); if (collection is not null) collection.Name = name; }
        else
        {
            var collection = new LibraryCollection { Name = name, Keys = new(WorkspaceSynchronization.Paths) };
            libraryMemory.Collections.Add(collection); collectionChoices.Add(collection.Id);
        }
        InvalidateLibraryOrganization();
        if (collectionDialogMap is not null)
        { collectionDialogMode = "members"; collectionDialogText = ""; collectionDialogScroll = 0; libraryField = 6; }
        else CloseCollectionDialog();
    }

    private bool CollectionPointerDown(float x, float y, int button)
        => button != 0 || !collectionDialogBounds.Contains(x, y);

    private void DrawLibraryCollectionDialog(ICanvas c)
    {
        if (!collectionDialogOpen) return;
        hits.Clear();
        c.Fill(new(0, HeaderHeight, width, height - HeaderHeight), 0x10141B, opacity: .9f);
        bool members = collectionDialogMode == "members";
        float w = Math.Min(460, width - 48), h = members ? Math.Min(500, height - 100) : 230;
        float x = (width - w) / 2, y = (height - h) / 2;
        collectionDialogBounds = new(x, y, w, h);
        c.Fill(collectionDialogBounds, Panel, 7); c.Stroke(collectionDialogBounds, Grid, radius: 7);
        c.Text(L.Get(members ? "library.addCollection" : collectionDialogMode == "rename" ? "library.renameCollection" : "library.newCollection"), x + 24, y + 20, 18, Foreground, w - 48, true);
        var field = new Rect(x + 24, y + 64, w - 48, 36);
        c.Fill(field, Surface, 4); c.Stroke(field, libraryField == 6 ? Accent : Grid, radius: 4);
        DrawInputText(c, new(field.X + 10, field.Y + 9, field.Width - 20, 20), collectionDialogText, 13, libraryField == 6, "library:6");
        if (collectionDialogText.Length == 0 && libraryField != 6)
            c.Text(L.Get(members ? "library.searchCollections" : "library.collectionName"), field.X + 10, field.Y + 9, 13, Muted, field.Width - 20);
        hits.Add(new(field, () => { libraryField = 6; FocusInput("library:6", collectionDialogText, mouseX); }, true));
        if (members)
        {
            var entries = libraryMemory.Collections.Where(c => LibraryDatabase.Normalize(c.Name).Contains(LibraryDatabase.Normalize(collectionDialogText))).ToArray();
            var list = new Rect(x + 24, y + 112, w - 48, h - 186);
            collectionDialogScroll = Math.Clamp(collectionDialogScroll, 0, Math.Max(0, entries.Length * 34 - list.Height));
            c.Clip(list);
            for (int i = (int)(collectionDialogScroll / 34); i < Math.Min(entries.Length, Math.Ceiling((collectionDialogScroll + list.Height) / 34)); i++)
            {
                var collection = entries[i];
                var row = new Rect(list.X, list.Y + i * 34 - collectionDialogScroll, list.Width, 32);
                bool selected = collectionChoices.Contains(collection.Id);
                if (selected || row.Contains(mouseX, mouseY)) c.Fill(row, selected ? 0x31494Bu : Surface, 3);
                c.Text(selected ? "☑" : "☐", row.X + 8, row.Y + 7, 14, selected ? Accent : Muted, 24);
                c.Text(collection.Name, row.X + 36, row.Y + 8, 13, Foreground, row.Width - 44);
                var clipped = new Rect(row.X, Math.Max(row.Y, list.Y), row.Width, Math.Max(0, Math.Min(row.Bottom, list.Bottom) - Math.Max(row.Y, list.Y)));
                hits.Add(new(clipped, () => { if (!collectionChoices.Add(collection.Id)) collectionChoices.Remove(collection.Id); }, true));
            }
            c.Unclip();
            Button(c, new(x + 24, y + h - 54, 180, 32), L.Get("library.newCollection"), () => ShowCollectionNameDialog(fromMembers: true));
        }
        else c.Text(collectionDialogError, x + 24, y + 115, 12, Error, w - 48);
        Button(c, new(x + w - 192, y + h - 54, 76, 32), L.Get("sliderBatch.cancel"), CloseCollectionDialog);
        Button(c, new(x + w - 108, y + h - 54, 84, 32), L.Get(members ? "library.collectionDone" : "library.collectionSave"), CommitCollectionDialog);
    }
}
