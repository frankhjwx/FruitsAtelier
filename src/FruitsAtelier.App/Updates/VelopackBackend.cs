using Velopack;
using Velopack.Sources;

namespace FruitsAtelier.App.Updates;

internal sealed class VelopackBackend : IUpdateBackend
{
    public const string Repository = "https://github.com/frankhjwx/FruitsAtelier";
    internal const string Channel = "win-x64";
    private readonly UpdateManager manager;
    private UpdateInfo? update;
    public VelopackBackend() : this(new GithubReleaseSource(Repository, Channel)) { }
    internal VelopackBackend(IUpdateSource source) => manager = new(source, new UpdateOptions { ExplicitChannel = Channel });
    public bool IsInstalled => manager.IsInstalled;
    public string? PendingVersion => manager.UpdatePendingRestart?.Version.ToString();
    public async Task<string?> Check()
    {
        update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return update?.TargetFullRelease.Version.ToString();
    }
    public Task Download(Action<int> progress, CancellationToken cancellation)
        => manager.DownloadUpdatesAsync(update ?? throw new InvalidOperationException("No update selected."), progress, cancellation);
    public void Apply() => manager.ApplyUpdatesAndRestart(manager.UpdatePendingRestart ?? throw new InvalidOperationException("No verified update is ready."));
}
