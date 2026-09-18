using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

internal sealed class LibraryBrowser
{
    internal const int PageSize = 64, PageLimit = 8;
    private readonly LibrarySearchSnapshot snapshot;
    private readonly object gate = new();
    private readonly Dictionary<int, IReadOnlyList<LibrarySetRow>> pages = [];
    private readonly Queue<int> pageOrder = [];
    private Task<(int Start, IReadOnlyList<LibrarySetRow> Rows)>? pageTask;
    private Task<(string Key, int Start, IReadOnlyList<LibraryMap> Rows)>? detailTask;
    private string? detailKey;
    private int detailStart;
    private IReadOnlyList<LibraryMap> details = [];
    public int Count => snapshot.Count;
    public int TopIndex { get; private init; }
    public LibrarySetRow? Selected { get; private set; }
    public bool Loading => pageTask is { IsCompleted: false } || detailTask is { IsCompleted: false };
    public int CachedRows => pages.Values.Sum(page => page.Count);
    public string? Error { get; private set; }

    private LibraryBrowser(LibrarySearchSnapshot snapshot) => this.snapshot = snapshot;
    public static LibraryBrowser Create(LibraryDatabase db, string query, bool projects, string? selected, string? top, int offset)
    {
        var snapshot = db.SearchSnapshot(query, projects);
        try
        {
            int topIndex = snapshot.FindIndex(top), selectedIndex = snapshot.FindIndex(selected);
            var result = new LibraryBrowser(snapshot) { TopIndex = topIndex };
            int first = Math.Clamp(topIndex >= 0 ? topIndex : offset, 0, Math.Max(0, snapshot.Count - 1));
            result.AddPage(first / PageSize * PageSize, snapshot.Page(first / PageSize * PageSize));
            if (selectedIndex < 0) selectedIndex = 0;
            if (result.Get(selectedIndex) is null) result.AddPage(selectedIndex / PageSize * PageSize, snapshot.Page(selectedIndex / PageSize * PageSize));
            result.Selected = result.Get(selectedIndex);
            return result;
        }
        catch { snapshot.Dispose(); throw; }
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
        pages[start] = rows; pageOrder.Enqueue(start);
    }
    public void Select(LibrarySetRow row) => Selected = row;
    public void SelectMap(LibraryMap map)
    {
        var row = pages.Values.SelectMany(page => page).FirstOrDefault(row => row.Map.Path == map.Path);
        if (row is not null) Selected = row;
    }
    public void Pump()
    {
        if (pageTask is { IsCompleted: true })
        {
            try { var page = pageTask.GetAwaiter().GetResult(); AddPage(page.Start, page.Rows); }
            catch (Exception e) { Error = e.Message; }
            pageTask = null;
        }
        if (detailTask is { IsCompleted: true })
        {
            try { var page = detailTask.GetAwaiter().GetResult(); detailKey = page.Key; detailStart = page.Start; details = page.Rows; }
            catch (Exception e) { Error = e.Message; }
            detailTask = null;
        }
    }
    public void RequestVisible(int first, int last)
    {
        if (pageTask is not null || Count == 0) return;
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
        if (Selected is not { } set || detailTask is not null) return;
        int start = Math.Max(0, first) / PageSize * PageSize;
        if (detailKey == set.Key && detailStart == start) return;
        detailTask = Task.Run(() => { lock (gate) return (set.Key, start, snapshot.Difficulties(set, start, 128)); });
    }
    public void Retire()
    {
        var pending = new Task?[] { pageTask, detailTask }.OfType<Task>().ToArray();
        _ = Task.Run(async () =>
        {
            try { await Task.WhenAll(pending); }
            catch { /* Disposal must also release snapshots after a failed page read. */ }
            finally { lock (gate) snapshot.Dispose(); }
        });
    }
}
