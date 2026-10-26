using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

internal sealed class LibraryBrowser
{
    internal const int PageSize = 64, PageLimit = 8;
    private readonly LibrarySearchSnapshot? snapshot;
    private readonly LibraryDatabase? database;
    private long catalogRevision;
    private readonly string query = "";
    private readonly bool projects;
    private readonly HashSet<string>? filter;
    private readonly Dictionary<string, long> visits = [];
    private readonly HashSet<string>? difficultyPaths;
    private readonly HashSet<string>? creators;
    private readonly int[] headers;
    private bool retiringForReorder, transferred, retired;
    private Task<LibraryBrowser>? reorderTask;
    private readonly object gate = new();
    private readonly Dictionary<int, IReadOnlyList<LibrarySetRow>> pages = [];
    private readonly Queue<int> pageOrder = [];
    private Task<(int Start, IReadOnlyList<LibrarySetRow> Rows)>? pageTask;
    private Task<(string Key, int Start, IReadOnlyList<LibraryMap> Rows)>? detailTask;
    private Task<(long Revision, List<(int Start, IReadOnlyList<LibrarySetRow> Rows)> Pages)>? discoveryTask;
    private long discoveryRevision;
    private string? detailKey;
    private int detailStart;
    private IReadOnlyList<LibraryMap> details = [];
    public int Count { get; }
    public int SetCount { get; }
    public int TopIndex { get; private set; }
    public LibrarySetRow? Selected { get; private set; }
    public bool Loading => pageTask is not null || detailTask is not null || discoveryTask is not null;
    public int CachedRows => pages.Values.Sum(page => page.Count);
    public string? Error { get; private set; }

    internal sealed record SavedPage(int Start, LibrarySetRow[] Rows);
    internal sealed record SavedView(int Count, int SetCount, int[] Headers, SavedPage[] Pages,
        LibrarySetRow? Selected, string? DetailKey, int DetailStart, LibraryMap[] Details);
    public long ContentRevision { get; private set; }
    public bool IsPreview => snapshot is null;
    private LibraryBrowser(SavedView saved)
    {
        Count = saved.Count; SetCount = saved.SetCount; headers = saved.Headers;
        foreach (var page in saved.Pages) AddPage(page.Start, page.Rows);
        Selected = saved.Selected; detailKey = saved.DetailKey; detailStart = saved.DetailStart; details = saved.Details;
        TopIndex = -1;
    }
    public static LibraryBrowser Restore(SavedView saved)
    {
        if (saved is null || saved.Headers is null || saved.Pages is null || saved.Details is null
            || saved.Count < 0 || saved.SetCount < 0 || saved.Pages.Length > PageLimit || saved.Details.Length > 128
            || saved.Headers.Any(i => i < 0 || i >= saved.Count)
            || !saved.Headers.SequenceEqual(saved.Headers.Distinct().Order())
            || saved.Pages.Any(p => p is null || p.Rows is null || p.Start < 0 || p.Start % PageSize != 0 || p.Rows.Length > PageSize
                || p.Rows.Where((r, i) => r is null || r.Index != p.Start + i || r.Index >= saved.Count || r.Map is null).Any()))
            throw new System.IO.InvalidDataException("Invalid Library preview.");
        return new(saved);
    }
    public SavedView? SaveView() => snapshot is null || retired || retiringForReorder ? null
        : new(Count, SetCount, headers, pages.Select(p => new SavedPage(p.Key, p.Value.ToArray())).ToArray(),
            Selected, detailKey, detailStart, details.ToArray());

