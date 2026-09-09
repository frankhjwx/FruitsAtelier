using FruitsAtelier.Core;

if (args.Length == 2 && args[0] == "--slider-corpus") return ImportedSliderCorpus.Run(args[1]);

var tests = new (string Name, Action Run)[]
{
    ("Incremental conversion matches full output across edits, RNG changes and failures", ConversionCacheTests.EditingMatchesFullConversion),
    ("Sparse curve fitting preserves corners and bounds whole-trajectory error", ImportedCurveFitTests.SparseAndBounded),
    ("Batch conversion completes repeats and preserves duration, export and cancellation", ImportedCurveFitTests.BatchPreservesDurationAndCancellation),
    ("Dense imported corners approximate within editable limits without changing duration", ImportedCurveFitTests.DenseCornerFallback),
    ("Timeline coordinate round trips and bounds", CoordinateRoundTrips),
    ("Zoom preserves mouse time at scale limits", ZoomAnchor),
    ("Beat snap quarters, sixths, offsets and midpoint", SnapGrid),
    ("Invalid numeric inputs are rejected", InvalidNumbers),
    ("Bezier parameter is not linear time", NonlinearTime),
    ("Bezier split preserves shape and existing IDs", BezierSplit),
    ("Linear split preserves piecewise trajectory", LinearSplit),
    ("Anchor movement preserves handles and validates neighbours", MoveAnchor),
    ("Handle movement rejects time reversal and X overflow", MoveHandle),
    ("History groups drag updates and preserves identity", HistoryTransactions),
    ("History cancellation, no-op and redo branching", HistoryCancelAndBranch),
    ("Deep clones do not alias nested editing state", CloneIndependence),
    ("Demo fixture is valid and initially populated", DemoFixture),
    ("Document rejects duplicate IDs and out-of-range objects", DocumentValidation),
    ("Approach rate validates endpoints and non-finite values", ApproachRateValidation),
    ("Approach rate participates in cloning and undo transactions", ApproachRateHistory),
    ("Catch preempt uses AR endpoints and legacy fractional quantisation", CatchPreempt),
    ("Catch fall speed preserves gameplay width scale", CatchFallSpeed),
    ("Slider conversion follows legacy events and RNG golden sequence", ConversionTests.LegacyEventsAndRandom),
    ("Slider tick rate remains separate from editor snap and timing offset", ConversionTests.IndependentTickRate),
    ("Bezier tick coordinates come from generated slider arc length", ConversionTests.BezierTickAlignment),
    ("FSlider generation enforces stable's SV=10 limit", ConversionTests.StableSliderVelocityLimit),
    ("Tiny RNG compensation changes geometry and preserves target X", ConversionTests.TinyCompensation),
    ("Tiny boundary constraints remain internal for usable compatibility output", ConversionTests.TinyBoundary),
    ("Numerical sampling limits preserve exact gameplay knots without user diagnostics", ConversionTests.NumericalSamplingLimit),
    ("Whole-parent RNG ordering includes overlapping streams", ConversionTests.CompleteContextRandom),
    ("Legacy tail exclusion and last-tick timing are preserved", ConversionTests.TailRules),
    ("Failed curves preserve valid slider and standalone outputs", ConversionTests.PartialFailure),
    ("Fractional timing compensation uses actual tiny event times", ConversionTests.FractionalTinyTiming),
    ("Local timing snap respects red boundaries and green SV", ImportedMapTests.TimingGrid),
    ("Same-time timing precedence and density limits are explicit", ImportedMapTests.TimingBoundaries),
    ("Imported slider keeps its starting timing across BPM and SV changes", ImportedMapTests.LockedStartTiming),
    ("Imported L/B/P/C paths and declared length semantics", ImportedMapTests.ImportedPaths),
    ("Repeat spans generate fruit and reversed tick positions", ImportedMapTests.RepeatEvents),
    ("Bananas use real spacing and consume four legacy RNG draws", ImportedMapTests.BananaRandom),
    ("Imported metadata and timing survive clone and undo", ImportedMapTests.ImportedHistory),
    ("Mixed Bezier and straight segments share one generated slider", EditableSliderTests.MixedSegments),
    ("Mixed-segment splitting preserves shape and validates active handles", EditableSliderTests.MixedSplitAndValidation),
    ("Editable repeats preserve one parent and one fruit per turnaround", EditableSliderTests.RepeatAuthoring),
    ("Conflicting repeated tiny targets use a silent compatibility fallback", EditableSliderTests.RepeatTinyConflict),
    ("Legacy path families convert to FSliders without losing repeat events", EditableSliderTests.ImportToEditable),
    ("Imported editing is undoable and failures preserve source state", EditableSliderTests.ConversionHistoryAndFailure),
    ("Corner insertion clears inactive line handles without touching other segments", CurvePointEditingTests.InsertLinearCorner),
    ("Bezier corner insertion retains split neighbour handles", CurvePointEditingTests.InsertBezierCorner),
    ("Corner conversion preserves active neighbour handles and derives segment types", CurvePointEditingTests.CornerKeepsNeighbourHandles),
    ("Curved conversion creates a tangent without reviving dormant handles", CurvePointEditingTests.CurvedPointClearsDormantHandles),
    ("Point tangents remain valid at X boundaries and saturated time controls", CurvePointEditingTests.BoundaryTangents),
    ("Rejected control-point edits leave the original track unchanged", CurvePointEditingTests.InvalidOperationsAreAtomic),
    ("Control-point history preserves repeat, samples and inserted IDs", CurvePointEditingTests.HistoryPreservesRepeatAndMetadata),
    ("Removing a mixed-segment point preserves active handles and clears dormant values", CurvePointEditingTests.RemoveMixedPoint),
    ("Point removal rejects endpoints atomically and preserves undo metadata", CurvePointEditingTests.RemoveRejectionAndHistory),
    ("Batch deletion preserves untouched segment kinds and dormant handles", BatchPointTests.MixedBatchPreservesUntouchedSegments),
    ("One batch can remove endpoints and interiors while deriving a new line", BatchPointTests.EndpointBatchAndNewLine),
    ("Trimming endpoints clears only their now-unused handles", BatchPointTests.EndpointOnlyPreservesAdjacentState),
    ("Empty and duplicate selections are safe and invalid batches are atomic", BatchPointTests.EmptyDuplicatesAndFailures),
    ("Batch point deletion is one undo step preserving repeat and source IDs", BatchPointTests.BatchHistoryPreservesIdentityAndRepeat),
    ("English fallback and localized format arguments remain valid", LocalizationTests.FallbackAndFormats),
    ("Language tables reject malformed values and report missing placeholders", LocalizationTests.InvalidTablesAndPlaceholders),
    ("Adding a language table automatically discovers its language", LocalizationTests.LanguageDiscovery),
    ("Embedded languages are complete and switching preserves message arguments", LocalizationTests.EmbeddedCatalogAndSwitching)
};
if (args.Contains("--fixtures")) tests = tests.Append(("User map fixtures preserve parent totals and convert every object", ImportedMapTests.RealFixtures)).ToArray();
if (args.Contains("--fixtures")) tests = tests.Append(("Real imported sliders become editable with measured geometry changes", EditableSliderTests.RealFixtures)).ToArray();

int failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static void CoordinateRoundTrips()
{
    foreach (double scale in new[] { 0.015, 0.08, 0.4, 1.5 })
    {
        var view = new TimelineTransform(217, 740, 639, 1234.5, scale);
        foreach (double x in new[] { 0.0, 127.3, 256, 512 })
        foreach (double time in new[] { -250.0, 0, 1234.5, 29999.75 })
        {
            var screen = view.ToScreen(new(time, x));
            var result = view.ToMap(screen.X, screen.Y);
            Near(time, result.TimeMs); Near(x, result.X);
        }
        Near(217, view.ToScreen(new(1234.5, 0)).X);
        Near(856, view.ToScreen(new(1234.5, 512)).X);
        Near(740, view.ToScreen(new(1234.5, 512)).Y);
        True(view.ToScreen(new(1734.5, 256)).Y < 740, "Later time must appear above the visible start.");
        True(view.ToMap(400, 700).TimeMs > 1234.5, "A point above the bottom must map to later time.");
    }
}

static void ZoomAnchor()
{
    var view = new TimelineTransform(200, 740, 700, 800, 0.1);
    double time = view.ToMap(320, 475).TimeMs;
    foreach (double factor in new[] { 1.5, 1.5, 0.5, 1000, 0.0001, 2.0 })
    {
        view.ZoomAt(475, factor);
        Near(time, view.ToMap(320, 475).TimeMs);
        True(view.PixelsPerMs is >= 0.015 and <= 16, "Zoom exceeded limits.");
    }
}

