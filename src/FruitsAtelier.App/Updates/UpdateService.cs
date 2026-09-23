using System.Text.Json;
using FruitsAtelier.App.Editor;

namespace FruitsAtelier.App.Updates;

internal interface IUpdateBackend
{
    bool IsInstalled { get; }
    string? PendingVersion { get; }
    Task<string?> Check();
    Task Download(Action<int> progress, CancellationToken cancellation);
    void Apply();
}

internal sealed class UpdatePreferences
{
    public bool AutomaticChecks { get; set; } = true;
    public DateTimeOffset? LastCheck { get; set; }
}

internal sealed class UpdateService : IDisposable
{
    private readonly IUpdateBackend backend;
    private readonly string settingsPath;
    private readonly Action<string> log;
    private readonly Action? statusChanged;
    private readonly CancellationTokenSource cancellation = new();
    private int busy;
    private UpdateStatus status;
    public UpdateStatus Status => Volatile.Read(ref status);
    public UpdatePreferences Preferences { get; }

    public UpdateService(IUpdateBackend backend, string settingsPath, Action<string> log, Action? statusChanged = null)
    {
        this.backend = backend; this.settingsPath = settingsPath; this.log = log; this.statusChanged = statusChanged;
        try { Preferences = File.Exists(settingsPath) ? JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(settingsPath)) ?? new() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { log(e.ToString()); Preferences = new(); }
        status = backend.PendingVersion is { } version ? new(UpdatePhase.Ready, version)
            : new(backend.IsInstalled ? UpdatePhase.Idle : UpdatePhase.Unsupported);
    }

    public void SavePreferences()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(Preferences));
            File.Move(temporary, settingsPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public bool ShouldCheckOnStartup => backend.IsInstalled && Preferences.AutomaticChecks;

    private void PublishStatus(UpdateStatus value)
    {
        Volatile.Write(ref status, value);
        statusChanged?.Invoke();
    }

    public async Task Check(DateTimeOffset now)
    {
        if (!backend.IsInstalled || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        var previous = Status;
        PublishStatus(new(UpdatePhase.Checking));
        try
        {
            Preferences.LastCheck = now;
            try { SavePreferences(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log(e.ToString()); }
            string? version = await backend.Check().ConfigureAwait(false);
            PublishStatus(backend.PendingVersion is { } pending && (version is null || version == pending)
                ? new(UpdatePhase.Ready, pending, 100)
                : version is null ? new(UpdatePhase.Current) : new(UpdatePhase.Available, version));
        }
        catch (Exception e) { log(e.ToString()); PublishStatus(previous.Phase == UpdatePhase.Ready ? previous : new(UpdatePhase.Failed)); }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task Download()
    {
        if (Status.Phase != UpdatePhase.Available || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        string version = Status.Version;
        PublishStatus(new(UpdatePhase.Downloading, version));
        try
        {
            await backend.Download(p => PublishStatus(new(UpdatePhase.Downloading, version, Math.Clamp(p, 0, 100))), cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            PublishStatus(new(UpdatePhase.Ready, version, 100));
        }
        catch (Exception e) { log(e.ToString()); PublishStatus(new(UpdatePhase.Failed, version)); }
        finally { Volatile.Write(ref busy, 0); }
    }

    public void Apply(Func<bool> save)
    {
        if (Status.Phase != UpdatePhase.Ready || !save()) return;
        backend.Apply();
    }

    public void Dispose() => cancellation.Cancel();
}