    private LibraryBrowser(LibrarySearchSnapshot snapshot, LibraryDatabase database, string query, bool projects,
        IReadOnlyCollection<string>? filter, LibraryOrganization? organization)
    {
        this.snapshot = snapshot; this.database = database; this.query = query; this.projects = projects;
        catalogRevision = database.Revision; Count = snapshot.Count; SetCount = snapshot.SetCount;
        headers = snapshot.HeaderIndices.ToArray();
        this.filter = filter is null ? null : new(filter, WorkspaceSynchronization.Paths);
        difficultyPaths = organization?.DifficultyPaths is { } paths ? new(paths, WorkspaceSynchronization.Paths) : null;
        creators = organization?.Creators is { } names ? new(names, StringComparer.Ordinal) : null;
        visits = new(organization?.Visits ?? new Dictionary<string, long>(), WorkspaceSynchronization.Paths);
    }
    public static LibraryBrowser Create(LibraryDatabase db, string query, bool projects, string? selected, string? top, float offset,
        IReadOnlyCollection<string>? favourites = null, LibraryOrganization? organization = null)
    {
        var snapshot = db.SearchSnapshot(query, projects, favourites, organization);
        try { return Prepare(snapshot, db, query, projects, selected, top, offset, favourites, organization); }
        catch { snapshot.Dispose(); throw; }
    }
    private static LibraryBrowser Prepare(LibrarySearchSnapshot snapshot, LibraryDatabase db, string query, bool projects,
        string? selected, string? top, float offset, IReadOnlyCollection<string>? filter, LibraryOrganization? organization)
    {
        int topIndex = snapshot.FindIndex(top), selectedIndex = snapshot.FindIndex(selected);
        var result = new LibraryBrowser(snapshot, db, query, projects, filter, organization) { TopIndex = topIndex };
        int first = topIndex >= 0 ? topIndex : result.IndexAtOffset(offset);
        result.AddPage(first / PageSize * PageSize, snapshot.Page(first / PageSize * PageSize));
        if (selectedIndex < 0) selectedIndex = snapshot.FirstMapIndex;
        if (selectedIndex >= 0 && result.Get(selectedIndex) is null)
            result.AddPage(selectedIndex / PageSize * PageSize, snapshot.Page(selectedIndex / PageSize * PageSize));
        result.Selected = selectedIndex >= 0 ? result.Get(selectedIndex) : snapshot.FindSelection(selected);
        return result;
    }
    public bool CanReorder(LibraryDatabase db, string query, bool projects, IReadOnlyCollection<string>? filter, LibraryOrganization organization)
        => snapshot is not null && !retired && !retiringForReorder && ReferenceEquals(database, db) && catalogRevision == db.Revision && this.query == query && this.projects == projects
            && (this.filter is null ? filter is null : filter is not null && this.filter.SetEquals(filter))
            && (difficultyPaths is null ? organization.DifficultyPaths is null : organization.DifficultyPaths is not null && difficultyPaths.SetEquals(organization.DifficultyPaths))
            && (creators is null ? organization.Creators is null : organization.Creators is not null && creators.SetEquals(organization.Creators))
            && visits.Count == (organization.Visits?.Count ?? 0) && visits.All(p => organization.Visits?.GetValueOrDefault(p.Key) == p.Value);
    public Task<LibraryBrowser> Reorder(LibraryOrganization organization, string? selected, string? top, float offset)
    {
        if (snapshot is null || database is null) throw new InvalidOperationException();
        retiringForReorder = true;
        var pending = new Task?[] { pageTask, detailTask, discoveryTask }.OfType<Task>().ToArray();
        return reorderTask = Task.Run(async () =>
        {
            try { await Task.WhenAll(pending); }
            catch { /* A failed page does not prevent rebuilding the result ordering. */ }
            lock (gate)
            {
                snapshot.Reorganize(organization);
                var next = Prepare(snapshot, database, query, projects, selected, top, offset, filter, organization);
                next.catalogRevision = catalogRevision;
                transferred = true;
                return next;
            }
        });
    }
    public float Extent => OffsetOf(Count);
    public float OffsetOf(int index)
    {
        int lo = 0, hi = headers.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (headers[mid] < index) lo = mid + 1; else hi = mid; }
        return index - lo * (1 - 30f / 86);
    }
    public int IndexAtOffset(float offset)
    {
        int lo = 0, hi = Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (OffsetOf(mid + 1) <= offset) lo = mid + 1; else hi = mid; }
        return Math.Clamp(lo, 0, Math.Max(0, Count - 1));
    }
    public LibrarySetRow? Get(int index)
    {
        if (!pages.TryGetValue(index / PageSize * PageSize, out var rows)) return null;
        int within = index % PageSize;
        return within >= 0 && within < rows.Count ? rows[within] : null;
    }
    private void AddPage(int start, IReadOnlyList<LibrarySetRow> rows)
    {
        if (pages.ContainsKey(start)) return;
        while (pages.Count >= PageLimit && pageOrder.TryDequeue(out int old)) pages.Remove(old);
        pages[start] = rows; pageOrder.Enqueue(start); ContentRevision++;
    }
    public void Select(LibrarySetRow row) { if (row.Header is null) { Selected = row; ContentRevision++; } }
    public void SelectMap(LibraryMap map)
    {
        var row = pages.Values.SelectMany(page => page).FirstOrDefault(row => row.Header is null && row.Map.Path == map.Path);
        if (row is not null) { Selected = row; ContentRevision++; }
    }
    public void Pump()
    {
        if (snapshot is null || retired || retiringForReorder) return;
        if (pageTask is { IsCompleted: true })
        {
            try { var page = pageTask.GetAwaiter().GetResult(); AddPage(page.Start, page.Rows); }
            catch (Exception e) { Error = e.Message; }
            pageTask = null;
        }
        if (detailTask is { IsCompleted: true })
        {
            try { var page = detailTask.GetAwaiter().GetResult(); detailKey = page.Key; detailStart = page.Start; details = page.Rows; ContentRevision++; }
            catch (Exception e) { Error = e.Message; }
            detailTask = null;
        }
        if (discoveryTask is { IsCompleted: true })
        {
            try
            {
                var update = discoveryTask.GetAwaiter().GetResult(); discoveryRevision = update.Revision;
                foreach (var page in update.Pages) pages[page.Start] = page.Rows;
                if (Selected is { } selected) Selected = Get(selected.Index) ?? selected;
                detailKey = null; details = []; ContentRevision++;
            }
            catch (Exception e) { Error = e.Message; discoveryRevision = snapshot.DiscoveryRevision; }
            discoveryTask = null;
        }
        if (discoveryTask is null && pageTask is null && detailTask is null && snapshot.DiscoveryRevision != discoveryRevision)
        {
            long revision = snapshot.DiscoveryRevision;
            int[] starts = pages.Keys.ToArray();
            discoveryTask = Task.Run(() =>
            {
                lock (gate) return (revision, starts.Select(start => (start, snapshot.Page(start))).ToList());
            });
        }
    }
    public void RequestVisible(int first, int last)
    {
        if (snapshot is null || retired || retiringForReorder || pageTask is not null || Count == 0) return;
        int start = Math.Clamp(first, 0, Count - 1) / PageSize * PageSize;
        int end = Math.Min(Count - 1, last) / PageSize * PageSize;
        for (int page = start; page <= end; page += PageSize)
        {
            if (pages.ContainsKey(page)) continue;
            int requested = page;
            pageTask = Task.Run(() => { lock (gate) return (requested, snapshot.Page(requested)); });
            return;
        }
    }
    public LibraryMap? Detail(int index)
        => Selected?.Key == detailKey && index >= detailStart && index < detailStart + details.Count ? details[index - detailStart] : null;
    public void RequestDetails(int first)
    {
        if (snapshot is null || retired || retiringForReorder || Selected is not { } set || detailTask is not null) return;
        int start = Math.Max(0, first) / PageSize * PageSize;
        if (detailKey == set.Key && detailStart == start) return;
        detailTask = Task.Run(() => { lock (gate) return (set.Key, start, snapshot.Difficulties(set, start, 128)); });
    }
    public void Retire()
    {
        if (retired) return;
        retired = true;
        if (transferred || snapshot is null) return;
        if (reorderTask is null) snapshot.CancelReferenceDiscovery();
        var pending = new Task?[] { pageTask, detailTask, discoveryTask, reorderTask }.OfType<Task>().ToArray();
        _ = Task.Run(async () =>
        {
            try { await Task.WhenAll(pending); }
            catch { /* Disposal must also release snapshots after a failed page read. */ }
            finally { lock (gate) if (!transferred) snapshot.Dispose(); }
        });
    }
}
