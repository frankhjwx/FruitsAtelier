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
    public Rect StreamSnapBounds { get; private set; }
    private bool SelectedStreamsOnly => ClipboardSelectedParentIds() is { Count: > 0 } ids
        && ids.All(id => Document.Tracks.Any(t => t.Id == id && t.StreamSnapDivisor is not null));

    private bool CanConvertStream => ClipboardInteractionReady && ClipboardSelectedParentIds().Any(id =>
        Document.Tracks.Any(t => t.Id == id) || Document.ImportedSliders.Any(t => t.Id == id));

    private void OpenStreamDialog()
    {
        if (notesLocked) { StatusMessage = L.Get("assist.locked"); return; }
        if (!CanConvertStream) { StatusMessage = L.Get("stream.selectSlider"); return; }
        streamTargets = ClipboardSelectedParentIds().Where(id => Document.Tracks.Any(t => t.Id == id)
            || Document.ImportedSliders.Any(t => t.Id == id)).ToArray();
        StreamSnapDivisor = Document.Tracks.FirstOrDefault(t => t.Id == streamTargets[0])?.StreamSnapDivisor ?? divisor;
        StreamSnapDivisor = SnapDivisors.MinBy(s => Math.Abs(s - StreamSnapDivisor));
        changingStreamSnap = SelectedStreamsOnly;
        streamSnapDragging = false;
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        StreamDialogVisible = true;
        streamError = "";
    }

    private void ApplyStream()
    {
        if (Edit(L.Get(changingStreamSnap ? "stream.changeSnap" : "stream.title"), () =>
        {
            foreach (Guid id in streamTargets)
            {
                var track = Document.Tracks.FirstOrDefault(t => t.Id == id)
                    ?? ImportedSliderEditing.ConvertToTrack(Document, id).Track;
                track.StreamSnapDivisor = StreamSnapDivisor;
            }
            var converted = CatchStreamConverter.Convert(Document);
            if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
        }))
        {
            StreamDialogVisible = false;
            streamSnapDragging = false;
            SelectObjects(streamTargets); tool = Tool.Select;
        }
        else streamError = StatusMessage;
    }

    private void StreamKey(int key)
    {
        if (key == 27) { StreamDialogVisible = false; streamSnapDragging = false; }
        else if (key == 13) ApplyStream();
        else if (key is 37 or 38 or 39 or 40)
            StreamSnapDivisor = SnapDivisors[Math.Clamp(Array.IndexOf(SnapDivisors, StreamSnapDivisor)
                + (key is 37 or 38 ? -1 : 1), 0, SnapDivisors.Length - 1)];
    }

    private void SetStreamSnap(float x)
    {
        float left = StreamSnapBounds.X + 7, right = StreamSnapBounds.Right - 31;
        int index = (int)MathF.Round(Math.Clamp((x - left) / (right - left), 0, 1) * (SnapDivisors.Length - 1));
        StreamSnapDivisor = SnapDivisors[index]; streamError = "";
    }

    private void ConvertStreamsBack()
    {
        if (!ClipboardInteractionReady || notesLocked) return;
        var ids = ClipboardSelectedParentIds();
        Edit(L.Get("stream.convertBack"), () =>
        {
            foreach (var track in Document.Tracks.Where(t => ids.Contains(t.Id))) track.StreamSnapDivisor = null;
            var converted = CatchStreamConverter.Convert(Document);
            if (!converted.Success) throw new InvalidOperationException(string.Join(L.Get("editor.diagnostics.separator"), converted.Diagnostics));
        });
    }

    private void DrawStreamDialog(ICanvas c)
    {
        if (!StreamDialogVisible) return;
        hits.Clear();
        float w = Math.Min(500, width - 32), x = (width - w) / 2, y = (height - 210) / 2;
        c.Fill(new(x, y, w, 210), Panel, 8); c.Stroke(new(x, y, w, 210), Grid, radius: 8);
        c.Text(L.Get(changingStreamSnap ? "stream.changeSnap" : "stream.title"), x + 18, y + 17, 16, Foreground, w - 36, true);
        c.Text(L.Get("stream.description"), x + 18, y + 49, 12, Muted, w - 36);
        c.Text(L.Get("ui.snap"), x + 18, y + 96, 11, Muted, 40);
        StreamSnapBounds = new(x + 64, y + 87, w - 88, 29);
        float left = StreamSnapBounds.X + 7, right = StreamSnapBounds.Right - 31;
        float knob = left + Array.IndexOf(SnapDivisors, StreamSnapDivisor) / (float)(SnapDivisors.Length - 1) * (right - left);
        c.Line(left, y + 102, right, y + 102, Accent, 2);
        c.Circle(knob, y + 102, 6, Accent);
        c.Text(L.Get("ui.snapDivisor", StreamSnapDivisor), StreamSnapBounds.Right - 28, y + 96, 10, Foreground, 40);
        Button(c, new(x + w - 194, y + 160, 80, 32), L.Get("mac.cancel"), () => StreamDialogVisible = false);
        if (streamError.Length > 0) c.Text(streamError, x + 18, y + 129, 11, Error, w - 36);
        Button(c, new(x + w - 106, y + 160, 88, 32), L.Get(changingStreamSnap ? "stream.saveSnap" : "stream.confirm"), ApplyStream, true);
    }
}