static void SnapGrid()
{
    Near(125, BeatGrid.Snap(124, 0, 500, 4));
    Near(125, BeatGrid.Snap(62.5, 0, 500, 4));
    Near(0, BeatGrid.Snap(-62.5, 0, 500, 4));
    Near(-125, BeatGrid.Snap(-62.5001, 0, 500, 4));
    Near(80, BeatGrid.Snap(30, -20, 600, 6));
    Near(37.25 + 500.0 / 3, BeatGrid.Snap(210, 37.25, 500, 6));
    double indexed = 37.25 + 1_000_000 * (500.0 / 6);
    Near(indexed, BeatGrid.Snap(indexed + 20, 37.25, 500, 6));
    foreach (int divisor in new[] { 5, 7, 8, 9, 12, 16 })
        Near(500.0 / divisor * 3, BeatGrid.Snap(500.0 / divisor * 3 + 1, 0, 500, divisor));
}

static void InvalidNumbers()
{
    Throws<ArgumentOutOfRangeException>(() => BeatGrid.Snap(double.NaN, 0, 500, 4));
    Throws<ArgumentOutOfRangeException>(() => BeatGrid.Snap(0, 0, 0, 4));
    Throws<ArgumentOutOfRangeException>(() => BeatGrid.Snap(0, 0, 500, 0));
    Throws<ArgumentOutOfRangeException>(() => BeatGrid.Snap(0, double.PositiveInfinity, 500, 4));
    Throws<InvalidOperationException>(() => new TimelineTransform(0, 0, 0, 0, 1).ToMap(1, 1));
    var curve = NonlinearCurve();
    True(!CurveMath.TryMoveAnchor(curve, curve.Nodes[0].Id, double.NaN, 10, out _), "NaN anchor accepted.");
    True(!CurveMath.TryMoveHandle(curve, curve.Nodes[0].Id, false, new(0, double.PositiveInfinity), out _), "Infinite handle accepted.");
    Near(0, curve.Nodes[0].TimeMs); Near(100, curve.Nodes[0].HandleOut.X);
}

static void NonlinearTime()
{
    var curve = NonlinearCurve();
    var midpoint = CurveMath.Evaluate(curve, 0, 0.5);
    Near(125, midpoint.TimeMs); Near(150, midpoint.X);
    Near(150, CurveMath.PositionAtTime(curve, 125));
    Near(240, CurveMath.PositionAtTime(curve, 512));
    Near(0, CurveMath.PositionAtTime(curve, -1));
    Near(300, CurveMath.PositionAtTime(curve, 1001));
}

static void BezierSplit()
{
    var original = NonlinearCurve(); var split = NonlinearCurve();
    Guid first = split.Nodes[0].Id, last = split.Nodes[1].Id;
    CurveMath.Split(split, 0, 0.37);
    True(split.Nodes.Count == 3 && split.Nodes[0].Id == first && split.Nodes[2].Id == last, "Split replaced endpoint IDs.");
    True(split.Nodes[1].Id != first && split.Nodes[1].Id != last, "Split reused an endpoint ID.");
    for (int i = 0; i <= 100; i++)
    {
        double u = i / 100.0;
        PointNear(CurveMath.Evaluate(original, 0, u * 0.37), CurveMath.Evaluate(split, 0, u));
        PointNear(CurveMath.Evaluate(original, 0, 0.37 + u * 0.63), CurveMath.Evaluate(split, 1, u));
        Near(CurveMath.PositionAtTime(original, i * 10), CurveMath.PositionAtTime(split, i * 10));
    }
    var doc = new MapDocument(); doc.Tracks.Add(split);
    True(CurveMath.Validate(doc).Count == 0, "Split produced invalid control ordering.");
    Throws<ArgumentOutOfRangeException>(() => CurveMath.Split(split, 0, 1e-10));
    True(split.Nodes.Count == 3, "Rejected split changed node count.");
}

