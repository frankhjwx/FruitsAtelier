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
            try { SliderMultiplierFault(cancelled); } catch (Exception error) { failures.Add(error); }
            try { BananaPreviewFault(cancelled); } catch (Exception error) { failures.Add(error); }
        }
        foreach (bool fault in new[] { false, true })
            try { PendingPreviewClosure(fault); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException(failures);
        Console.WriteLine("PASS background task faults, cancellations, validation recovery and pending preview shutdown");
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

    private static void SliderMultiplierFault(bool cancelled)
    {
        var ui = new Ui(false);
        ui.LoadDocument(Map());
        var before = ui.View.Document.DeepClone();
        Field("sliderMultiplierValidationOwner").SetValue(ui.View,
            typeof(EditorView).GetProperty("history", Private)!.GetValue(ui.View));
        Field("sliderMultiplierValidationSnapshot").SetValue(ui.View, before.DeepClone());
        Field("sliderMultiplierValidationCompensation").SetValue(ui.View, Field("compensateTinyDroplets").GetValue(ui.View));
        Field("sliderMultiplierDraft").SetValue(ui.View, 2d);
        var field = Field("sliderMultiplierValidation");
        field.SetValue(ui.View, FailedTask(field.FieldType.GenericTypeArguments[0], cancelled,
            new IOException("Injected slider multiplier worker failure")));
        int exceptions = PumpRepeatedly(ui.View, "CompleteSliderMultiplierValidation");
        Check(exceptions == 0 && !ui.View.SliderMultiplierValidationBusy && field.GetValue(ui.View) is null,
            $"Multiplier {(cancelled ? "cancellation" : "fault")} escaped {exceptions} pumps or retained/restarted validation");
        Check(ui.View.Document.ContentEquals(before) && ui.View.PrepareFileOperation(),
            "Failed multiplier validation changed content or blocked saving");
        Check(((string)Field("timingError").GetValue(ui.View)!).Length > 0, "Failed multiplier validation did not report its error");
        SaveAndReopen(ui.View);
        ui.Paint(); ui.Paint();
    }

    private static void BananaPreviewFault(bool cancelled)
    {
        var ui = new Ui(false);
        var map = Map();
        map.BananaShowers.Add(new() { TimeMs = 2000, EndTimeMs = 4000 });
        ui.LoadDocument(map);
        var before = ui.View.Document.DeepClone();
        typeof(EditorView).GetProperty(nameof(EditorView.BananaEditVisible))!.SetValue(ui.View, true);
        Field("bananaEditSnapshot").SetValue(ui.View, before.DeepClone());
        Field("bananaEditTargets").SetValue(ui.View, new[] { before.BananaShowers[0].Id });
        Field("bananaEditRequested").SetValue(ui.View, Field("bananaEditPattern").GetValue(ui.View));
        var field = Field("bananaEditJob");
        field.SetValue(ui.View, FailedTask(field.FieldType.GenericTypeArguments[0], cancelled));
        int exceptions = PumpRepeatedly(ui.View, "PumpBananaPreview");
        Check(exceptions == 0 && field.GetValue(ui.View) is null
            && ((string)Field("bananaEditError").GetValue(ui.View)!).Length > 0,
            $"Banana preview {(cancelled ? "cancellation" : "fault")} escaped {exceptions} pumps or restarted its task");
        Check(ui.View.Document.ContentEquals(before), "Failed banana preview changed authoring content");
        typeof(EditorView).GetMethod("CloseBananaEdit", Private)!.Invoke(ui.View, null);
        Check(ui.View.PrepareFileOperation(), "Failed banana preview prevented closing its dialog and saving");
        SaveAndReopen(ui.View);
        ui.Paint(); ui.Paint();
    }

    private static void PendingPreviewClosure(bool fault)
    {
        var view = new EditorView(false);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Field("libraryPreviewSave").SetValue(view, pending.Task);
        using var entered = new ManualResetEventSlim();
        Task? flushing = null;
        var close = Task.Run(() => { entered.Set(); flushing = view.CompleteLibraryCacheWritesAsync(); });
        Check(entered.Wait(TimeSpan.FromSeconds(5)), "Preview shutdown probe did not start");
        bool returned = close.Wait(TimeSpan.FromMilliseconds(500));
        bool awaitedPendingWrite = returned && flushing is { IsCompleted: false };
        if (fault) pending.SetException(new IOException("Injected derived preview write failure"));
        else pending.SetResult();
        Check(close.Wait(TimeSpan.FromSeconds(5)), "Preview shutdown probe did not retire");
        Check(returned, "Pending derived preview write blocked the shutdown caller");
        Check(awaitedPendingWrite && flushing!.Wait(TimeSpan.FromSeconds(5)), "Preview shutdown abandoned pending work or failed to observe its completion");
    }

    private static object FailedTask(Type resultType, bool cancelled, Exception? fault = null)
    {
        string name = cancelled ? nameof(Task.FromCanceled) : nameof(Task.FromException);
        var method = typeof(Task).GetMethods().Single(m => m.Name == name && m.IsGenericMethodDefinition);
        return method.MakeGenericMethod(resultType).Invoke(null,
            [cancelled ? new CancellationToken(true) : fault ?? new InvalidOperationException("Injected background worker failure")])!;
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
