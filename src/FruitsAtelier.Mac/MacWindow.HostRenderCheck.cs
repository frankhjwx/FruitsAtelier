using Avalonia;
using Avalonia.Media.Imaging;
using FruitsAtelier.Core;

namespace FruitsAtelier.Mac;

internal sealed partial class MacWindow
{
    // Run-Editor-Mac.command --smoke-check --mac-host-check
    // Real Avalonia frames are required: recording canvases cannot detect render invalidation.
    private async Task HostRenderCheck()
    {
        string folder = Path.Combine(MacPaths.Artifacts, "macos-check", "host", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        View.LibrarySettings.Workspace = folder;
        View.LibrarySettings.Songs = "";
        // Optional user fixture is read only; all workspace operations target a
        // newly created session below artifacts, never the original workspace.
        string? source = Environment.GetEnvironmentVariable("FRUITS_MAC_CHECK_MAP");
        MapDocument? external = source is not null ? ProjectSerializer.ReadFile(source) : null;
        if (external is not null) external.SourcePath = null;
        int expectedFruits = external?.Fruits.Count ?? 1;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var document = external?.DeepClone() ?? new MapDocument { Name = "Mac open fixture " + cycle, DurationMs = 5000 };
            if (external is null) document.Fruits.Add(new Fruit { TimeMs = 1000, X = 160 });
            var session = WorkspaceProject.Create(folder, BeatmapProject.FromDocuments([document]), "");
            var map = new LibraryMap(Path.Combine(session.Directory, session.Manifest.Difficulties[0].File),
                session.Directory, document.Name, "", "", "", "", "Rain", "", "", "", "", session.Directory);
            View.NewProject(); View.ShowLibrary(); editor.Focus();
            View.RequestLibraryOpen!(map, false);
            await WaitFor(() => View.WorkspaceSession?.Directory == session.Directory && !busy);
            if (View.Document.Fruits.Count != expectedFruits || View.ErrorVisible)
                throw new InvalidOperationException("Library open entered render recovery or lost the fixture.");
            Console.WriteLine($"PASS native library open {cycle}: focus and completed audio callbacks during render");
        }

        // Completion occurs inside View.Render. Exercise the recovery refresh through
        // that same boundary, rather than merely testing a queued ordinary refresh.
        var current = View.WorkspaceSession!;
        var recoveryMap = new LibraryMap(Path.Combine(current.Directory, current.Manifest.Difficulties[0].File),
            current.Directory, "Recovery fixture", "", "", "", "", "Rain", "", "", "", "", current.Directory);
        View.NewProject(); View.ShowLibrary();
        View.BeginLibraryOpen(recoveryMap, () => editor.ReportUiFailure(new InvalidOperationException("Injected Mac render callback failure")));
        await WaitFor(() => View.RecoveryActive, allowRecovery: true);
        await Task.Delay(150);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)editor.Bounds.Width, (int)editor.Bounds.Height), new Vector(96, 96));
        bitmap.Render(editor); bitmap.Save(Path.Combine(folder, "recovery.png"));
        View.RetryRender(); editor.Refresh();
        await Task.Delay(150);
        if (View.ErrorVisible || View.Document.Fruits.Count != expectedFruits)
            throw new InvalidOperationException("Recovery retry lost the fixture or failed to resume rendering.");
        Console.WriteLine("PASS native render recovery and retry retain the loaded project");

        async Task WaitFor(Func<bool> ready, bool allowRecovery = false)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!ready() && DateTime.UtcNow < deadline)
            {
                if (!allowRecovery && View.ErrorVisible) throw new InvalidOperationException("Native library open entered error recovery.");
                editor.Refresh(); await Task.Delay(30);
            }
            if (!ready()) throw new TimeoutException("Native library operation did not complete.");
            await Task.Delay(100);
        }
    }
}