static void LinearSplit()
{
    var curve = new CurveTrack { Kind = CurveKind.Linear };
    curve.Nodes.Add(new Anchor { TimeMs = 250, X = 64 });
    curve.Nodes.Add(new Anchor { TimeMs = 1250, X = 464 });
    CurveMath.Split(curve, 0, 0.25);
    Near(500, curve.Nodes[1].TimeMs); Near(164, curve.Nodes[1].X);
    for (int i = 0; i <= 20; i++) Near(64 + i * 20, CurveMath.PositionAtTime(curve, 250 + i * 50));
}

static void MoveAnchor()
{
    var curve = EditableCurve(); var node = curve.Nodes[1];
    MapPoint originalIn = node.HandleIn, originalOut = node.HandleOut;
    True(CurveMath.TryMoveAnchor(curve, node.Id, 1100, 280, out string error), error);
    True(node.HandleIn == originalIn && node.HandleOut == originalOut, "Anchor move changed relative handles.");
    Near(900, node.TimeMs + node.HandleIn.TimeMs); Near(230, node.X + node.HandleIn.X);
    True(!CurveMath.TryMoveAnchor(curve, node.Id, 100, 280, out _), "Time reversal accepted.");
    True(!CurveMath.TryMoveAnchor(curve, node.Id, 1100, 10, out _), "Control point X overflow accepted.");
    Near(1100, node.TimeMs); Near(280, node.X);
    var linear = new CurveTrack { Kind = CurveKind.Linear };
    linear.Nodes.Add(new Anchor { TimeMs = 100, X = 200 });
    linear.Nodes.Add(new Anchor { TimeMs = 101, X = 200 });
    True(!CurveMath.TryMoveAnchor(linear, linear.Nodes[1].Id, 100.0005, 200, out _), "Minimum spacing not enforced.");
    linear.Nodes[0].TimeMs = 1;
    True(CurveMath.TryMoveAnchor(linear, linear.Nodes[1].Id, 1.001, 200, out error), error);
}

static void MoveHandle()
{
    var curve = EditableCurve(); var first = curve.Nodes[0];
    MapPoint initial = first.HandleOut;
    True(!CurveMath.TryMoveHandle(curve, first.Id, false, new(900, 50), out _), "Crossed control times accepted.");
    True(!CurveMath.TryMoveHandle(curve, first.Id, false, new(200, 500), out _), "X overflow accepted.");
    True(!CurveMath.TryMoveHandle(curve, first.Id, true, new(1, 0), out _), "Incoming direction accepted.");
    True(first.HandleOut == initial, "Rejected handle edit changed state.");
    True(CurveMath.TryMoveHandle(curve, first.Id, false, new(400, 100), out string error), error);
    True(first.HandleOut == new MapPoint(400, 100), "Valid edit was not applied.");
}

static void HistoryTransactions()
{
    var history = new EditorHistory(DemoMap.Create());
    Guid fruitId = history.Document.Fruits[0].Id, anchorId = history.Document.Tracks[0].Nodes[0].Id;
    double oldX = history.Document.Fruits[0].X;
    history.Begin("Move fruit");
    for (int i = 0; i < 30; i++) history.Document.Fruits[0].X = 200 + i;
    history.Commit();
    True(history.IsDirty && history.CanUndo && history.UndoLabel == "Move fruit", "Transaction not committed.");
    history.Undo();
    Near(oldX, history.Document.Fruits[0].X);
    True(!history.IsDirty && !history.CanUndo && history.CanRedo, "Drag created more than one undo step.");
    True(history.Document.Fruits[0].Id == fruitId && history.Document.Tracks[0].Nodes[0].Id == anchorId, "Undo changed IDs.");
    history.Redo();
    Near(229, history.Document.Fruits[0].X);
    True(history.Document.Fruits[0].Id == fruitId, "Redo changed ID.");
    history.Begin("Remove fruit"); history.Document.Fruits.RemoveAt(0); history.Commit(); history.Undo();
    True(history.Document.Fruits[0].Id == fruitId, "Undo deletion changed ID.");
}

