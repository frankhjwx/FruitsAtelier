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
    private readonly CancellationTokenSource cancellation = new();
    private int busy;
    private UpdateStatus status;
    public UpdateStatus Status => Volatile.Read(ref status);
    public UpdatePreferences Preferences { get; }

    public UpdateService(IUpdateBackend backend, string settingsPath, Action<string> log)
    {
        this.backend = backend; this.settingsPath = settingsPath; this.log = log;
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

    public bool ShouldCheck(DateTimeOffset now) => backend.IsInstalled && Preferences.AutomaticChecks
        && Status.Phase != UpdatePhase.Ready && (Preferences.LastCheck is not { } last || now < last || now - last >= TimeSpan.FromDays(1));

    public async Task Check(DateTimeOffset now)
    {
        if (!backend.IsInstalled || Status.Phase == UpdatePhase.Ready || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        Volatile.Write(ref status, new(UpdatePhase.Checking));
        try
        {
            Preferences.LastCheck = now;
            SavePreferences();
            string? version = await backend.Check().ConfigureAwait(false);
            Volatile.Write(ref status, version is null ? new(UpdatePhase.Current) : new(UpdatePhase.Available, version));
        }
        catch (Exception e) { log(e.ToString()); Volatile.Write(ref status, new(UpdatePhase.Failed)); }
        finally { Volatile.Write(ref busy, 0); }
    }

    public async Task Download()
    {
        if (Status.Phase != UpdatePhase.Available || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        string version = Status.Version;
        Volatile.Write(ref status, new(UpdatePhase.Downloading, version));
        try
        {
            await backend.Download(p => Volatile.Write(ref status, new(UpdatePhase.Downloading, version, Math.Clamp(p, 0, 100))), cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            Volatile.Write(ref status, new(UpdatePhase.Ready, version, 100));
        }
        catch (Exception e) { log(e.ToString()); Volatile.Write(ref status, new(UpdatePhase.Failed, version)); }
        finally { Volatile.Write(ref busy, 0); }
    }

    public void Apply(Func<bool> save)
    {
        if (Status.Phase != UpdatePhase.Ready || !save()) return;
        backend.Apply();
    }

    public void Dispose() => cancellation.Cancel();
}
