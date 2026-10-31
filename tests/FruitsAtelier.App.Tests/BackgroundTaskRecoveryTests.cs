using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class BackgroundTaskRecoveryTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (bool cancelled in new[] { false, true })
        {
            try { CollectionFault(cancelled); } catch (Exception error) { failures.Add(error); }
            try { LegacyFault(cancelled); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
        Console.WriteLine("PASS background task faults and cancellations retire collection work and restore Legacy transactions");
    }

    private static void CollectionFault(bool cancelled)
    {
        var ui = new Ui(false);
        ui.LoadDocument(Map());
        var before = ui.View.Document.DeepClone();
        var catalog = new OsuCollectionCatalog("previous", "cached", true, []);
        Field("osuCollections").SetValue(ui.View, catalog);
        var field = Field("osuCollectionsTask");
        field.SetValue(ui.View, FailedTask(typeof(OsuCollectionCatalog), cancelled));
        int exceptions = PumpRepeatedly(ui.View, "PumpOsuCollections");
        Check(exceptions == 0 && field.GetValue(ui.View) is null,
            $"Collection {(cancelled ? "cancellation" : "fault")} escaped {exceptions} of 3 pumps or retained its task");
        Check(ui.View.Document.ContentEquals(before) && ui.View.PrepareFileOperation(),
            "Collection failure changed content or blocked saving");
        Check(ReferenceEquals(Field("osuCollections").GetValue(ui.View), catalog), "Collection failure discarded the last usable catalog");
        SaveAndReopen(ui.View);
        ui.Paint(); ui.Paint();
    }

    private static void LegacyFault(bool cancelled)
    {
        var ui = new Ui(false);
        ui.LoadDocument(Map());
        var before = ui.View.Document.DeepClone();
        var history = (EditorHistory)typeof(EditorView).GetProperty("history", Private)!.GetValue(ui.View)!;
        history.Begin("Legacy effects");
        ui.View.Document.Fruits[0].X = 300;
        var field = Field("legacyDropletJob");
        var jobType = field.FieldType;
        var resultType = jobType.GetProperty("Task")!.PropertyType.GenericTypeArguments[0];
        var job = Activator.CreateInstance(jobType, history, history.ActiveTransactionVersion,
            ui.View.Document.DeepClone(), Field("compensateTinyDroplets").GetValue(ui.View)!, L.Language,
            FailedTask(resultType, cancelled));
        field.SetValue(ui.View, job);
        int exceptions = PumpRepeatedly(ui.View, "PumpLegacyDropletRefresh");
        Check(exceptions == 0 && !ui.View.LegacyDropletRefreshBusy && !history.HasActiveTransaction
            && ui.View.Document.ContentEquals(before),
            $"Legacy {(cancelled ? "cancellation" : "fault")} escaped {exceptions} pumps; active transaction={history.HasActiveTransaction}, baseline restored={ui.View.Document.ContentEquals(before)}");
        Check(ui.View.PrepareFileOperation(), "Failed Legacy work blocked saving restored content");
        SaveAndReopen(ui.View);
        history.Begin("Subsequent edit"); ui.View.Document.Fruits[0].X = 200; history.Commit();
        ui.Key('Z', ctrl: true);
        Check(ui.View.Document.ContentEquals(before), "Legacy failure prevented later editing and undo");
        ui.Paint(); ui.Paint();
    }

    private static object FailedTask(Type resultType, bool cancelled)
    {
        string name = cancelled ? nameof(Task.FromCanceled) : nameof(Task.FromException);
        var method = typeof(Task).GetMethods().Single(m => m.Name == name && m.IsGenericMethodDefinition);
        return method.MakeGenericMethod(resultType).Invoke(null,
            [cancelled ? new CancellationToken(true) : new InvalidOperationException("Injected background worker failure")])!;
    }

    private static int PumpRepeatedly(EditorView view, string name)
    {
        int exceptions = 0;
        var pump = typeof(EditorView).GetMethod(name, Private)!;
        for (int i = 0; i < 3; i++)
            try { pump.Invoke(view, null); }
            catch (TargetInvocationException) { exceptions++; }
        return exceptions;
    }

    private static FieldInfo Field(string name) => typeof(EditorView).GetField(name, Private)!;
    private static void SaveAndReopen(EditorView view)
    {
        string directory = Path.Combine("artifacts", "tests", "background-task-recovery");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".catchdiff");
        ProjectSerializer.WriteFile(view.Document, path);
        Check(ProjectSerializer.ReadFile(path).ContentEquals(view.Document), "Recovered content did not survive save and reopen");
    }
    private static MapDocument Map()
    {
        var map = new MapDocument { DurationMs = 5000 };
        map.TimingPoints.Add(new() { TimeMs = 0, BeatLengthMs = 500 });
        map.Fruits.Add(new() { TimeMs = 1000, X = 100 });
        return map;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