static void HistoryCancelAndBranch()
{
    var history = new EditorHistory(DemoMap.Create());
    double initial = history.Document.Fruits[0].X;
    history.Begin("Cancelled"); history.Document.Fruits[0].X = 300; history.Cancel();
    Near(initial, history.Document.Fruits[0].X);
    True(!history.IsDirty && !history.CanUndo, "Cancelled drag committed.");
    history.Begin("No-op"); history.Document.Fruits[0].X = 300; history.Document.Fruits[0].X = initial; history.Commit();
    True(!history.CanUndo && !history.IsDirty, "No-op created a change.");
    history.Begin("Edit"); history.Document.Fruits[0].X = 300; history.Commit(); history.Undo();
    history.Begin("No-op after undo"); history.Commit();
    True(history.CanRedo, "No-op destroyed redo.");
    history.Begin("Cancel after undo"); history.Document.Fruits[0].X = 400; history.Cancel();
    True(history.CanRedo, "Cancel destroyed redo.");
    history.Begin("New branch"); history.Document.Fruits[0].X = 500; history.Commit();
    True(!history.CanRedo, "New branch retained obsolete redo.");
    history.Reset(DemoMap.Create());
    True(!history.IsDirty && !history.CanUndo && !history.CanRedo, "Reset retained history.");
}

static void CloneIndependence()
{
    var original = DemoMap.Create(); var clone = original.DeepClone();
    clone.Fruits[0].X = 500;
    clone.Tracks[0].Nodes[0].HandleOut = new(10, 10);
    clone.Tracks[1].Nodes.Clear();
    Near(96, original.Fruits[0].X);
    True(original.Tracks[0].Nodes[0].HandleOut == new MapPoint(350, 10), "Handle aliased.");
    True(original.Tracks[1].Nodes.Count == 4, "Node collection aliased.");
    True(original.Fruits[0].Id == clone.Fruits[0].Id, "Clone regenerated IDs.");
}

static void DemoFixture()
{
    var demo = DemoMap.Create();
    Near(30_000, demo.DurationMs); Near(500, demo.BeatLengthMs);
    var errors = CurveMath.Validate(demo);
    True(errors.Count == 0, string.Join("; ", errors));
    True(demo.Fruits.Count(f => f.TimeMs < 6000) >= 8, "Initial viewport lacks fruit.");
    True(demo.Tracks.Count(t => t.Nodes[^1].TimeMs <= 6000) >= 2, "Initial viewport lacks complete curves.");
    True(demo.Tracks.Any(t => t.Kind == CurveKind.Linear) && demo.Tracks.Any(t => t.Kind == CurveKind.Bezier), "Demo lacks both curve types.");
}

static void DocumentValidation()
{
    var doc = DemoMap.Create();
    doc.Fruits[1].Id = doc.Fruits[0].Id;
    doc.Fruits[2].TimeMs = doc.DurationMs + 1;
    doc.Tracks[0].Nodes[0].X = double.NaN;
    True(CurveMath.Validate(doc).Count >= 3, "Invalid model was accepted.");
}

