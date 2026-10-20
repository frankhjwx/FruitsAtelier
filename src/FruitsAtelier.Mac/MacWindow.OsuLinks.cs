using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    private long nextOsuLinkCheck;
    private readonly Queue<string> pendingOsuForwards = new();
    private void ConfigureOsuLinks()
    {
        View.ReadOsuLinkSettings = () =>
        {
            var preferences = OsuLinkPreferences.Load();
            return (preferences.TargetId, OperatingSystem.IsMacOS() && MacOsuLinks.IsDefault,
                OperatingSystem.IsMacOS() && MacOsuLinks.TargetReady(preferences));
        };
        View.ReadOsuLinkApplications = () => MacOsuLinks.Discover();
        View.RequestSelectOsuLinkFallback = id => RunFile(() =>
        {
            if (MacOsuLinks.Resolve(id) is null) throw new InvalidOperationException(L.Get("osuLinks.targetInvalid"));
            new OsuLinkPreferences { TargetId = id }.Save(); View.RefreshOsuLinkSettings(); editor.Refresh();
            return Task.CompletedTask;
        });
    }
    private void PollOsuLinks()
    {
        if (Environment.TickCount64 >= nextOsuLinkCheck)
        {
            bool wasRequired = View.OsuLinkFallbackRequired;
            View.RefreshOsuLinkSettings(); nextOsuLinkCheck = Environment.TickCount64 + 1000;
            if (wasRequired != View.OsuLinkFallbackRequired) editor.Refresh();
        }
        if (!View.OsuLinkFallbackRequired && pendingOsuForwards.TryDequeue(out string? waiting))
        { RunFile(() => { MacOsuLinks.Forward(waiting); return Task.CompletedTask; }); return; }
        if (!Program.OpenLinks.TryDequeue(out string? link)) return;
        Activate();
        if (EditorTimestamp.IsEditorLink(link)) View.HandleEditorLink(link);
        else if (View.OsuLinkFallbackRequired) pendingOsuForwards.Enqueue(link);
        else RunFile(() => { MacOsuLinks.Forward(link); return Task.CompletedTask; });
        editor.Refresh();
    }
}
