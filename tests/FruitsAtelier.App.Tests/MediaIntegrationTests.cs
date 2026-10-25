using System.Diagnostics;

internal static class MediaIntegrationTests
{
    public static int Run(bool finalFour = false)
    {
        (string Name, Action Check)[] checks =
        [
            ("Difficulty save and undo", DifficultySaveTests.Run),
            ("Version history", VersionHistoryUiTests.Run),
            ("Workspace save and export", WorkspaceSaveTests.Run),
            ("Romanised metadata", LibraryImportTests.Metadata),
            ("Mapping backgrounds", MappingCanvasBackgroundTests.Run),
            ("Library exit", LibraryExitTests.Run),
            ("Workspace Library", LibraryTests.Run),
            ("FAD import", FadTests.Run),
            ("Library navigation", LibraryNavigationTests.Run),
            ("Discard confirmation", DiscardConfirmationTests.Run),
            ("Project difficulties", ProjectTests.Run)
        ];
        int failed = 0;
        foreach (var check in checks.Where(c => !finalFour || c.Name is "Difficulty save and undo" or "Library exit" or "Workspace Library" or "FAD import"))
        {
            var timer = Stopwatch.StartNew();
            try { check.Check(); Console.WriteLine($"PASS {check.Name} ({timer.Elapsed.TotalSeconds:F2}s)"); }
            catch (Exception error) { failed++; Console.WriteLine($"FAIL {check.Name}: {error}"); }
        }
        return failed == 0 ? 0 : 1;
    }
}
