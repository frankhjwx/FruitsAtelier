using System.Globalization;
using System.Text.RegularExpressions;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool CollaborationPanelVisible => PanelVisible(DockPanel.CollaborationMapping);
    private bool CollaborationFocused => CollaborationPanelVisible && DockContentInputAvailable && collaborationField.Length > 0;
    private Rect collaborationBounds;
    private float collaborationPanelScroll, collaborationContentHeight;
    internal Rect CollaborationPanelViewport => collaborationBounds;
    internal Rect CollaborationStartBounds { get; private set; }
    public bool CollaborationBusy => collaborationTask is not null;
    private sealed record CollaborationPrepared(MapDocument Document, HitsoundResourcePlan Resources, OsuWriteCache Cache, CatchConversionResult Conversion, OsuWriteResult Export,
        IReadOnlyList<CollaborationMapping.ExcludedObject> ExcludedObjects);
    private Task<CollaborationPrepared>? collaborationTask;
    private BeatmapProject? collaborationSnapshot;
    private Guid collaborationTarget;
    private EditorHistory? collaborationOwner;
    private bool collaborationCompensation;
    private string collaborationLanguage = "";
    private string collaborationError = "", collaborationField = "", collaborationText = "";
    private Guid collaborationFieldId;
    private bool collaborationFieldEnd;

    public void ShowCollaborationPanel()
    {
        if (!HasEditorProject || LibraryVisible || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        TimingPageVisible = false;
        AddDockPanel(DockPanel.CollaborationMapping, false);
        var group = PanelGroup(DockPanel.CollaborationMapping);
        group.Active = DockPanel.CollaborationMapping; group.Collapsed = false;
        panelMenuOpen = false; menu = -1; contextItems.Clear();
        collaborationError = collaborationField = "";
        collaborationPanelScroll = 0;
    }

    private void SetCollaborationReferences(List<CollaborationReference> references)
    {
        try
        {
            var candidate = Document.DeepClone(); candidate.CollaborationReferences = references.Count == 0 ? null : references;
            OsuBeatmapReader.Validate(candidate);
            CollaborationMapping.ValidateReferences(CollaborationProjectSnapshot(candidate, references, content: false), difficulties[activeDifficulty].Id, references);
            history.RestoreVersion(L.Get("collab.edit"), candidate);
            collaborationError = "";
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { collaborationError = error.Message; SetNotice(error.Message); }
    }

    // Only source/target content is copied for the worker; graph validation needs the other references alone.
    private BeatmapProject CollaborationProjectSnapshot(MapDocument target, IReadOnlyList<CollaborationReference> references, bool content = true)
    {
        var needed = references.Select(r => r.DifficultyId).ToHashSet();
        var id = difficulties[activeDifficulty].Id;
        return new BeatmapProject { Name = ProjectName, Difficulties = difficulties.Select(d => new ProjectDifficulty
        {
            Id = d.Id, Name = d.Name, FaOnly = d.FaOnly, Document = content && d.Id == id ? target.DeepClone() : content && needed.Contains(d.Id) ? d.History.Document.DeepClone()
                : new MapDocument { CollaborationReferences = (d.Id == id ? target : d.History.Document).CollaborationReferences?.ToList() }
        }).ToList() };
    }

    public void SynchronizeCollaboration()
    {
        if (CollaborationBusy || !CommitCollaborationField() || !PrepareFileOperation()) return;
        var references = Document.CollaborationReferences?.ToArray() ?? [];
        if (!references.Any(r => r.DifficultyId != Guid.Empty)) return;
        collaborationSnapshot = CollaborationProjectSnapshot(Document, references);
        collaborationTarget = difficulties[activeDifficulty].Id; collaborationOwner = history;
        var snapshot = collaborationSnapshot; var target = collaborationTarget; bool compensate = compensateTinyDroplets;
        collaborationCompensation = compensate;
        collaborationLanguage = L.Language;
        collaborationError = "";
        CloseCollaborationReport();
        collaborationTask = Task.Run(() =>
        {
            var cache = new OsuWriteCache();
            var candidate = CollaborationMapping.Synchronize(snapshot, target, references, compensate, cache);
            return new CollaborationPrepared(candidate.Document, candidate.Resources, cache, cache.Convert(candidate.Document, compensate), OsuBeatmapWriter.Serialize(candidate.Document, compensate, cache), candidate.ExcludedObjects);
        });
    }

    private void PumpCollaboration()
    {
        if (collaborationTask is not { IsCompleted: true } task) return;
        collaborationTask = null;
        var snapshot = collaborationSnapshot; collaborationSnapshot = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (snapshot is null || !ReferenceEquals(history, collaborationOwner) || difficulties[activeDifficulty].Id != collaborationTarget) return;
            if (collaborationCompensation != compensateTinyDroplets || collaborationLanguage != L.Language)
                throw new InvalidDataException(L.Get("collab.stale"));
            var needed = snapshot.Difficulties.Single(d => d.Id == collaborationTarget).Document.CollaborationReferences!
                .Select(r => r.DifficultyId).Append(collaborationTarget).ToHashSet();
            foreach (var before in snapshot.Difficulties.Where(d => needed.Contains(d.Id)))
            {
                var current = difficulties.FirstOrDefault(d => d.Id == before.Id);
                if (current is null || !current.History.Document.ContentEquals(before.Document))
                    throw new InvalidDataException(L.Get("collab.stale"));
            }
            var resourcePlan = result.Resources;
            resourcePlan.Apply();
            try
            {
                history.RestoreVersion(L.Get("collab.sync"), result.Document, (redo, _, _) =>
                {
                    if (redo) resourcePlan.Apply();
                    else resourcePlan.Restore(difficulties.Select(d => d.History.Document));
                });
            }
            catch { resourcePlan.Restore(); throw; }
            ResetDeferredConversion();
            editorWriteCache = result.Cache;
            convertedSnapshot = result.Document; convertedWithCompensation = collaborationCompensation;
            conversion = result.Conversion; playableExport = result.Export; playableObjects = result.Export.PlayableObjects;
            contentDragPreview = false; clickedCoordinate = null;
            Select(Guid.Empty); renderedTiming = SnapTiming(); RefreshConversionPresentation();
            PreloadProjectHitsounds();
            collaborationError = ""; SetNotice(L.Get("collab.done"));
            collaborationExcluded = result.ExcludedObjects;
            collaborationReportPosition = null; collaborationReportScroll = 0;
        }
        catch (Exception error) { collaborationError = error.Message; SetNotice(error.Message); }
        finally { collaborationOwner = null; }
    }

    private void AddCollaborationReference()
    {
        if (CollaborationBusy || !CommitCollaborationField()) return;
        var references = Document.CollaborationReferences?.ToList() ?? [];
        double start = references.Select(r => r.EndMs).DefaultIfEmpty(0).Max();
        references.Add(new(Guid.NewGuid(), Guid.Empty, start, Math.Min(int.MaxValue, Math.Max(start + 1000, Math.Ceiling(Document.DurationMs)))));
        SetCollaborationReferences(references);
        collaborationPanelScroll = Math.Max(0, 238 + references.Count * 40 - collaborationBounds.Height);
    }

    private void GenerateCollaborationSections()
    {
        if (CollaborationBusy || !CommitCollaborationField()) return;
        try
        {
            SetCollaborationReferences(CollaborationMapping.SectionsFromBookmarks(Document, TimelineDurationMs));
            collaborationPanelScroll = 0;
        }
        catch (InvalidDataException error) { collaborationError = error.Message; SetNotice(error.Message); }
    }

    private void PickCollaborationSource(Guid referenceId, float x, float y, int page = 0)
    {
        if (!CommitCollaborationField()) return;
        contextItems.Clear();
        contextItems.Add(new(L.Get("collab.unassigned"), () => SetCollaborationReferences((Document.CollaborationReferences ?? [])
            .Select(r => r.Id == referenceId ? r with { DifficultyId = Guid.Empty } : r).ToList())));
        var choices = difficulties.Where(d => d.Id != difficulties[activeDifficulty].Id).ToArray();
        foreach (var difficulty in choices.Skip(page * 8).Take(8))
            contextItems.Add(new(difficulty.Name, () => SetCollaborationReferences((Document.CollaborationReferences ?? [])
                .Select(r => r.Id == referenceId ? r with { DifficultyId = difficulty.Id } : r).ToList())));
        if (page > 0) contextItems.Add(new(L.Get("collab.previous"), () => PickCollaborationSource(referenceId, x, y, page - 1)));
        if ((page + 1) * 8 < choices.Length) contextItems.Add(new(L.Get("collab.next"), () => PickCollaborationSource(referenceId, x, y, page + 1)));
        contextBounds = new(Math.Clamp(x, 0, Math.Max(0, width - 280)), Math.Clamp(y, 0, Math.Max(0, height - ContextMenuHeight)), 280, ContextMenuHeight);
    }

    private void FocusCollaborationField(CollaborationReference reference, bool end)
    {
        if (!CommitCollaborationField()) return;
        collaborationFieldId = reference.Id; collaborationFieldEnd = end;
        collaborationField = "collab:" + reference.Id + (end ? ":end" : ":start");
        collaborationText = Time(end ? reference.EndMs : reference.StartMs);
        SelectInput(collaborationField, collaborationText);
    }

    private bool CommitCollaborationField()
    {
        if (collaborationField.Length == 0) return true;
        if (!TryParseCollaborationTime(collaborationText, out double value))
        { collaborationError = L.Get("collab.invalidRange"); return false; }
        var references = Document.CollaborationReferences?.ToList() ?? [];
        int index = references.FindIndex(r => r.Id == collaborationFieldId);
        if (index < 0) { collaborationField = ""; return true; }
        references[index] = collaborationFieldEnd ? references[index] with { EndMs = value } : references[index] with { StartMs = value };
        SetCollaborationReferences(references);
        if (collaborationError.Length > 0) return false;
        collaborationField = ""; return true;
    }

    private void CollaborationKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { collaborationField = collaborationError = ""; contextItems.Clear(); return; }
        if (CollaborationBusy) return;
        if (ctrl && key == 'S') { if (CommitCollaborationField()) RequestSave?.Invoke(); return; }
        if (collaborationField.Length > 0)
        {
            if (key == 13 || key == 9) { CommitCollaborationField(); return; }
            InputKey(collaborationField, ref collaborationText, key, ctrl, shift, 128, RequestPasteField); return;
        }
        if (ctrl && key is 'Z' or 'Y')
        { if (key == 'Y' || shift) history.Redo(); else history.Undo(); collaborationError = ""; }
    }

    internal static bool TryParseCollaborationTime(string text, out double value)
    {
        value = 0;
        var match = Regex.Match(text.Trim(), @"^(?<time>\d+:\d{1,2}:\d{1,3}|\d+)\s*(?:\(\d+(?:\s*,\s*\d+)*\)\s*-?)?$", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var parts = match.Groups["time"].Value.Split(':');
        if (parts.Length == 1)
            return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) && (value = milliseconds) >= 0;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds >= 60
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int ms) || ms >= 1000) return false;
        value = minutes * 60000d + seconds * 1000d + ms;
        return value <= int.MaxValue;
    }

    private bool CollaborationPointer(float x, float y, int button)
    {
        if (!CollaborationPanelVisible || !DockContentInputAvailable || menu >= 0 || contextItems.Count > 0) return false;
        if (!collaborationBounds.Contains(x, y)) return !CommitCollaborationField();
        if (button == 0) for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
        return true;
    }

    private void DrawCollaborationPanel(ICanvas c, Rect bounds)
    {
        var horizontal = PrepareDockHorizontal(DockPanel.CollaborationMapping, bounds, 332);
        collaborationBounds = horizontal.Viewport;
        var references = Document.CollaborationReferences ?? [];
        var incoming = difficulties.SelectMany(d => (d.History.Document.CollaborationReferences ?? [])
            .Where(r => r.DifficultyId == difficulties[activeDifficulty].Id).Select(r => (Difficulty: d, Reference: r))).ToArray();
        collaborationContentHeight = (references.Count == 0 ? 170 : 250 + references.Count * 40)
            + 60 + Math.Max(1, incoming.Length) * 68 + 60;
        collaborationPanelScroll = Math.Clamp(collaborationPanelScroll, 0, Math.Max(0, collaborationContentHeight - collaborationBounds.Height));
        float x = bounds.X - horizontal.Offset, w = horizontal.ContentWidth;
        float y = bounds.Y - collaborationPanelScroll;
        int firstHit = hits.Count;
        CollaborationStartBounds = default;
        c.Clip(collaborationBounds);
        c.Text(CurrentDifficultyName, x, y + 4, 15, Foreground, w, true);
        if (references.Count > 0)
        {
            c.Text(L.Get("collab.helpShort"), x, y + 34, 12, Muted, w);
            c.Text(L.Get("collab.completeObjects"), x, y + 56, 12, Muted, w);
        }
        float controlsY = y + (references.Count == 0 ? 38 : 86);
        Button(c, new(x, controlsY, (w - 8) / 2, 32), L.Get("collab.add"), AddCollaborationReference,
            enabled: !CollaborationBusy && references.Count < 256);
        Button(c, new(x + (w + 8) / 2, controlsY, (w - 8) / 2, 32), L.Get("collab.sync"), SynchronizeCollaboration,
            enabled: !CollaborationBusy && references.Any(r => r.DifficultyId != Guid.Empty));
        Button(c, new(x, controlsY + 40, w, 32), L.Get("collab.fromBookmarks"), GenerateCollaborationSections,
            enabled: !CollaborationBusy && Document.DurationMs > 0);
        float timeWidth = 90, sourceWidth = w - timeWidth * 2 - 50;
        if (references.Count > 0)
        {
            c.Text(L.Get("collab.references"), x, y + 180, 14, Foreground, w, true);
            c.Text(L.Get("collab.source"), x, y + 214, 11, Muted, sourceWidth);
            c.Text(L.Get("collab.start"), x + sourceWidth + 6, y + 214, 11, Muted, timeWidth);
            c.Text(L.Get("collab.end"), x + sourceWidth + timeWidth + 12, y + 214, 11, Muted, timeWidth);
        }
        y += references.Count == 0 ? 122 : 238;
        if (references.Count == 0)
        {
            c.Text(L.Get("collab.emptyShort"), x, y + 8, 12, Muted, w);
            y += 40;
        }
        foreach (var reference in references)
        {
            string name = reference.DifficultyId == Guid.Empty ? L.Get("collab.unassigned")
                : difficulties.FirstOrDefault(d => d.Id == reference.DifficultyId)?.Name ?? L.Get("collab.missing");
            float rowY = y;
            Button(c, new(x, y, sourceWidth, 32), name + " ▾", () => PickCollaborationSource(reference.Id, bounds.X, rowY + 34), enabled: !CollaborationBusy);
            Button(c, new(x + w - 32, y, 32, 32), "×", () =>
            {
                collaborationField = "";
                SetCollaborationReferences((Document.CollaborationReferences ?? []).Where(r => r.Id != reference.Id).ToList());
            }, enabled: !CollaborationBusy);
            Number(false, new(x + sourceWidth + 6, y, timeWidth, 32));
            Number(true, new(x + sourceWidth + timeWidth + 12, y, timeWidth, 32));
            y += 40;
            void Number(bool end, Rect input)
            {
                string key = "collab:" + reference.Id + (end ? ":end" : ":start");
                c.Fill(input, Surface, 4); c.Stroke(input, collaborationField == key ? Accent : Grid, radius: 4);
                DrawInputText(c, new(input.X + 8, input.Y + 8, input.Width - 16, 20),
                    collaborationField == key ? collaborationText : Time(end ? reference.EndMs : reference.StartMs), 12,
                    collaborationField == key, key);
                hits.Add(new(input, () => FocusCollaborationField(reference, end), !CollaborationBusy));
                if (!end && CollaborationStartBounds.Width == 0 && input.Y >= collaborationBounds.Y && input.Bottom <= collaborationBounds.Bottom)
                    CollaborationStartBounds = input;
            }
        }
        c.Line(x, y, x + w, y, Grid);
        c.Text(L.Get("collab.targets"), x, y + 16, 14, Foreground, w, true);
        y += 58;
        if (incoming.Length == 0) { c.Text(L.Get("collab.noTargets"), x, y + 8, 12, Muted, w); y += 68; }
        foreach (var item in incoming)
        {
            Button(c, new(x, y, w, 30), item.Difficulty.Name, () =>
            {
                if (CommitCollaborationField()) { SwitchDifficulty(difficulties.IndexOf(item.Difficulty)); collaborationField = ""; }
            }, enabled: !CollaborationBusy);
            c.Text(L.Get("collab.interval", Time(item.Reference.StartMs), Time(item.Reference.EndMs)), x + 8, y + 37, 12, Muted, w - 8);
            y += 68;
        }
        c.Text(CollaborationBusy ? L.Get("collab.checking") : collaborationError, x, y + 16, 12,
            CollaborationBusy ? Muted : Error, w);
        c.Unclip();
        for (int i = hits.Count - 1; i >= firstHit; i--)
        {
            var input = hits[i].Bounds;
            if (input.Y < collaborationBounds.Y || input.Bottom > collaborationBounds.Bottom) { hits.RemoveAt(i); continue; }
            float left = Math.Max(input.X, collaborationBounds.X), right = Math.Min(input.Right, collaborationBounds.Right);
            if (right <= left) hits.RemoveAt(i);
            else hits[i] = hits[i] with { Bounds = input with { X = left, Width = right - left } };
        }
        DrawDockHorizontal(c, horizontal);
        if (collaborationContentHeight > collaborationBounds.Height)
        {
            float thumb = Math.Max(20, collaborationBounds.Height * collaborationBounds.Height / collaborationContentHeight);
            c.Fill(new(bounds.Right + 5, bounds.Y + collaborationPanelScroll / (collaborationContentHeight - collaborationBounds.Height)
                * (collaborationBounds.Height - thumb), 3, thumb), Muted, 1);
        }
    }
}
