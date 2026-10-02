using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private sealed record SyncPath(Guid Source, MapPoint[] Points, bool Controls = false)
    {
        public double Start { get; } = Points.Length == 0 ? 0 : Points.Min(p => p.TimeMs);
        public double End { get; } = Points.Length == 0 ? 0 : Points.Max(p => p.TimeMs);
    }
    private sealed record SyncPane(MapDocument Document, IReadOnlyList<ConvertedCatchObject> Objects, SyncPath[] Paths, Dictionary<Guid, int> Indices, uint[] Colours);
    private sealed record SyncFocus(IReadOnlySet<Guid> Local, IReadOnlySet<Guid> External, double Start, double End);
    private sealed record SyncRange(string Key, double Start, double End, uint Colour, string Label);
    private const uint SyncUnresolved = 0xED737B, SyncPreviouslyResolved = 0xD5A34D, SyncResolvedThisRound = 0x70D69B;
    private sealed record SyncComparison(SyncPane Local, SyncPane External, Dictionary<string, SyncFocus> Focus, DateTime? LocalSaved, DateTime? ExternalSaved,
        IReadOnlySet<Guid> RetainedLocal, IReadOnlySet<Guid> RejectedExternal, string? ExternalAudioHash, SyncReviewData Review);
    private readonly Dictionary<Guid, SyncComparison> syncComparisons = [];
    private WorkspaceMerge? syncVisualMerge;
    private string? syncVisualKey;
    private double syncViewStart, syncViewSpan, syncZoom = 1;
    private bool syncShowValues;
    private Rect syncCanvasBounds;
    private bool syncShowResult;
    private int syncPreviewRevision;
    private SyncPane? syncResultPane;
    private Task<(WorkspaceMerge Merge, int Revision, SyncPane Pane)>? syncPreviewTask;
    private static readonly HashSet<(Guid SourceId, int EventIndex)> noSyncHyper = [];

    private static SyncPane PrepareSyncPane(MapDocument document, bool compensate)
    {
        var conversion = CatchStreamConverter.Convert(document, compensate);
        if (!conversion.Success) throw new InvalidDataException(string.Join("\n", conversion.Diagnostics));
        var paths = new List<SyncPath>();
        foreach (var track in document.Tracks)
        {
            double duration = track.Nodes[^1].TimeMs - track.Nodes[0].TimeMs;
            for (int span = 0; span < track.SpanCount; span++)
            {
                var points = new List<MapPoint>();
                for (int segment = 0; segment < track.Nodes.Count - 1; segment++)
                    for (int step = 0; step <= 32; step++)
                    {
                        var p = CurveMath.Evaluate(track, segment, step / 32.0);
                        points.Add(new(track.Nodes[0].TimeMs + span * duration + (span % 2 == 0 ? p.TimeMs - track.Nodes[0].TimeMs : track.Nodes[^1].TimeMs - p.TimeMs), p.X));
                    }
                paths.Add(new(track.Id, points.ToArray()));
            }
            foreach (var node in track.Nodes)
            {
                paths.Add(new(track.Id, [new(node.TimeMs, node.X)], true));
                if (node.HandleIn != default) paths.Add(new(track.Id, [new(node.TimeMs, node.X), new(node.TimeMs + node.HandleIn.TimeMs, node.X + node.HandleIn.X)], true));
                if (node.HandleOut != default) paths.Add(new(track.Id, [new(node.TimeMs, node.X), new(node.TimeMs + node.HandleOut.TimeMs, node.X + node.HandleOut.X)], true));
            }
        }
        foreach (var slider in conversion.Sliders.Where(s => s.IsImported && s.Path.Count > 1))
        {
            var distances = new double[slider.Path.Count];
            for (int i = 1; i < distances.Length; i++)
                distances[i] = distances[i - 1] + Math.Sqrt(Math.Pow(slider.Path[i].X - slider.Path[i - 1].X, 2) + Math.Pow(slider.Path[i].GeometryY - slider.Path[i - 1].GeometryY, 2));
            if (distances[^1] <= 0) continue;
            for (int span = 0; span < slider.SpanCount; span++)
            {
                double start = slider.StartTimeMs + span * slider.DurationMs / slider.SpanCount;
                paths.Add(new(slider.SourceId, slider.Path.Select((p, i) => new MapPoint(start
                    + (span % 2 == 0 ? distances[i] / distances[^1] : 1 - distances[i] / distances[^1]) * slider.DurationMs / slider.SpanCount, p.X)).ToArray()));
            }
        }
        var indices = conversion.Objects.Select(o => o.SourceId).Distinct().Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        var colours = new List<uint>();
        foreach (var line in document.OriginalSections.Where(s => s.Name == "Colours").SelectMany(s => s.Lines))
        {
            var pair = line.Split(':', 2);
            if (pair.Length != 2 || !pair[0].Trim().StartsWith("Combo", StringComparison.Ordinal)) continue;
            var rgb = pair[1].Split(',');
            if (rgb.Length == 3 && byte.TryParse(rgb[0].Trim(), out byte r) && byte.TryParse(rgb[1].Trim(), out byte g) && byte.TryParse(rgb[2].Trim(), out byte b))
                colours.Add((uint)(r << 16 | g << 8 | b));
        }
        return new(document, conversion.Objects, paths.ToArray(), indices, colours.ToArray());
    }

    private static SyncComparison PrepareSyncComparison(WorkspaceMerge merge, bool compensate, string? authoringPath = null)
    {
        var local = PrepareSyncPane(merge.Local, compensate); var external = PrepareSyncPane(merge.External.Document, compensate);
        var focus = new Dictionary<string, SyncFocus>();
        foreach (var conflict in merge.Conflicts)
        {
            var ours = merge.ConflictSources(conflict.Key, false); var theirs = merge.ConflictSources(conflict.Key, true);
            var times = local.Objects.Where(o => ours.Contains(o.SourceId)).Concat(external.Objects.Where(o => theirs.Contains(o.SourceId))).Select(o => o.TimeMs)
                .Concat(local.Paths.Where(p => ours.Contains(p.Source)).Concat(external.Paths.Where(p => theirs.Contains(p.Source))).SelectMany(p => p.Points.Select(v => v.TimeMs))).ToArray();
            focus[conflict.Key] = new(ours, theirs, times.Length == 0 ? 0 : times.Min(), times.Length == 0 ? 0 : times.Max());
        }
        var rejectedLines = merge.PreviouslyRetained.SelectMany(r => r.ExternalLines).ToHashSet();
        var rejectedOrders = WorkspaceSynchronization.ObjectLines(merge.External.Text).Select((line, order) => (line, order))
            .Where(p => rejectedLines.Contains(p.line)).Select(p => p.order).ToHashSet();
        return new(local, external, focus, authoringPath is not null && File.Exists(authoringPath) ? File.GetLastWriteTimeUtc(authoringPath) : null,
            File.Exists(merge.External.Path) ? File.GetLastWriteTimeUtc(merge.External.Path) : null,
            merge.PreviouslyRetained.SelectMany(r => r.Sources).ToHashSet(),
            WorkspaceSynchronization.SourceIds(merge.External.Document).Where(p => rejectedOrders.Contains(p.Order)).Select(p => p.Id).ToHashSet(),
            SyncAudioHash(merge.External.Document.AudioPath), PrepareSyncReview(merge));
    }

    private void DrawSyncComparison(ICanvas c, WorkspaceMerge merge, SyncComparison comparison)
    {
        syncRow = Math.Clamp(syncRow, 0, merge.Conflicts.Count - 1);
        syncTab ??= SyncCategory(merge.Conflicts[syncRow].Key);
        if (SyncFieldOrder.ContainsKey(syncTab))
        { DrawSyncFields(c, merge, comparison); DrawSyncTabs(c, merge, comparison); return; }
        var categoryConflicts = merge.Conflicts.Where(c => SyncCategory(c.Key) == syncTab).ToArray();
        var conflict = categoryConflicts.FirstOrDefault(c => c.Key == merge.Conflicts[syncRow].Key) ?? categoryConflicts.FirstOrDefault();
        if (syncTab != "Objects")
        {
            string section = syncTab == "Timing" ? "TimingPoints" : syncTab;
            conflict ??= new(section + "/", comparison.Review.LocalSections.GetValueOrDefault(section, ""), comparison.Review.ExternalSections.GetValueOrDefault(section, ""));
            DrawSyncSection(c, merge, comparison, conflict); DrawSyncTabs(c, merge, comparison); return;
        }
        bool hasConflict = conflict is not null;
        conflict ??= new("$objects:all", "", "");
        var focus = comparison.Focus.GetValueOrDefault(conflict.Key) ?? new SyncFocus(new HashSet<Guid>(), new HashSet<Guid>(),
            comparison.Local.Objects.FirstOrDefault()?.TimeMs ?? 0, comparison.Local.Objects.LastOrDefault()?.TimeMs ?? 0);
        if (!ReferenceEquals(syncVisualMerge, merge))
        { syncVisualMerge = merge; syncVisualKey = null; syncResultPane = null; syncPreviewRevision++; }
        bool newFocus = syncVisualKey != conflict.Key;
        if (newFocus)
        {
            syncVisualKey = conflict.Key;
            syncZoom = 1; syncObjectDetailScroll = 0;
        }
        PumpSyncPreview(merge);
        float x = 16, y = 16, w = width - 32, h = height - 32;
        c.Fill(new(0, 0, width, height), Background);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("sync.title"), x + 20, y + 16, 20, Foreground, w - 40, true);
        c.Text(syncStatuses.GetValueOrDefault(syncDifficulty)?.State == WorkspaceSyncState.NeedsBaseline ? L.Get("sync.compareNoBaseline") : L.Get("sync.compareHelp"), x + 20, y + 46, 12, Muted, w - 40);
        string label = !hasConflict ? L.Get("ui.unchanged") : conflict.Key.StartsWith("$objects:") ? L.Get("sync.objects", Array.IndexOf(categoryConflicts, conflict) + 1) : conflict.Key == "$audio" ? L.Get("sync.audio") : conflict.Key;
        string stateLabel = syncRoundChoices.Contains(conflict.Key) ? L.Get("sync.resolvedThisRound")
            : merge.PreviouslyResolved.Contains(conflict.Key) ? L.Get("sync.alreadyResolved") : L.Get("sync.unresolvedRange");
        if (hasConflict) label += " · " + stateLabel;
        c.Text(label, x + 20, y + 106, 15, Accent, w - 40);
        float paneWidth = (w - 40 - (syncShowResult ? 24 : 12)) / (syncShowResult ? 3 : 2);
        if (hasConflict) DrawSyncObjectDetails(c, comparison.Review.Objects[conflict.Key], new(x + 20, y + 130, w - 40, 74));
        else syncObjectDetailBounds = default;
        var left = new Rect(x + 20, y + 214, paneWidth, h - 390);
        var right = left with { X = left.Right + 12 };
        syncShowValues = !conflict.Key.StartsWith("$objects:");
        var field = SyncField(left);
        double center = syncViewStart + syncViewSpan / 2;
        // A common AR scale keeps matching times aligned even when the versions disagree about AR.
        syncViewSpan = field.Height / (CatchScrollTiming.PixelsPerMs(comparison.Local.Document.ApproachRate, field.Width) * syncZoom);
        syncViewStart = Math.Max(0, newFocus ? focus.Start - syncViewSpan * .35 : center - syncViewSpan / 2);
        syncCanvasBounds = new(left.X, left.Y, w - 40, left.Height);
        bool dirty = difficulties.FirstOrDefault(d => d.Id == syncDifficulty)?.History.IsDirty == true;
        var ranges = comparison.Focus.Where(p => p.Key.StartsWith("$objects:")).Select(p => new SyncRange(p.Key, p.Value.Start, p.Value.End,
            syncRoundChoices.Contains(p.Key) ? SyncResolvedThisRound : merge.PreviouslyResolved.Contains(p.Key) ? SyncPreviouslyResolved : SyncUnresolved,
            syncRoundChoices.Contains(p.Key) ? L.Get("sync.resolvedThisRound") : merge.PreviouslyResolved.Contains(p.Key) ? L.Get("sync.alreadyResolved") : L.Get("sync.unresolvedRange"))).ToArray();
        SyncRange[] PaneRanges(bool external) => ranges.Select(range =>
            syncChoices.TryGetValue(range.Key, out bool chosenExternal) && chosenExternal != external
                ? range with { Colour = Muted } : range).ToArray();
        var localChoices = comparison.RetainedLocal.ToDictionary(id => id, _ => true);
        var externalChoices = comparison.RejectedExternal.ToDictionary(id => id, _ => false);
        foreach (var choice in syncChoices)
            if (comparison.Focus.TryGetValue(choice.Key, out var decided))
            {
                foreach (var id in decided.Local) localChoices[id] = !choice.Value;
                foreach (var id in decided.External) externalChoices[id] = choice.Value;
            }
        DrawSyncPane(c, comparison.Local, left, "FA", focus.Local, conflict.Key.StartsWith("$objects:") ? null : conflict.Local,
            SavedLabel(comparison.LocalSaved, comparison.ExternalSaved, dirty), localChoices,
            merge.PreviouslyResolved.Contains(conflict.Key) ? L.Get("sync.previouslyRejected") : null, PaneRanges(false), conflict.Key,
            selectRange: key => ChooseSyncItem(merge, key, false));
        DrawSyncPane(c, comparison.External, right, "osu!", focus.External, conflict.Key.StartsWith("$objects:") ? null : conflict.External,
            SavedLabel(comparison.ExternalSaved, comparison.LocalSaved, false), externalChoices, ranges: PaneRanges(true), currentKey: conflict.Key,
            selectRange: key => ChooseSyncItem(merge, key, true));
        bool chosen = syncChoices.TryGetValue(conflict.Key, out bool external);
        if (chosen) c.Stroke(external ? right : left, Accent, 3);
        if (syncShowResult)
        {
            var result = right with { X = right.Right + 12 };
            if (syncResultPane is { } pane) DrawSyncPane(c, pane, result, L.Get("sync.resultPreview"), new HashSet<Guid>(), null);
            else c.Text(L.Get("sync.checking"), result.X + 12, result.Y + 35, 13, Muted, result.Width - 24);
        }
        SyncReviewButton(c, new(left.X, y + h - 164, left.Width, 36), L.Get("sync.chooseLocal"), () => ChooseSyncItem(merge, conflict.Key, false), chosen && !external, enabled: hasConflict);
        SyncReviewButton(c, new(right.X, y + h - 164, right.Width, 36), L.Get("sync.chooseExternal"), () => ChooseSyncItem(merge, conflict.Key, true), chosen && external, enabled: hasConflict);
        DrawSyncReviewFooter(c, merge, x, y, w, h, showPreview: true);
        DrawSyncTabs(c, merge, comparison);
    }

    private static string SavedLabel(DateTime? saved, DateTime? other, bool dirty)
    {
        string label = saved is { } date ? L.Get("sync.savedAt", date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)) : L.Get("sync.unsaved");
        if (saved is not null && other is not null && saved > other) label += " · " + L.Get("sync.newerSaved");
        if (dirty && saved is not null) label += " · " + L.Get("sync.unsaved");
        return label;
    }

    private void DrawSyncPane(ICanvas c, SyncPane pane, Rect bounds, string title, IReadOnlySet<Guid> selected, string? value, string? saved = null,
        IReadOnlyDictionary<Guid, bool>? decisions = null, string? absentMessage = null, IReadOnlyList<SyncRange>? ranges = null, string? currentKey = null, Action<string>? selectRange = null)
    {
        c.Fill(bounds, Background); c.Stroke(bounds, Grid);
        c.Text(title, bounds.X + 10, bounds.Y + 6, 15, Foreground, bounds.Width - 20, true);
        c.Text(saved ?? L.Get("sync.previewPending"), bounds.X + 10, bounds.Y + 27, 10, Muted, bounds.Width - 20);
        if (value is not null) DrawSyncValue(c, value.Length == 0 ? L.Get("sync.absent") : value, bounds.X + 10, bounds.Y + 50, bounds.Width - 20);
        var field = SyncField(bounds);
        float Y(double t) => field.Bottom - (float)((t - syncViewStart) / syncViewSpan * field.Height);
        float X(double p) => field.X + (float)(p / 512) * field.Width;
        c.Clip(bounds);
        double step = Math.Pow(10, Math.Floor(Math.Log10(syncViewSpan / 5)));
        if (syncViewSpan / step > 12) step *= 5;
        for (double time = Math.Ceiling(syncViewStart / step) * step; time <= syncViewStart + syncViewSpan; time += step)
        {
            float at = Y(time); c.Line(field.X, at, field.Right, at, Grid);
            c.Text(SyncObjectDifferences.FormatTime(time), bounds.X + 4, at - 7, 10, Muted, 74);
        }
        for (int i = 0; i <= 4; i++) c.Line(X(i * 128), field.Y, X(i * 128), field.Bottom, Grid, opacity: .5f);
        c.Clip(field);
        foreach (var range in ranges ?? [])
        {
            float top = Y(range.End), bottom = Y(range.Start);
            if (bottom < field.Y || top > field.Bottom) continue;
            float padding = Math.Max(8, (float)CatchSize.FruitRadius(pane.Document.CircleSize) * field.Width / 512);
            var area = new Rect(field.X + 1, top - padding, field.Width - 2, Math.Max(1, bottom - top) + padding * 2);
            c.Fill(area, range.Colour, opacity: .10f);
            c.Stroke(area, range.Colour, range.Key == currentKey ? 2.5f : 1.5f);
            c.Text(range.Label, field.X + 6, Math.Max(field.Y + 2, area.Y + 3), 10, range.Colour, field.Width - 12);
            string key = range.Key;
            hits.Add(new(area with { Y = Math.Max(area.Y, field.Y), Height = Math.Max(0, Math.Min(area.Bottom, field.Bottom) - Math.Max(area.Y, field.Y)) }, () =>
            {
                if (selectRange is not null) selectRange(key);
                else if (syncVisualMerge is { } active) syncRow = active.Conflicts.FindIndex(c => c.Key == key);
            }, true));
        }
        uint currentColour = ranges?.FirstOrDefault(r => r.Key == currentKey)?.Colour ?? SyncUnresolved;
        foreach (var path in pane.Paths)
        {
            if (path.Points.Length == 0 || path.End < syncViewStart || path.Start > syncViewStart + syncViewSpan) continue;
            bool highlight = selected.Contains(path.Source);
            bool decided = decisions?.TryGetValue(path.Source, out _) == true;
            bool retained = decided && decisions![path.Source];
            float opacity = !highlight && decided && !retained ? .2f : path.Controls ? .6f : 1;
            for (int i = 1; i < path.Points.Length; i++) c.Line(X(path.Points[i - 1].X), Y(path.Points[i - 1].TimeMs), X(path.Points[i].X), Y(path.Points[i].TimeMs), highlight ? currentColour : Purple, highlight ? 2.5f : 1.5f, opacity);
            if (path.Controls && highlight) foreach (var point in path.Points) Diamond(c, X(point.X), Y(point.TimeMs), 4, currentColour);
        }
        int low = 0, high = pane.Objects.Count;
        while (low < high) { int middle = low + (high - low) / 2; if (pane.Objects[middle].TimeMs < syncViewStart) low = middle + 1; else high = middle; }
        for (int i = low; i < pane.Objects.Count && pane.Objects[i].TimeMs <= syncViewStart + syncViewSpan; i++)
        {
            var item = pane.Objects[i];
            int index = pane.Indices.GetValueOrDefault(item.SourceId);
            uint colour = item.Kind == CatchObjectKind.Banana ? CatchObjectVisual.BananaColour(item.TimeMs)
                : pane.Colours.Length > 0 ? pane.Colours[index % pane.Colours.Length] : 0xFFFFFF;
            bool highlight = selected.Contains(item.SourceId);
            bool decided = decisions?.TryGetValue(item.SourceId, out _) == true;
            bool retained = decided && decisions![item.SourceId];
            DrawCatchObject(c, item, X(item.X), Y(item.TimeMs), field.Width, opacity: !highlight && decided && !retained ? .2f : 1, circleSize: pane.Document.CircleSize,
                hyperStarts: noSyncHyper, colourOverride: colour, skinIndexOverride: index);
            if (highlight) c.Circle(X(item.X), Y(item.TimeMs), (float)CatchSize.FruitRadius(pane.Document.CircleSize) * field.Width / 512 + 4, currentColour, false, 2.5f);
        }
        c.Unclip(); c.Unclip();
        if (value is null && selected.Count == 0 && currentKey != "$objects:all" && title is "FA" or "osu!") c.Text(absentMessage ?? L.Get("sync.absent"), bounds.X + 10, bounds.Y + 52, 12, Gold, bounds.Width - 20);
    }

    private void ScrollSyncComparison(float x, float y, float delta, bool zoom)
    {
        if (syncPage == "resolve" && syncTab == "Objects" && syncObjectDetailBounds.Contains(x, y))
        { syncObjectDetailScroll = Math.Clamp(syncObjectDetailScroll - (int)(delta / 120) * 2, 0, syncObjectDetailMax); return; }
        if (syncPage is not ("resolve" or "failed") || !syncCanvasBounds.Contains(x, y)) return;
        if (syncPage == "failed" || syncVisualKey?.StartsWith("$fields:", StringComparison.Ordinal) == true || syncVisualKey?.StartsWith("$text:", StringComparison.Ordinal) == true)
        { syncTextScroll = Math.Clamp(syncTextScroll - (int)(delta / 120 * 69), 0, syncTextMaxScroll); return; }
        if (zoom)
        {
            syncZoom = Math.Clamp(syncZoom * Math.Pow(1.2, delta / 120), .1, 10);
        }
        else syncViewStart = Math.Max(0, syncViewStart + delta / 120 * syncViewSpan / 8);
    }

    private Rect SyncField(Rect bounds)
    {
        float inset = syncShowValues ? 136 : 52;
        return new(bounds.X + 80, bounds.Y + inset, bounds.Width - 92, Math.Max(40, bounds.Height - inset - 14));
    }

    private void PumpSyncPreview(WorkspaceMerge merge)
    {
        if (syncPreviewTask is { IsCompleted: true } task)
        {
            syncPreviewTask = null;
            try { var result = task.GetAwaiter().GetResult(); if (ReferenceEquals(result.Merge, merge) && result.Revision == syncPreviewRevision) syncResultPane = result.Pane; }
            catch (Exception e) { syncShowResult = false; ShowError(e.Message); }
        }
        if (!syncShowResult || syncResultPane is not null || syncPreviewTask is not null) return;
        var choices = merge.Conflicts.ToDictionary(c => c.Key, c => syncChoices.GetValueOrDefault(c.Key));
        int revision = syncPreviewRevision; bool compensate = compensateTinyDroplets;
        syncPreviewTask = Task.Run(() => (merge, revision, PrepareSyncPane(WorkspaceSynchronization.Resolve(merge, choices), compensate)));
    }
}
