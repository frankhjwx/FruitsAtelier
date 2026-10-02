using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool StreamDialogVisible { get; private set; }
    public int StreamSnapDivisor { get; private set; } = 4;
    private Guid[] streamTargets = [];
    private string streamError = "";
    private bool streamSnapDragging, changingStreamSnap;
    public bool StreamBreakIntoFruits { get; private set; }
    public Rect StreamSnapBounds { get; private set; }
    private bool SelectedStreamsOnly => ClipboardSelectedParentIds() is { Count: > 0 } ids
        && ids.All(id => Document.Tracks.Any(t => t.Id == id && t.StreamSnapDivisor is not null));

    private bool CanConvertStream => ClipboardInteractionReady && ClipboardSelectedParentIds().Any(id =>
        Document.Tracks.Any(t => t.Id == id) || Document.ImportedSliders.Any(t => t.Id == id));

    internal void OpenStackDialog()
    {
        OpenStreamDialog();
        if (!StreamDialogVisible) return;
        SetConversionMode(true);
    }

    private void InitializeConversionPreview()
    {
        stackSelectedPoint = 0;
        stackTiming = new TimingMap.Lookup(Document);
        stackDraft = Document.Tracks.FirstOrDefault(t => t.Id == streamTargets[0])?.Stack?.DeepClone() ?? new();
        for (int i = 0; i < stackDraft.Points.Count; i++)
            stackDraft.Points[i] = stackDraft.Points[i] with { Distance = Math.Min(StackMaximumDistance, stackDraft.Points[i].Distance) };
        stackPreviewSource = Document.Tracks.FirstOrDefault(t => t.Id == streamTargets[0])?.DeepClone();
        if (stackPreviewSource is null)
        {
            try { stackPreviewSource = ImportedSliderEditing.ConvertToTrack(Document.DeepClone(), streamTargets[0]).Track; }
            catch (Exception error) { StreamDialogVisible = false; StatusMessage = error.Message; }
        }
        stackSnap = stackPreviewSource?.Stack is null ? 16 : StreamSnapDivisor;
        streamSnap = StreamSnapDivisor;
        stackPreviewScrollMs = 0;
        stackNumericField = -1; stackNumericError = "";
        stackMode = true;
        StreamSnapDivisor = stackSnap;
        RefreshStackPreview();
        double start = stackPreviewSource?.Nodes[0].TimeMs ?? 0;
        double duration = stackPreviewSource is null ? 1 : Math.Max(.001, CurveMath.EndTimeMs(stackPreviewSource) - start);
        foreach (var fruit in stackPreview)
        {
            double u = (fruit.TimeMs - start) / duration;
            if (Math.Abs(stackDraft.AdjustmentAt(u)) < 1e-8) continue;
            double center = CurveMath.PositionAtTime(stackPreviewSource!, fruit.TimeMs);
            double distance = fruit.X - center;
            if (Math.Abs(distance) <= StackMaximumDistance) continue;
            double side = (fruit.EventIndex % 2 == 0) == stackDraft.StartLeft ? -1 : 1;
            double baseX = Math.Clamp(center + side * stackDraft.DistanceAt(u), 0, 512);
            stackDraft.SetAdjustment(u, Math.Clamp(center + Math.Sign(distance) * StackMaximumDistance, 0, 512) - baseX);
        }
        RefreshStackPreview(); ResetStackHistory();
        stackMode = false; StreamSnapDivisor = streamSnap;
        RefreshStackPreview();
    }

    private void OpenStreamDialog()
    {
        if (notesLocked) { StatusMessage = L.Get("assist.locked"); return; }
        if (!CanConvertStream) { StatusMessage = L.Get("stream.selectSlider"); return; }
        streamTargets = ClipboardSelectedParentIds().Where(id => Document.Tracks.Any(t => t.Id == id)
            || Document.ImportedSliders.Any(t => t.Id == id)).ToArray();
        StreamSnapDivisor = Document.Tracks.FirstOrDefault(t => t.Id == streamTargets[0])?.StreamSnapDivisor ?? divisor;
        StreamSnapDivisor = SnapDivisors.MinBy(s => Math.Abs(s - StreamSnapDivisor));
        changingStreamSnap = SelectedStreamsOnly;
        StreamBreakIntoFruits = false;
        streamSnapDragging = false;
        stackMode = false; stackPointDragging = stackFruitDragging = stackSelectedFruit = -1;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        StreamDialogVisible = true;
        streamError = "";
        InitializeConversionPreview();
    }

    private int streamSnap, stackSnap;
    private void SetConversionMode(bool stack)
    {
        if (stackMode == stack || !CommitStackNumeric()) return;
        CancelStackDrag(); streamSnapDragging = false;
        if (stackMode) stackSnap = StreamSnapDivisor; else streamSnap = StreamSnapDivisor;
        stackMode = stack;
        StreamSnapDivisor = stack ? stackSnap : streamSnap;
        RefreshStackPreview();
    }

    private void ApplyStream()
    {
        if (stackMode && !CommitStackNumeric()) return;
        Guid[] selected = streamTargets;
        if (Edit(L.Get(stackMode ? "stack.title" : changingStreamSnap ? "stream.changeSnap" : "stream.title"), () =>
        {
            foreach (Guid id in streamTargets)
            {
                var track = Document.Tracks.FirstOrDefault(t => t.Id == id)
                    ?? ConvertImportedSlider(id).Track;
                track.StreamSnapDivisor = StreamSnapDivisor;
                track.Stack = stackMode ? stackDraft.DeepClone() : null;
            }
            if (StreamBreakIntoFruits) selected = ObjectStructureEditing.BreakStreams(Document, streamTargets);
            var converted = CatchStreamConverter.Convert(Document);
            if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
        }))
        {
            StreamDialogVisible = false;
            streamSnapDragging = false;
            SelectObjects(selected); tool = Tool.Select;
        }
        else streamError = StatusMessage;
    }

    private void StreamKey(int key, bool ctrl, bool shift)
    {
        if (stackMode && StackHistoryKey(key, ctrl, shift)) return;
        if (stackMode && StackNumericKey(key, ctrl, shift)) return;
        if (key == 27) { StreamDialogVisible = false; streamSnapDragging = false; stackPointDragging = stackFruitDragging = -1; }
        else if (key == 9) SetConversionMode(!stackMode);
        else if (key == 13) ApplyStream();
        else if (key is 37 or 38 or 39 or 40)
        {
            StreamSnapDivisor = SnapDivisors[Math.Clamp(Array.IndexOf(SnapDivisors, StreamSnapDivisor)
                + (key is 37 or 38 ? -1 : 1), 0, SnapDivisors.Length - 1)];
            RefreshStackPreview(); if (stackMode) RecordStackDraft();
        }
    }

    private void SetStreamSnap(float x)
    {
        float left = StreamSnapBounds.X + 7, right = StreamSnapBounds.Right - 31;
        int index = (int)MathF.Round(Math.Clamp((x - left) / (right - left), 0, 1) * (SnapDivisors.Length - 1));
        StreamSnapDivisor = SnapDivisors[index]; streamError = ""; RefreshStackPreview();
    }

    private void ConvertStreamsBack()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        var ids = ClipboardSelectedParentIds();
        Edit(L.Get("stream.convertBack"), () =>
        {
            foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id))) { track.StreamSnapDivisor = null; track.Stack = null; }
            var converted = CatchStreamConverter.Convert(Document);
            if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
        });
    }

    private void DrawStreamDialog(ICanvas c)
    {
        if (!StreamDialogVisible) return;
        DrawStackDialog(c);
    }
}
