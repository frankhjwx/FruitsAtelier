using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Updates;
using L = FruitsAtelier.Localization.Strings;

internal static class UpdateTests
{
    public static void Lifecycle() => Run().GetAwaiter().GetResult();
    private static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "atelier-updates-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "updates.json");
            var backend = new Backend();
            using var service = new UpdateService(backend, path, _ => { });
            var now = DateTimeOffset.UtcNow;
            Check(service.ShouldCheck(now), "First installed launch checks for updates");
            var check = service.Check(now);
            await service.Check(now);
            Check(backend.Checks == 1 && service.Status.Phase == UpdatePhase.Checking, "Concurrent checks are coalesced");
            backend.Result.SetResult("0.8.2"); await check;
            Check(service.Status == new UpdateStatus(UpdatePhase.Available, "0.8.2"), "Stable update becomes available");
            Check(!service.ShouldCheck(now.AddHours(23)) && service.ShouldCheck(now.AddDays(1)), "Daily background check limit");
            using var restarted = new UpdateService(new Backend(), path, _ => { });
            Check(!restarted.ShouldCheck(now), "Last attempt survives restarting");
            service.Preferences.AutomaticChecks = false; service.SavePreferences();
            Check(!service.ShouldCheck(now.AddDays(2)), "Automatic checks can be disabled");
            var download = service.Download(); await service.Download();
            Check(backend.Downloads == 1 && service.Status.Phase == UpdatePhase.Downloading, "Concurrent downloads are coalesced");
            backend.Progress!(42);
            Check(service.Status.Progress == 42, "Download progress is reported");
            backend.DownloadResult.SetResult(); await download;
            Check(service.Status.Phase == UpdatePhase.Ready, "Only completed download becomes ready");
            service.Apply(() => false); Check(backend.Applies == 0, "Failed or canceled save prevents installation");
            try { service.Apply(() => throw new IOException("save failure")); } catch (IOException) { }
            Check(backend.Applies == 0, "Save exception prevents installation");
            service.Apply(() => true); Check(backend.Applies == 1, "Successful save permits installation");

            var bad = new Backend(); bad.Result.SetResult("0.8.2");
            using var failure = new UpdateService(bad, path, _ => { });
            await failure.Check(now);
            bad.DownloadResult.SetException(new InvalidDataException("checksum mismatch"));
            await failure.Download(); failure.Apply(() => true);
            Check(failure.Status.Phase == UpdatePhase.Failed && bad.Applies == 0, "Invalid download never installs");
            var network = new Backend(); network.Result.SetException(new HttpRequestException("offline"));
            using var offline = new UpdateService(network, path, _ => { }); await offline.Check(now);
            Check(offline.Status.Phase == UpdatePhase.Failed, "Network failure is recoverable");
            var pending = new Backend { PendingVersion = "0.8.2" };
            using var ready = new UpdateService(pending, path, _ => { });
            Check(ready.Status.Phase == UpdatePhase.Ready && !ready.ShouldCheck(now.AddDays(2)), "Downloaded update survives restart without auto-applying");
            using var development = new UpdateService(new Backend { IsInstalled = false }, path, _ => { });
            Check(development.Status.Phase == UpdatePhase.Unsupported && !development.ShouldCheck(now), "Source checkout does not self-update");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void Interface()
    {
        var ui = new Ui(false);
        int check = 0, download = 0, restart = 0, preference = 0;
        ui.View.RequestUpdateCheck = () => check++;
        ui.View.RequestUpdateDownload = () => download++;
        ui.View.RequestUpdateRestart = () => restart++;
        ui.View.RequestUpdatePreference = () => preference++;
        ui.View.ShowLibrary(); ui.Paint();
        Click("library.settings"); Click("update.title");
        Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("update.currentVersion", "v" + System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(EditorView).Assembly)!.InformationalVersion.Split('+')[0])), "Update page displays build version");
        Click("update.check"); Check(check == 1, "Manual check action");
        ui.View.UpdateStatus = new(UpdatePhase.Available, "0.8.2"); ui.Paint(); Click("update.download");
        Check(download == 1, "Download action");
        ui.View.UpdateStatus = new(UpdatePhase.Downloading, "0.8.2", 42); ui.Paint();
        ui.Click(50, 310); Check(check == 1 && restart == 0, "Installing is unavailable during download");
        ui.View.UpdateStatus = new(UpdatePhase.Ready, "0.8.2"); ui.Paint(); Click("update.restart");
        Check(restart == 1, "Restart requires explicit action");
        Click("update.automaticOn"); Check(!ui.View.AutomaticUpdateChecks && preference == 1, "Preference toggles and persists");
        ui.Key(27); ui.Paint(); Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("library.apply")), "Escape returns to settings");
        void Click(string key)
        {
            var text = ui.Canvas.Texts.Last(t => t.Value == L.Get(key));
            ui.Click(text.X + 2, text.Y + 2); ui.Paint();
        }
    }

    private sealed class Backend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string? PendingVersion { get; set; }
        public int Checks, Downloads, Applies;
        public TaskCompletionSource<string?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DownloadResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<int>? Progress;
        public Task<string?> Check() { Checks++; return Result.Task; }
        public Task Download(Action<int> progress, CancellationToken cancellation) { Downloads++; Progress = progress; return DownloadResult.Task; }
        public void Apply() => Applies++;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
