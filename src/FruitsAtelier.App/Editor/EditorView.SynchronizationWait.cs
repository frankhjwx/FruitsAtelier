using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool synchronizationWaitRequested;
    private Action<bool>? synchronizationWaitCompleted;
    public bool SynchronizationWaitVisible => synchronizationWaitRequested && SynchronizationBusy && !ErrorVisible;

    private bool NotifySynchronizationBlocked()
    {
        if (!SynchronizationBusy) return false;
        synchronizationWaitRequested = true;
        return true;
    }

    public bool WaitForSynchronization(Action<bool> completed)
    {
        if (!NotifySynchronizationBlocked()) return false;
        if (synchronizationWaitCompleted is not null) completed(false);
        else synchronizationWaitCompleted = completed;
        return true;
    }

    private void PumpSynchronizationWait()
    {
        if (!synchronizationWaitRequested || SynchronizationBusy && !ErrorVisible) return;
        synchronizationWaitRequested = false;
        var completed = synchronizationWaitCompleted;
        synchronizationWaitCompleted = null;
        completed?.Invoke(!ErrorVisible && !SynchronizationVisible);
    }

    private void DrawSynchronizationWait(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        c.Fill(new(0, 0, width, height), Background, opacity: .35f);
        float w = Math.Min(420, width - 32), x = (width - w) / 2, y = (height - 126) / 2;
        c.Fill(new(x, y, w, 126), Panel, 8);
        c.Stroke(new(x, y, w, 126), Accent, 1, 8);
        c.Text(L.Get(workspaceSaveTask is not null ? "files.saving" : syncCommitTask is not null ? "sync.applying" : "sync.checking"),
            x + 20, y + 20, 17, Foreground, w - 40, true);
        c.Text(L.Get("sync.waitHelp"), x + 20, y + 52, 12, Muted, w - 40);
        c.Fill(new(x + 20, y + 93, w - 40, 4), Grid, 2);
        float segment = (w - 40) / 4;
        float phase = (float)((Math.Sin(TestplayRealtime / 300) + 1) / 2);
        c.Fill(new(x + 20 + phase * (w - 40 - segment), y + 93, segment, 4), Accent, 2);
    }
}
