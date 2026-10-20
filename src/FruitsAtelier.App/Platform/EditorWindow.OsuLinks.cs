using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private long nextOsuLinkCheck;
    private readonly Queue<string> pendingOsuForwards = new();
    private void ConfigureOsuLinks()
    {
        view.ReadOsuLinkSettings = () =>
        {
            var preferences = OsuLinkPreferences.Load();
            return (preferences.TargetId, OsuLinkAssociation.IsDefault, OsuLinkAssociation.TargetReady(preferences));
        };
        view.ReadOsuLinkApplications = () => OsuLinkApplications.Discover();
        view.RequestSelectOsuLinkFallback = id => FileOperation(() =>
        {
            if (OsuLinkAssociation.Resolve(id) is null) throw new InvalidOperationException(L.Get("osuLinks.targetInvalid"));
            new OsuLinkPreferences { TargetId = id }.Save();
            view.RefreshOsuLinkSettings(); Invalidate();
        });
    }

    private void PollOsuLinks()
    {
        if (Environment.TickCount64 >= nextOsuLinkCheck)
        {
            bool wasRequired = view.OsuLinkFallbackRequired;
            view.RefreshOsuLinkSettings(); nextOsuLinkCheck = Environment.TickCount64 + 1000;
            if (wasRequired != view.OsuLinkFallbackRequired) Invalidate();
        }
        if (!view.OsuLinkFallbackRequired && pendingOsuForwards.TryDequeue(out string? waiting))
        { FileOperation(() => OsuLinkAssociation.Forward(waiting)); return; }
        if (!FazFileAssociation.PendingLinks.TryDequeue(out string? link)) return;
        Native.ShowWindow(hwnd, Native.IsIconic(hwnd) ? 9 : 5); Native.SetForegroundWindow(hwnd);
        if (EditorTimestamp.IsEditorLink(link)) view.HandleEditorLink(link);
        else if (view.OsuLinkFallbackRequired) pendingOsuForwards.Enqueue(link);
        else FileOperation(() => OsuLinkAssociation.Forward(link));
        Invalidate();
    }
}