static void ApproachRateValidation()
{
    var doc = new MapDocument();
    Near(8, doc.ApproachRate);
    foreach (double valid in new[] { 0.0, 4.25, 5, 8.55, 10 })
    {
        doc.ApproachRate = valid;
        True(CurveMath.Validate(doc).Count == 0, $"AR {valid} was rejected.");
    }
    foreach (double invalid in new[] { -0.001, 10.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        doc.ApproachRate = invalid;
        True(CurveMath.Validate(doc).Count > 0, $"AR {invalid} was accepted.");
    }
}

static void ApproachRateHistory()
{
    var original = new MapDocument { ApproachRate = 8.55 };
    var clone = original.DeepClone();
    Near(8.55, clone.ApproachRate);
    clone.ApproachRate = 4.25;
    Near(8.55, original.ApproachRate);

    var history = new EditorHistory(original);
    history.Begin("Change AR");
    history.Document.ApproachRate = 9.1;
    history.Commit();
    True(history.IsDirty && history.CanUndo, "AR edit was not tracked.");
    history.Undo();
    Near(8.55, history.Document.ApproachRate);
    True(!history.IsDirty, "AR undo did not restore clean state.");
    history.Redo();
    Near(9.1, history.Document.ApproachRate);
    history.Begin("Cancel AR");
    history.Document.ApproachRate = 0;
    history.Cancel();
    Near(9.1, history.Document.ApproachRate);
    history.Undo();
    history.Begin("No-op AR");
    history.Document.ApproachRate = 8.55;
    history.Commit();
    True(!history.IsDirty && !history.CanUndo && history.CanRedo, "No-op AR changed history.");
}

static void CatchPreempt()
{
    Near(1800, CatchScrollTiming.PreemptMs(0));
    Near(1200, CatchScrollTiming.PreemptMs(5));
    Near(750, CatchScrollTiming.PreemptMs(8));
    Near(450, CatchScrollTiming.PreemptMs(10));
    Near(1500, CatchScrollTiming.PreemptMs(2.5));
    Near(825, CatchScrollTiming.PreemptMs(7.5));
    Near(1201, CatchScrollTiming.PreemptMs(4.99));
    Near(1198, CatchScrollTiming.PreemptMs(5.01));
    Near(667, CatchScrollTiming.PreemptMs(8.55));
    // 9.1 becomes 9.100000381469727 as float, making preempt slightly below 585 before truncation.
    Near(584, CatchScrollTiming.PreemptMs(9.1));
    foreach (double invalid in new[] { -0.001, 10.001, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Throws<ArgumentOutOfRangeException>(() => CatchScrollTiming.PreemptMs(invalid));
}

static void CatchFallSpeed()
{
    Near(440.0 / 750, CatchScrollTiming.PixelsPerMs(8, 512));
    Near(880.0 / 450, CatchScrollTiming.PixelsPerMs(10, 1024));
    Near(220.0 / 750, CatchScrollTiming.PixelsPerMs(8, 256));
    foreach (double width in new[] { 180.0, 512, 943.5, 1024 })
    {
        double speed = CatchScrollTiming.PixelsPerMs(8, width);
        Near(440 * width / 512, speed * CatchScrollTiming.PreemptMs(8));
        var view = new TimelineTransform(0, 800, width, 1000, speed);
        var first = view.ToScreen(new(1000, 100));
        var next = view.ToScreen(new(1250, 228));
        Near((440.0 / 3) / 128, (first.Y - next.Y) / (next.X - first.X));
    }
    foreach (double invalidWidth in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
        Throws<ArgumentOutOfRangeException>(() => CatchScrollTiming.PixelsPerMs(8, invalidWidth));
    Throws<ArgumentOutOfRangeException>(() => CatchScrollTiming.PixelsPerMs(double.NaN, 512));
}

static CurveTrack NonlinearCurve()
{
    var curve = new CurveTrack { Kind = CurveKind.Bezier };
    curve.Nodes.Add(new Anchor { TimeMs = 0, X = 0, HandleOut = new(0, 100) });
    curve.Nodes.Add(new Anchor { TimeMs = 1000, X = 300, HandleIn = new(-1000, -100) });
    return curve;
}

static CurveTrack EditableCurve()
{
    var curve = new CurveTrack { Kind = CurveKind.Bezier };
    curve.Nodes.Add(new Anchor { TimeMs = 0, X = 100, HandleOut = new(200, 50) });
    curve.Nodes.Add(new Anchor { TimeMs = 1000, X = 300, HandleIn = new(-200, -50), HandleOut = new(200, 0) });
    curve.Nodes.Add(new Anchor { TimeMs = 2000, X = 200, HandleIn = new(-200, 0) });
    return curve;
}

static void Near(double expected, double actual, double tolerance = 1e-7)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        throw new Exception($"Expected {expected:R}, got {actual:R} (tolerance {tolerance}).");
}

static void PointNear(MapPoint expected, MapPoint actual) { Near(expected.TimeMs, actual.TimeMs); Near(expected.X, actual.X); }
static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
