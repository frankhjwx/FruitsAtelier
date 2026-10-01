using FruitsAtelier.Core;

internal static class SynchronizationTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Sync: FA breaks bookmarks and timing write back without exporting notes", () => Run(f =>
        {
            var entry = f.Session.Manifest.Difficulties[0];
            string objects = string.Join('\n', WorkspaceSynchronization.ObjectLines(File.ReadAllText(f.Source)));
            OsuTimeline.AddBreak(f.Diff.Document, 4000, 10000);
            OsuTimeline.AddBookmark(f.Diff.Document, 5000);
            f.Diff.Document.TimingPoints.Add(new TimingPoint { TimeMs = 500, BeatLengthMs = -100, Uninherited = false });
            void Synchronize()
            {
                var merge = f.Merge();
                Check(!merge.RequiresResolution && merge.LocalFieldUpdates.Count > 0, "FA changes need no choice");
                var candidate = WorkspaceSynchronization.WriteLocalFields(f.Session, entry, merge);
                WorkspaceSynchronization.Accept(f.Session, entry, candidate, f.Diff.Document, true, writtenFields: merge.LocalFieldUpdates);
                Check(!f.Merge().RequiresResolution && f.Merge().LocalFieldUpdates.Count == 0, "write advances the baseline");
                Check(string.Join('\n', WorkspaceSynchronization.ObjectLines(File.ReadAllText(f.Source))) == objects, "notes stay byte-equivalent");
            }
            Synchronize();
            var history = new EditorHistory(f.Diff.Document);
            history.Begin("note in break");
            history.Document.Fruits.Add(new Fruit { TimeMs = 7000, X = 200 });
            history.Commit(); f.Diff.Document = history.Document;
            Check(OsuTimeline.Breaks(f.Diff.Document).Count == 2, "note splits the break");
            Synchronize();
            history.Undo(); f.Diff.Document = history.Document; Synchronize();
            Check(OsuTimeline.Breaks(OsuBeatmapReader.ReadFile(f.Source)).Single() == new BreakPeriod(4000, 10000), "undo writes back the restored break");
            history.Redo(); f.Diff.Document = history.Document; Synchronize();
            File.WriteAllText(f.Source, File.ReadAllText(f.Source).Replace("2,4000,", "2,4100,"));
            Check(f.Merge().RequiresResolution, "external break edits remain reviewable");
        }));
        yield return ("Sync: authored timing write excludes SV from unexported curves", () => Run(f =>
        {
            var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 3500, X = 400 }]);
            f.Diff.Document.Tracks.Add(track);
            f.Diff.Document.TimingPoints.Add(new TimingPoint { TimeMs = 500, BeatLengthMs = -100, Uninherited = false });
            var merge = f.Merge();
            var candidate = WorkspaceSynchronization.WriteLocalFields(f.Session, f.Session.Manifest.Difficulties[0], merge);
            Check(candidate.Document.TimingPoints.Count == 2 && candidate.Document.TimingPoints[1].TimeMs == 500,
                "only authored green is written; new curve SV stays local");
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], candidate, f.Diff.Document, true, writtenFields: merge.LocalFieldUpdates);
            Check(!f.Merge().RequiresResolution, "generated timing mismatch does not prompt after write-back");
        }));
        yield return ("Sync: local break write rejects an external save after comparison", () => Run(f =>
        {
            OsuTimeline.AddBreak(f.Diff.Document, 4000, 10000);
            var merge = f.Merge();
            string changed = File.ReadAllText(f.Source).Replace("Title:Title", "Title:external");
            File.WriteAllText(f.Source, changed);
            bool rejected = false;
            try { WorkspaceSynchronization.WriteLocalFields(f.Session, f.Session.Manifest.Difficulties[0], merge); }
            catch (IOException) { rejected = true; }
            Check(rejected && File.ReadAllText(f.Source) == changed, "stale write preserves external bytes");
        }));
        yield return ("Sync: a new FA break edit writes back after a retained decision", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("[HitObjects]", "[Events]\n2,4000,10000\n[HitObjects]"));
            var merge = f.Merge();
            var choices = merge.Conflicts.ToDictionary(c => c.Key, _ => false);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, f.Diff.Document, true, review: merge, choices: choices);
            Check(!f.Merge().RequiresResolution && f.Merge().LocalFieldUpdates.Count == 0, "unchanged retained choice stays pending");
            OsuTimeline.AddBreak(f.Diff.Document, 5000, 9000);
            merge = f.Merge();
            Check(!merge.RequiresResolution && merge.LocalFieldUpdates.Contains("Events/"), "new FA edit replaces the retained difference");
            var candidate = WorkspaceSynchronization.WriteLocalFields(f.Session, f.Session.Manifest.Difficulties[0], merge);
            Check(OsuTimeline.Breaks(candidate.Document).Single() == new BreakPeriod(5000, 9000), "new break is written");
        }));
        yield return ("Sync: uniform timing offset has one summary and keeps generated SV derived", () => Run(f =>
        {
            var output = CaptureCurveTiming(f);
            var shifted = output.ReadBack.DeepClone();
            foreach (var point in shifted.TimingPoints) point.TimeMs += 12;
            string external = OsuBeatmapWriter.Serialize(shifted).Text;
            File.WriteAllText(f.Source, external);
            var merge = f.Merge();
            var conflict = merge.Conflicts.Single(c => c.Key == "TimingPoints/");
            Check(conflict.TimingShift is { OffsetMs: 12, Count: > 1, LocalRemainder.Length: 0, ExternalRemainder.Length: 0 }, "one exact offset summary replaces repeated lines");
            var choices = merge.Conflicts.ToDictionary(c => c.Key, c => c.Key == "TimingPoints/");
            var resolved = WorkspaceSynchronization.Resolve(merge, choices);
            Check(resolved.TimingPoints.Count == f.Diff.Document.TimingPoints.Count
                && resolved.TimingPoints.Zip(f.Diff.Document.TimingPoints).All(pair => pair.First.TimeMs == pair.Second.TimeMs + 12
                    && pair.First.BeatLengthMs == pair.Second.BeatLengthMs), "only authored timing moves; generated SV is not imported");
            choices["TimingPoints/"] = false;
            Check(TimingValues(WorkspaceSynchronization.Resolve(merge, choices).TimingPoints) == TimingValues(f.Diff.Document.TimingPoints), "FA choice keeps timing exact");
            shifted.TimingPoints.Add(new TimingPoint { TimeMs = 6000, BeatLengthMs = -50, Uninherited = false });
            File.WriteAllText(f.Source, OsuBeatmapWriter.Serialize(shifted).Text);
            conflict = f.Merge().Conflicts.Single(c => c.Key == "TimingPoints/");
            Check(conflict.TimingShift is { OffsetMs: 12 } summary && summary.ExternalRemainder.Contains("6000,-50"), "additional edits remain visible beside the offset");
        }));
        yield return ("Sync: sound and combo edits retain exact editable curve handles", () => Run(f =>
        {
            CaptureCurveTiming(f);
            var track = f.Diff.Document.Tracks[0];
            track.Kind = CurveKind.Bezier;
            track.Nodes[0].HandleOut = new(80, 40); track.Nodes[1].HandleIn = new(-80, -20);
            var output = OsuBeatmapWriter.Serialize(f.Diff.Document);
            File.WriteAllText(f.Source, output.Text);
            f.Session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(f.Source, f.Diff.Document, f.Session.Directory,
                output.Text, output.ObjectSources);
            var lines = WorkspaceSynchronization.ObjectLines(output.Text);
            int index = output.ObjectSources.ToList().IndexOf(track.Id);
            string originalLine = output.ReadBack.ImportedSliders.Single(s => s.SourceOrder == index).OriginalLine!;
            var parts = originalLine.Split(',');
            Array.Resize(ref parts, 11);
            parts[3] = "6"; parts[4] = "8"; parts[8] = "8|2"; parts[9] = "2:3|3:2"; parts[10] = "2:3:0:0:";
            string edited = string.Join(',', parts);
            File.WriteAllText(f.Source, output.Text.Replace(originalLine, edited));
            var merge = f.Merge();
            var choices = merge.Conflicts.ToDictionary(c => c.Key, _ => true);
            foreach (var resolved in new[] { WorkspaceSynchronization.Resolve(merge, choices), WorkspaceSynchronization.ResolveExternal(merge) })
            {
                var retained = resolved.Tracks.Single(t => t.Id == track.Id);
                Check(retained.Nodes.Select(n => (n.Id, n.TimeMs, n.X, n.HandleIn, n.HandleOut))
                    .SequenceEqual(track.Nodes.Select(n => (n.Id, n.TimeMs, n.X, n.HandleIn, n.HandleOut))), "anchors and handles stay exact");
                Check(retained.OriginalLine!.Split(',').Skip(8).SequenceEqual(parts.Skip(8)) && ObjectFlags.NewCombo(resolved, track.Id)
                    && ObjectFlags.Sounds(resolved, track.Id).SequenceEqual(new[] { 8, 2 }), "external flags and sample fields are applied");
                Check(WorkspaceSynchronization.ObjectLines(OsuBeatmapWriter.Serialize(resolved).Text).SequenceEqual(WorkspaceSynchronization.ObjectLines(merge.External.Text)),
                    "export reproduces the accepted external attributes");
                var saved = ProjectSerializer.Read(ProjectSerializer.Serialize(resolved, f.Source), f.Source);
                Check(saved.Tracks.Single(t => t.Id == track.Id).Nodes[0].HandleOut == track.Nodes[0].HandleOut, "project round trip retains handles");
            }
            parts[5] = "L|300:192";
            File.WriteAllText(f.Source, output.Text.Replace(originalLine, string.Join(',', parts)));
            merge = f.Merge();
            Check(WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true)).Tracks.All(t => t.Id != track.Id),
                "changed geometry imports the external slider");
        }));
        yield return ("Sync: base SV overrides retain authoring and review the actual exported value", () => Run(f =>
        {
            var document = f.Diff.Document;
            document.OverrideSliderMultiplier = true; SliderMultiplierEditing.Apply(document, 1.3);
            double authoringMultiplier = document.SliderMultiplier;
            var output = OsuBeatmapWriter.Serialize(document);
            File.WriteAllText(f.Source, output.Text);
            f.Session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(f.Source, document, f.Session.Directory,
                output.Text, output.ObjectSources);
            var merge = f.Merge();
            Check(!merge.RequiresResolution, "Unchanged override export has no synchronization conflict");
            var retained = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>());
            Check(retained.SliderMultiplier == authoringMultiplier && retained.SliderMultiplierOverride == 1.3,
                "Keeping the exported value retains the independent authoring base");
            SliderMultiplierEditing.Apply(document, 1.5);
            File.WriteAllText(f.Source, output.Text.Replace("SliderMultiplier:1.3", "SliderMultiplier:1.2"));
            merge = f.Merge();
            Check(merge.Conflicts.Any(c => c.Key == "Difficulty/SliderMultiplier" && c.Local == "1.5" && c.External == "1.2"),
                "Concurrent SV edits review actual local and external values");
            var choices = merge.Conflicts.ToDictionary(c => c.Key, _ => false);
            retained = WorkspaceSynchronization.Resolve(merge, choices);
            Check(retained.SliderMultiplierOverride == 1.5 && retained.SliderMultiplier == authoringMultiplier,
                "Choosing FA retains the confirmed override");
        }));
        yield return ("Sync: one green edit reviews and applies only changed timing amongst generated SV", () => Run(f =>
        {
            var output = CaptureCurveTiming(f);
            var before = f.Diff.Document.DeepClone();
            string green = "500,-100,4,1,0,100,0,0";
            string external = output.Text.Replace("0,500,4,1,0,100,1,0", "0,500,4,1,0,100,1,0\r\n" + green);
            foreach (var point in output.ReadBack.TimingPoints)
                external = external.Replace(point.BeatLengthMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    point.BeatLengthMs.ToString("G15", System.Globalization.CultureInfo.InvariantCulture));
            File.WriteAllText(f.Source, external);
            var merge = f.Merge();
            Check(merge.Conflicts.Count == 1 && merge.Conflicts[0] is { Key: "TimingPoints/", Local: "" }
                && merge.Conflicts[0].External == green, "only the added green line is shown");
            var choices = new Dictionary<string, bool> { ["TimingPoints/"] = true };
            var comparison = WorkspaceSynchronization.CompareWithoutBaseline(before, merge.External, f.Session.Directory, true);
            Check(comparison.Conflicts.Count == 1 && comparison.Conflicts[0].External == green, "baseline-free review also isolates the added green");
            var keptComparison = WorkspaceSynchronization.Resolve(comparison, new Dictionary<string, bool> { ["TimingPoints/"] = false });
            Check(TimingValues(keptComparison.TimingPoints) == TimingValues(before.TimingPoints), "baseline-free FA choice keeps authoring timing");
            var outsideComparison = WorkspaceSynchronization.Resolve(comparison, choices);
            Check(outsideComparison.TimingPoints.Count == before.TimingPoints.Count + 1, "baseline-free osu choice does not import unrelated generated SV");
            var resolved = WorkspaceSynchronization.Resolve(merge, choices);
            Check(resolved.TimingPoints.Count == before.TimingPoints.Count + 1, "unrelated generated greens stay derived");
            Check(TimingValues(resolved.TimingPoints.Where(point => point.TimeMs != 500)) == TimingValues(before.TimingPoints),
                "unrelated authoring timing keeps its exact values");
            Check(resolved.Tracks.Select(t => t.Id).SequenceEqual(before.Tracks.Select(t => t.Id)), "editable curves keep identity");
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, resolved, true, review: merge, choices: choices);
            f.Diff.Document = resolved; WorkspaceProject.Save(f.Session, f.Session.Project); f.Session = WorkspaceProject.Open(f.Session.Directory);
            Check(!f.Merge().RequiresResolution && !f.Session.Manifest.Difficulties[0].Sync!.LocalOverrides.Contains("TimingPoints/"),
                "applied timing does not become a retained generated-SV difference after restart");
            foreach (bool remove in new[] { false, true })
            {
                File.WriteAllText(f.Source, remove ? external.Replace(green + "\r\n", "") : external.Replace(green, green.Replace("-100", "-50")));
                merge = f.Merge();
                Check(merge.Conflicts.Count == 1 && merge.Conflicts[0].Local == green
                    && merge.Conflicts[0].External == (remove ? "" : green.Replace("-100", "-50")), "one changed/deleted green remains one review row");
                resolved = WorkspaceSynchronization.Resolve(merge, choices);
                Check(TimingValues(resolved.TimingPoints.Where(point => point.TimeMs != 500)) == TimingValues(before.TimingPoints),
                    "editing/deleting a green preserves unrelated timing");
                Check(remove ? resolved.TimingPoints.All(t => t.TimeMs != 500) : resolved.TimingPoints.Single(t => t.TimeMs == 500).BeatLengthMs == -50,
                    "external green choice is applied");
            }
        }));
        yield return ("Sync: retained timing additions can be revisited without importing generated SV", () => Run(f =>
        {
            var output = CaptureCurveTiming(f);
            string green = "500,-100,4,1,0,100,0,0";
            File.WriteAllText(f.Source, output.Text.Replace("0,500,4,1,0,100,1,0", "0,500,4,1,0,100,1,0\r\n" + green));
            var merge = f.Merge(); var choices = new Dictionary<string, bool> { ["TimingPoints/"] = false };
            var kept = WorkspaceSynchronization.Resolve(merge, choices);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, kept, true, review: merge, choices: choices);
            f.Diff.Document = kept; WorkspaceProject.Save(f.Session, f.Session.Project); f.Session = WorkspaceProject.Open(f.Session.Directory);
            merge = f.Merge();
            Check(!merge.RequiresResolution && merge.PreviouslyResolved.Contains("TimingPoints/")
                && merge.Conflicts.Single().Local == "" && merge.Conflicts.Single().External == green, "retained choice reviews only the actual addition");
            var outside = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool> { ["TimingPoints/"] = true });
            Check(outside.TimingPoints.Count == kept.TimingPoints.Count + 1 && outside.TimingPoints.Single(t => t.TimeMs == 500).BeatLengthMs == -100,
                "revisiting the retained choice restores the external addition");
        }));
        yield return ("Sync: sample edits on replaced generated SV preserve authored velocity", () => Run(f =>
        {
            var output = CaptureCurveTiming(f, sampleBoundary: true);
            var generated = output.ReadBack.TimingPoints.Single(t => t.TimeMs == 3000);
            Check(generated.BeatLengthMs != -100, "fixture has a replaced authored green");
            string line = generated.OriginalLine!;
            var parts = line.Split(','); parts[5] = "80";
            File.WriteAllText(f.Source, output.Text.Replace(line, string.Join(',', parts)));
            var merge = f.Merge();
            Check(merge.Conflicts.Count == 1 && merge.Conflicts[0].Local.Split('\n').Length == 1
                && merge.Conflicts[0].External.Split('\n').Length == 1, "one sample edit is one changed timing row");
            var resolved = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool> { ["TimingPoints/"] = true });
            var point = resolved.TimingPoints.Single(t => t.TimeMs == 3000);
            Check(point.BeatLengthMs == -100 && point.Volume == 80 && resolved.TimingPoints.Count == f.Diff.Document.TimingPoints.Count,
                "only the edited sample field is transferred to authoring");
        }));
        yield return ("Sync: osu save rewrites do not conflict with storyboard settings", () => Run(f =>
        {
            string before = Fixture().Replace("Mode:2", "Mode:2\nWidescreenStoryboard:0")
                .Replace("[TimingPoints]", "[Events]\n//Background\n0,0,\"bg.jpg\",0,0\n//Break Periods\n//Storyboard\nSprite,Foreground,Centre,\"sprite.png\",320,240\n F,0,0,500,0,1\n2,2100,2900\n[TimingPoints]")
                .Replace("0,500,4,1,0,100,1,0", "0,413.793103448276,4,1,0,100,1,0\n1000,-76.25857146343249,4,1,0,100,0,0")
                .Replace("100,192,1000,1,0", "100,192,1000.82758620691,1,0")
                .Replace("150,192,1500,1,0,0:0:0:0:", "256,192,1500.7,8,0,2000.9,0:0:0:0:")
                .Replace("200,192,2000,1,0", "200,192,3000,1,0");
            File.WriteAllText(f.Source, before);
            f.Diff.Document = OsuBeatmapReader.ReadFile(f.Source);
            f.Session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(f.Source, f.Diff.Document, f.Session.Directory);
            string saved = before.Replace("WidescreenStoryboard:0", "WidescreenStoryboard:1")
                .Replace("//Break Periods", "//Break Periods\nBreak,2100,2900")
                .Replace("2,2100,2900\n[TimingPoints]", "\n[TimingPoints]")
                .Replace("-76.25857146343249", "-76.2585714634325")
                .Replace("1000.82758620691,1", "1000,5")
                .Replace("1500.7,8,0,2000.9", "1500,8,0,2000")
                .Replace("200,192,3000,1", "200,192,3000,5");
            File.WriteAllText(f.Source, saved);
            var merge = f.Merge();
            Check(merge.Conflicts.Select(c => c.Key).SequenceEqual(["General/WidescreenStoryboard"]), "only the changed storyboard setting needs review");
            var resolved = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool> { ["General/WidescreenStoryboard"] = true });
            Check(resolved.Fruits[0].TimeMs == f.Diff.Document.Fruits[0].TimeMs
                && resolved.TimingPoints[1].BeatLengthMs == f.Diff.Document.TimingPoints[1].BeatLengthMs,
                "comparison does not round authoring values");
            Check(resolved.OriginalSections.Single(s => s.Name == "Events").Lines.SequenceEqual(
                f.Diff.Document.OriginalSections.Single(s => s.Name == "Events").Lines), "comparison preserves event source text");
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, resolved, true,
                review: merge, choices: new Dictionary<string, bool> { ["General/WidescreenStoryboard"] = true });
            f.Diff.Document = resolved;
            Check(!f.Merge().RequiresResolution && f.Session.Manifest.Difficulties[0].Sync!.LocalOverrides.Count == 0,
                "save rewrites do not become pending local overrides");
            Check(!WorkspaceSynchronization.CompareWithoutBaseline(resolved, WorkspaceSynchronization.ReadStable(f.Source), f.Session.Directory, true).RequiresResolution,
                "baseline-free comparison uses the same save semantics");
            foreach (var (text, key) in new[] {
                (saved.Replace("Break,2100,2900", "Break,2101,2900"), "Events/"),
                (saved.Replace(" F,0,0,500,0,1", " F,0,0,501,0,1"), "Events/"),
                (saved.Replace("//Background", "Video,0,\"movie.mp4\"\n//Background"), "Events/"),
                (saved.Replace("-76.2585714634325", "-76.2585714634"), "TimingPoints/"),
                (saved.Replace("1000,-76", "1000.1,-76"), "TimingPoints/"),
                (saved.Replace("1000,5", "1001,5"), "$objects:"),
                (saved.Replace("1500,8,0,2000", "1500,8,0,2001"), "$objects:"),
                (saved.Replace("1000,5,0", "1000,21,0"), "$objects:") })
            {
                File.WriteAllText(f.Source, text);
                Check(f.Merge().Conflicts.Any(c => c.Key.StartsWith(key)), "real edit stays visible: " + key);
            }
        }));
        yield return ("Sync: ordinary combo edits and timing order remain visible", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("1500,1,0", "1500,5,0"));
            Check(f.Merge().Conflicts.Count(c => c.Key.StartsWith("$objects:")) == 1, "ordinary new combo edit");
            string before = Fixture().Replace("0,500,4,1,0,100,1,0", "0,500,4,1,0,100,1,0\n0,-50,4,1,0,100,0,0\n0,-100,4,1,0,100,0,0");
            File.WriteAllText(f.Source, before);
            f.Diff.Document = OsuBeatmapReader.ReadFile(f.Source);
            f.Session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(f.Source, f.Diff.Document, f.Session.Directory);
            File.WriteAllText(f.Source, before.Replace("0,-50,4,1,0,100,0,0\n0,-100,4,1,0,100,0,0", "0,-100,4,1,0,100,0,0\n0,-50,4,1,0,100,0,0"));
            Check(f.Merge().Conflicts.Any(c => c.Key == "TimingPoints/"), "same-time timing order affects SV");
        }));
        yield return ("Sync: all section fields support additions, retained choices and deletions", () =>
        {
            foreach (var (section, line, key) in new[] {
                ("General", "PreviewTime:1234", "General/PreviewTime"),
                ("Editor", "Bookmarks:100,200", "Editor/Bookmarks"),
                ("Difficulty", "HPDrainRate:7", "Difficulty/HPDrainRate"),
                ("Metadata", "CustomField:extra", "Metadata/CustomField"),
                ("Colours", "Combo1:10,20,30", "Colours/Combo1"),
                ("Events", "// storyboard\nSprite,Foreground,Centre,\"test.png\",320,240\n F,0,1000,2000,0,1", "Events/"),
                ("CustomSection", "arbitrary:text\nunchanged payload", "CustomSection/") }) Run(f =>
            {
                string addition = "[" + section + "]\n" + line + "\n";
                string added = Fixture().Replace("[HitObjects]", addition + "[HitObjects]");
                File.WriteAllText(f.Source, added);
                var merge = f.Merge();
                Check(merge.Conflicts.Any(c => c.Key == key) && merge.RequiresResolution, key + " addition is reviewable");
                var local = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => false));
                WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, local, true,
                    review: merge, choices: merge.Conflicts.ToDictionary(c => c.Key, _ => false));
                f.Diff.Document = local;
                Check(!f.Merge().RequiresResolution && f.Merge().PreviouslyResolved.Contains(key), key + " local choice remains accepted");
                merge = f.Merge();
                var external = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
                Check(OsuBeatmapWriter.Serialize(external).Text.Contains(line.Replace("\n", "\r\n")), key + " complete text is applied");
                WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, external, true);
                f.Diff.Document = external;
                File.WriteAllText(f.Source, Fixture());
                merge = f.Merge();
                Check(merge.RequiresResolution && merge.Conflicts.Any(c => c.Key == key && c.External == ""), key + " removal is reviewable");
                var deleted = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
                Check(!OsuBeatmapWriter.Serialize(deleted).Text.Contains(line.Split('\n')[0]), key + " removal is applied");
                Check(deleted.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, key + " preserves authoring identity");
            });
        });
        yield return ("Sync: timing choices preserve ordered inherited and uninherited points", () => Run(f =>
        {
            f.Diff.Document.TimingPoints[0].BeatLengthMs = 600;
            File.WriteAllText(f.Source, Fixture().Replace("0,500,4,1,0,100,1,0", "0,400,3,2,1,70,1,1\n0,-50,3,3,2,60,0,0"));
            var merge = f.Merge();
            Check(merge.Conflicts.Any(c => c.Key == "TimingPoints/"), "timing text conflict");
            var local = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => false));
            var external = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(local.TimingPoints.Single().BeatLengthMs == 600, "local timing choice");
            Check(external.TimingPoints.Count == 2 && external.TimingPoints[0].Uninherited && !external.TimingPoints[1].Uninherited
                && external.TimingPoints[1].BeatLengthMs == -50 && external.TimingPoints[1].Volume == 60, "external timing order and fields");
        }));
        yield return ("Sync: osu save precision and omitted slider defaults preserve real edits", () => Run(f =>
        {
            string[] lines = [
                "100,192,1000,2,0,L|180:220,1,129.68676013495843,4|0,0:0|0:0,0:0:0:0:",
                "200,192,2000,2,0,L|280:220,1,209.99999999999994,,,0:0:0:0:",
                "300,192,3000,2,0,L|380:220,1,78.75000300407498,,,0:0:0:0:",
                "400,192,4000,1,2,0:0:0:0:"];
            string prefix = Fixture().Split("[HitObjects]")[0] + "[HitObjects]\n";
            File.WriteAllText(f.Source, prefix + string.Join('\n', lines));
            f.Diff.Document = OsuBeatmapReader.ReadFile(f.Source);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true);
            WorkspaceProject.Save(f.Session, f.Session.Project);
            string[] rewritten = [
                lines[0].Replace("129.68676013495843", "129.686760134958"),
                "200,192,2000,2,0,L|280:220,1,210",
                "300,192,3000,2,0,L|380:220,1,78.750003004075",
                lines[3]];
            File.WriteAllText(f.Source, prefix + string.Join('\n', rewritten));
            Check(f.Merge().Conflicts.Count(c => c.Key.StartsWith("$objects:")) == 0, "save-only precision and default fields create no object conflicts");
            foreach (string changed in new[] {
                rewritten[0].Replace("4|0", "2|0"),
                rewritten[0].Replace("0:0:0:0:", "0:0:0:80:custom.wav"),
                rewritten[0].Replace("0:0|0:0", "1:2|0:0") })
            {
                File.WriteAllText(f.Source, prefix + string.Join('\n', new[] { changed }.Concat(rewritten.Skip(1))));
                Check(f.Merge().Conflicts.Count(c => c.Key.StartsWith("$objects:")) == 1, "non-default sound changes remain visible");
            }
            rewritten[0] = rewritten[0].Replace("180:220", "179:220");
            rewritten[3] = rewritten[3].Replace("400,192", "399,193");
            File.WriteAllText(f.Source, prefix + string.Join('\n', rewritten));
            Check(f.Merge().Conflicts.Count(c => c.Key.StartsWith("$objects:")) == 2, "two actual geometry edits remain exactly two conflicts");
            rewritten[0] = lines[0].Replace("129.68676013495843", "129.686761134958");
            rewritten[3] = lines[3];
            File.WriteAllText(f.Source, prefix + string.Join('\n', rewritten));
            Check(f.Merge().Conflicts.Count(c => c.Key.StartsWith("$objects:")) == 1, "a small real slider length change remains visible");
        }));
        yield return ("Sync: all ten metadata fields require a choice for any differing value", () =>
        {
            foreach (string key in new[] { "Title", "TitleUnicode", "Artist", "ArtistUnicode", "Creator", "Version", "Source", "Tags", "BeatmapID", "BeatmapSetID" })
            foreach (bool localOnly in new[] { false, true }) Run(f =>
            {
                var external = OsuBeatmapReader.ReadFile(f.Source);
                Set(localOnly ? f.Diff.Document : external, key, key.EndsWith("ID") ? "12345" : "Changed value");
                File.WriteAllText(f.Source, OsuBeatmapWriter.Serialize(external).Text);
                var merge = f.Merge();
                Check(merge.RequiresResolution && merge.Conflicts.Any(c => c.Key == "Metadata/" + key), key + " unilateral change requires selection");
                var choices = merge.Conflicts.ToDictionary(c => c.Key, _ => false);
                var kept = WorkspaceSynchronization.Resolve(merge, choices);
                WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, kept, true, review: merge, choices: choices);
                f.Diff.Document = kept;
                var again = f.Merge();
                Check(!again.RequiresResolution && again.PreviouslyResolved.Contains("Metadata/" + key), key + " accepted difference remains resolved");
                Set(external, key, key.EndsWith("ID") ? "67890" : "Changed again");
                File.WriteAllText(f.Source, OsuBeatmapWriter.Serialize(external).Text);
                Check(f.Merge().RequiresResolution, key + " new external edit renews conflict");
            });
        });
        yield return ("Sync: deleting copied difficulties does not steal surviving exact associations", () =>
        {
            foreach (bool copiedIds in new[] { false, true }) Run(f =>
            {
                string survivor = copiedIds ? Fixture().Replace("BeatmapID:0", "BeatmapID:123").Replace("BeatmapSetID:-1", "BeatmapSetID:456") : Fixture();
                File.WriteAllText(f.Source, survivor);
                var copies = new List<string>();
                foreach (string name in new[] { "Test", "Test2" })
                {
                    string path = Path.Combine(f.Set, name + ".osu"); copies.Add(path);
                    string text = survivor.Replace("Version:Rain", "Version:" + name);
                    if (copiedIds) text = text.Replace("100,192,1000", "400,192,1000");
                    File.WriteAllText(path, text);
                    f.Session.Project.Difficulties.AddRange(BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(path)]).Difficulties);
                }
                WorkspaceProject.Save(f.Session, f.Session.Project);
                foreach (string copy in copies) File.Delete(copy);
                f.Session = WorkspaceProject.Open(f.Session.Directory);
                var scan = f.Scan();
                Check(scan.Difficulties[0].Candidate?.Path == f.Source && scan.Difficulties[0].State != WorkspaceSyncState.Duplicate, "survivor keeps its exact association");
                Check(scan.Difficulties.Skip(1).All(s => s.State == WorkspaceSyncState.Missing && s.Candidate is null), "deleted copies remain missing even with copied objects or IDs");
                Check(scan.Additions.Count == 0 && f.Session.Project.Difficulties.Count == 3, "discovery neither imports duplicates nor removes authoring");
            });
        });
        yield return ("Sync: competing inferred rename matches require association rather than duplicate cleanup", () => Run(f =>
        {
            string copy = Path.Combine(f.Set, "Test.osu"); File.WriteAllText(copy, Fixture().Replace("Version:Rain", "Version:Test"));
            f.Session.Project.Difficulties.AddRange(BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(copy)]).Difficulties);
            WorkspaceProject.Save(f.Session, f.Session.Project);
            string moved = Path.Combine(f.Set, "renamed.osu"); File.Move(f.Source, moved); File.Delete(copy);
            var scan = f.Scan();
            Check(scan.Difficulties.All(s => s.State == WorkspaceSyncState.Ambiguous && s.Candidate is null && s.Candidates.SequenceEqual(new[] { moved })), "unproven identity cannot trigger destructive duplicate cleanup");
            Check(scan.Additions.Count == 0, "contested candidate is not imported again");
        }));
        yield return ("Sync: periodic checks stay in associated folders while explicit discovery finds moved maps", () => Run(f =>
        {
            string moved = Path.Combine(f.Songs, "moved"); Directory.CreateDirectory(moved);
            File.Move(f.Source, Path.Combine(moved, "map.osu"));
            File.Move(Path.Combine(f.Set, "audio.mp3"), Path.Combine(moved, "audio.mp3"));
            Check(WorkspaceSynchronization.Scan(f.Session, f.Songs, searchMissing: false).Difficulties.Single().State == WorkspaceSyncState.Missing,
                "periodic check does not search unrelated folders");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Changed, "explicit discovery finds the relocated association");
        }));
        yield return ("Sync: missing difficulty search skips unrelated oversized and invalid maps", () => Run(f =>
        {
            File.Delete(f.Source);
            string other = Path.Combine(f.Songs, "other"); Directory.CreateDirectory(other);
            foreach (string folder in new[] { f.Set, other })
            {
                using (var oversized = File.Create(Path.Combine(folder, "oversized.osu")))
                    oversized.SetLength(OsuBeatmapReader.MaximumFileBytes + 1L);
                File.WriteAllText(Path.Combine(folder, "invalid.osu"), Fixture().Replace("osu file format v14", "osu file format v999"));
            }
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Missing, "unrelated invalid files must not block missing-file resolution");
            using (var oversized = File.Create(f.Source)) oversized.SetLength(OsuBeatmapReader.MaximumFileBytes + 1L);
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Unavailable, "oversized linked file remains unavailable rather than deleted");
        }));
        yield return ("Sync: older accepted baselines recover reviewable retained objects", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].TimeMs = 800;
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "320,192,1800"));
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true);
            f.Session.Manifest.Difficulties[0].Sync!.RetainedObjectsRecorded = false;
            var review = f.Merge();
            Check(!review.RequiresResolution && review.PreviouslyResolved.Count == 1, "older accepted difference is already resolved and reviewable");
            var replaced = WorkspaceSynchronization.Resolve(review, review.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(replaced.Fruits.Count == 3 && replaced.Fruits.Any(o => o.TimeMs == 1800) && replaced.Fruits.All(o => o.TimeMs != 800), "older decision can be changed without duplicate objects");
        }));
        yield return ("Sync: retained fields can be reviewed again without becoming new conflicts", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "FA title");
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External title"));
            var first = f.Merge(); var decisions = first.Conflicts.ToDictionary(c => c.Key, _ => false);
            var kept = WorkspaceSynchronization.Resolve(first, decisions);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], first.External, kept, true, review: first, choices: decisions);
            f.Diff.Document = kept;
            var review = f.Merge();
            Check(!review.RequiresResolution && review.PreviouslyResolved.Contains("Metadata/Title"), "resolved field remains reviewable");
            Check(OsuBeatmapReader.Setting(WorkspaceSynchronization.Resolve(review, new Dictionary<string, bool>()), "Metadata", "Title") == "FA title", "automatic updates retain the choice");
            Check(OsuBeatmapReader.Setting(WorkspaceSynchronization.Resolve(review, new Dictionary<string, bool> { ["Metadata/Title"] = true }), "Metadata", "Title") == "External title", "field can be resolved again");
        }));
        yield return ("Sync: retained FA objects track repeated external time moves across anchors after restart", () => Run(f =>
        {
            Guid id = f.Diff.Document.Fruits[0].Id;
            f.Diff.Document.Fruits[0].TimeMs = 800;
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "300,192,1800"));
            var first = f.Merge(); var decisions = first.Conflicts.ToDictionary(c => c.Key, _ => false);
            var kept = WorkspaceSynchronization.Resolve(first, decisions);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], first.External, kept, true, review: first, choices: decisions);
            f.Diff.Document = kept; WorkspaceProject.Save(f.Session, f.Session.Project);
            f.Session = WorkspaceProject.Open(f.Session.Directory);
            var history = f.Merge();
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Current && !history.RequiresResolution && history.PreviouslyResolved.Count == 1,
                "resolved difference remains reviewable without requiring another decision");
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "320,192,1900"));
            var second = f.Merge(); var conflict = second.Conflicts.Single(c => c.Key.StartsWith("$objects:"));
            Check(second.WasPreviouslyRetained(conflict.Key) && second.ConflictSources(conflict.Key, false).SetEquals(new[] { id }), "retained identity survives time changes");
            var accepted = WorkspaceSynchronization.Resolve(second, second.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(accepted.Fruits.Count == 3 && accepted.Fruits.All(o => o.Id != id) && accepted.Fruits.Any(o => o.TimeMs == 1900), "external replaces the retained local object instead of duplicating it");
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], second.External, accepted, true, review: second, choices: second.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(f.Session.Manifest.Difficulties[0].Sync!.RetainedObjects.Count == 0, "external choice clears the previous local decision");
        }));
        yield return ("Sync: ignored external additions conflict again without claiming unrelated FA objects", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture() + "\n320,192,2300,1,0,0:0:0:0:\n");
            var first = f.Merge(); var decisions = first.Conflicts.ToDictionary(c => c.Key, _ => false);
            var kept = WorkspaceSynchronization.Resolve(first, decisions);
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], first.External, kept, true, review: first, choices: decisions);
            f.Diff.Document = kept;
            File.WriteAllText(f.Source, Fixture() + "\n340,192,2400,1,0,0:0:0:0:\n");
            var second = f.Merge(); var conflict = second.Conflicts.Single(c => c.Key.StartsWith("$objects:"));
            Check(second.WasPreviouslyRetained(conflict.Key) && second.ConflictSources(conflict.Key, false).Count == 0, "previously rejected insertion is explicit");
            var accepted = WorkspaceSynchronization.Resolve(second, second.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(accepted.Fruits.Count == 4 && kept.Fruits.All(o => accepted.Fruits.Any(a => a.Id == o.Id)), "unrelated FA objects survive accepting the new addition");
        }));
        yield return ("Sync: baseline-free deletions expose an absent side and retain unrelated identities", () => Run(f =>
        {
            string text = string.Join('\n', OsuBeatmapWriter.Serialize(f.Diff.Document).Text.Split('\n').Where(line => !line.StartsWith("100,192,1000,")));
            File.WriteAllText(f.Source, text);
            var merge = WorkspaceSynchronization.CompareWithoutBaseline(f.Diff.Document, WorkspaceSynchronization.ReadStable(f.Source), f.Session.Directory, true);
            var conflict = merge.Conflicts.Single(c => c.Key.StartsWith("$objects:"));
            Check(merge.ConflictSources(conflict.Key, false).Contains(f.Diff.Document.Fruits[0].Id) && merge.ConflictSources(conflict.Key, true).Count == 0, "absent counterpart");
            var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Fruits.Count == 2 && result.Fruits.Any(o => o.Id == f.Diff.Document.Fruits[1].Id), "deletion applies independently");
        }));
        yield return ("Sync: no baseline supports independent field and ordered object choices", () => Run(f =>
        {
            string text = OsuBeatmapWriter.Serialize(f.Diff.Document).Text.Replace("Title:Title", "Title:External")
                .Replace("100,192,1000", "300,192,1000").Replace("150,192,1500", "350,192,1500");
            File.WriteAllText(f.Source, text);
            var merge = WorkspaceSynchronization.CompareWithoutBaseline(f.Diff.Document, WorkspaceSynchronization.ReadStable(f.Source), f.Session.Directory, true);
            Check(merge.Conflicts.Count == 3, "one field and two ordered objects");
            Reject(() => WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>()));
            var choices = merge.Conflicts.ToDictionary(c => c.Key, c => c.Key == "Metadata/Title" || c.Key == "$objects:1");
            var result = WorkspaceSynchronization.Resolve(merge, choices);
            Check(OsuBeatmapReader.Setting(result, "Metadata", "Title") == "External", "external field chosen");
            Check(result.Fruits.Any(o => o.Id == f.Diff.Document.Fruits[0].Id && o.X == 100)
                && result.Fruits.Any(o => o.TimeMs == 1500 && o.X == 350), "mixed object choices");
        }));
        yield return ("Sync: deleting a stale association requires resolving an external rename first", () => Run(f =>
        {
            string renamed = Path.Combine(f.Set, "renamed.osu"); File.Move(f.Source, renamed);
            Reject(() => WorkspaceAssociations.DeleteDifficulty(f.Session, f.Session.Project, f.Diff.Id));
            Check(File.Exists(renamed) && WorkspaceProject.Open(f.Session.Directory).Project.Difficulties.Count == 1, "neither side removed from stale association");
        }));
        yield return ("Sync: choosing the complete FA version retains unchanged local fields against future external edits", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External"));
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true, retainLocalFields: true);
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External again"));
            Check(f.Merge().Conflicts.Any(c => c.Key == "Metadata/Title"), "explicit full FA choice stays pending");
        }));
        yield return ("Sync: source changes between reading and baseline capture cannot become the baseline", () => Run(f =>
        {
            var imported = OsuBeatmapReader.Read(File.ReadAllText(f.Source), f.Source);
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:Changed during import"));
            bool rejected = false;
            try { WorkspaceSynchronization.Capture(f.Source, imported, f.Session.Directory); }
            catch (IOException) { rejected = true; }
            Check(rejected, "concurrent source edit rejected");
        }));
        yield return ("Sync: tied-time external insertion preserves output order", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000,1,0,0:0:0:0:", "300,192,1000,1,0,0:0:0:0:\n100,192,1000,1,0,0:0:0:0:"));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            var output = OsuBeatmapWriter.Serialize(result);
            Check(WorkspaceSynchronization.ObjectLines(output.Text).Take(2).Select(l => l.Split(',')[0]).SequenceEqual(new[] { "300", "100" }), "same-time ordering");
        }));
        yield return ("Sync: independent local additions survive external object deletion", () => Run(f =>
        {
            var added = new Fruit { TimeMs = 3000, X = 321 }; f.Diff.Document.Fruits.Add(added);
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000,1,0,0:0:0:0:\n", ""));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Fruits.Count == 3 && result.Fruits.Any(o => o.Id == added.Id), "local addition retained: " + string.Join(";", result.Fruits.Select(o => $"{o.TimeMs}:{o.X}:{o.Id == added.Id}")));
        }));
        yield return ("Sync: interrupted deletion rolls back the external removal", () => Run(f =>
        {
            string backup = WorkspaceSynchronization.Archive(f.Session, "delete-test");
            File.Copy(f.Source, Path.Combine(backup, "external.osu"));
            string journal = System.Text.Json.JsonSerializer.Serialize(new { DifficultyId = f.Diff.Id, Path = f.Source, Backup = backup, Hash = WorkspaceProject.Hash(f.Source) });
            File.WriteAllText(Path.Combine(f.Session.Directory, "delete.json"), journal); File.Delete(f.Source);
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            Check(File.Exists(f.Source) && reopened.Project.Difficulties[0].Id == f.Diff.Id, "both sides restored");
        }));
        yield return ("Sync: two legacy difficulties in one project require one retained owner", () => Run(f =>
        {
            var copy = new ProjectDifficulty { Name = f.Diff.Name, Document = f.Diff.Document.DeepClone() };
            f.Session.Project.Difficulties.Add(copy);
            var entry = f.Session.Manifest.Difficulties[0];
            f.Session.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = copy.Id, Name = copy.Name, Source = f.Source, SourceHash = entry.SourceHash, Sync = entry.Sync });
            WorkspaceProject.Save(f.Session, f.Session.Project);
            Check(f.Scan().Difficulties.All(s => s.State == WorkspaceSyncState.Duplicate), "all duplicate owners blocked");
            var keep = WorkspaceAssociations.Claims(f.Workspace).Single(c => c.DifficultyId == copy.Id);
            var result = WorkspaceAssociations.KeepOnly(f.Session, f.Session.Project, f.Source, keep);
            Check(result.Project.Difficulties.Single().Id == copy.Id && File.Exists(f.Source), "selected owner only");
        }));
        yield return ("Sync: metadata rename and audio replacement can happen together", () => Run(f =>
        {
            string folder = Path.Combine(f.Songs, "new set"); Directory.Move(f.Set, folder);
            string path = Path.Combine(folder, "new.osu"); File.Move(Path.Combine(folder, "map.osu"), path);
            File.Delete(Path.Combine(folder, "audio.mp3")); File.WriteAllText(Path.Combine(folder, "new.mp3"), "different recording");
            File.WriteAllText(path, Fixture().Replace("audio.mp3", "new.mp3").Replace("Title:Title", "Title:New").Replace("Artist:Artist", "Artist:New").Replace("Creator:Mapper", "Creator:New"));
            var status = f.Scan().Difficulties.Single(); var result = f.Resolve(status.Candidate!);
            Check(result.AudioPath == Path.Combine(folder, "new.mp3") && result.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, "joint rename and replacement");
        }));
        yield return ("Sync: timestamp and size equality cannot hide a metadata change", () => Run(f =>
        {
            var stamp = File.GetLastWriteTimeUtc(f.Source);
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:Other")); File.SetLastWriteTimeUtc(f.Source, stamp);
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Changed, "content check");
        }));
        yield return ("Sync: a truncated live source is unavailable rather than deleted", () => Run(f =>
        {
            File.WriteAllText(f.Source, "osu file format v14\n");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Unavailable, "incomplete save must not relink or delete");
        }));
        yield return ("Sync: missing referenced audio requires explicit repair", () => Run(f =>
        {
            File.Delete(Path.Combine(f.Set, "audio.mp3"));
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.AudioMissing, "audio missing");
        }));
        yield return ("Sync: matching metadata changes on both sides need no choice", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "Same"); File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:Same"));
            Check(f.Merge().Conflicts.Count == 0, "same value");
        }));
        yield return ("Sync: accepted local metadata stays pending through later external edits", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "Local"); File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External"));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => false));
            WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], merge.External, result, true); f.Diff.Document = result;
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:External again"));
            Check(f.Merge().Conflicts.Any(c => c.Key == "Metadata/Title"), "local choice cannot be silently overwritten later");
        }));
        yield return ("Sync: external timing and difficulty settings update while retaining object IDs", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("0,500,4", "0,400,3").Replace("CircleSize:5", "CircleSize:6"));
            var result = f.Resolve(WorkspaceSynchronization.ReadStable(f.Source));
            Check(result.TimingPoints[0].BeatLengthMs == 400 && result.TimingPoints[0].Meter == 3 && result.CircleSize == 6, "settings applied");
            Check(result.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, "object retained");
        }));
        yield return ("Sync: exported curves keep anchors and handles after metadata updates", () => Run(f =>
        {
            var track = new CurveTrack { Kind = CurveKind.Bezier };
            track.Nodes.AddRange([new() { TimeMs = 3000, X = 100, HandleOut = new(100, 20) }, new() { TimeMs = 4000, X = 250, HandleIn = new(-100, -20) }]);
            f.Diff.Document.Tracks.Add(track);
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true); WorkspaceExport.Commit(f.Session, plan);
            File.WriteAllText(f.Source, File.ReadAllText(f.Source).Replace("Creator:Mapper", "Creator:Uploaded mapper"));
            var result = f.Resolve(WorkspaceSynchronization.ReadStable(f.Source));
            Check(result.Tracks.Single().Id == track.Id && result.Tracks[0].Nodes[0].Id == track.Nodes[0].Id
                && result.Tracks[0].Nodes[0].HandleOut == track.Nodes[0].HandleOut, "curve authoring retained");
        }));
        yield return ("Sync: changing one stream output resolves its complete authoring group", () => Run(f =>
        {
            var track = new CurveTrack { Kind = CurveKind.Linear, StreamSnapDivisor = 4 };
            track.Nodes.AddRange([new() { TimeMs = 3000, X = 100 }, new() { TimeMs = 4000, X = 250 }]); f.Diff.Document.Tracks.Add(track);
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true); WorkspaceExport.Commit(f.Session, plan);
            string[] lines = WorkspaceSynchronization.ObjectLines(plan.Output.Text); int index = plan.Output.ObjectSources.ToList().FindIndex(id => id == track.Id);
            string changed = "350," + lines[index][(lines[index].IndexOf(',') + 1)..];
            File.WriteAllText(f.Source, plan.Output.Text.Replace(lines[index], changed));
            var merge = f.Merge(); var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Tracks.Count == 0 && result.Fruits.Count == lines.Length, "all stream members retained when accepting external structure");
        }));
        yield return ("Sync: interrupted export restores pending authoring without overwriting newer osu content", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 430;
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true);
            WorkspaceExportRecovery.Prepare(f.Session, f.Session.Project, plan, f.Diff.Id);
            File.WriteAllText(f.Source, Fixture().Replace("100,192", "321,192"));
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            Check(reopened.Project.Difficulties[0].Document.Fruits[0].X == 430, "pending authoring recovered");
            Check(OsuBeatmapReader.ReadFile(f.Source).Fruits[0].X == 321, "newer external state not overwritten");
            Check(WorkspaceSynchronization.Scan(reopened, f.Songs).Difficulties.Single().State == WorkspaceSyncState.Changed, "external difference still detected");
        }));
        yield return ("Sync: legacy missing baseline requires explicit version choice", () => Run(f =>
        {
            f.Session.Manifest.Difficulties[0].Sync = null;
            File.WriteAllText(f.Source, Fixture().Replace("100,192", "321,192"));
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.NeedsBaseline, "unknown history must not auto merge");
        }));
        yield return ("Sync: numeric spelling and newline changes are not object conflicts", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("100,192,1000", "100.0,192.0,1000.0").Replace("\n", "\r\n"));
            Check(f.Merge().Conflicts.Count == 0, "semantic normalization");
        }));
        yield return ("Sync: duplicate project resolution preserves unique difficulties", () => Run(f =>
        {
            f.CopyProject(); var copy = WorkspaceProject.Open(Path.Combine(f.Workspace, "copied-project"));
            string unique = Path.Combine(f.Set, "unique.osu"); File.WriteAllText(unique, Fixture().Replace("100,192", "333,192"));
            var diff = BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(unique)]).Difficulties.Single(); copy.Project.Difficulties.Add(diff);
            WorkspaceProject.Save(copy, copy.Project);
            var keep = WorkspaceAssociations.Claims(f.Workspace).First(c => c.Project == f.Session.Directory);
            var winner = WorkspaceAssociations.KeepOnly(f.Session, f.Session.Project, f.Source, keep);
            Check(winner.Project.Difficulties.Count == 2 && winner.Project.Difficulties.Any(d => d.Id == diff.Id), "unique difficulty moved");
            Check(File.Exists(unique) && File.Exists(f.Source) && WorkspaceAssociations.Claims(f.Workspace).Select(c => c.Project).Distinct().Count() == 1, "one project, both external files");
        }));
        yield return ("Sync: selecting FA audio can recover overwritten bytes", () => Run(f =>
        {
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "external replacement");
            string path = WorkspaceSynchronization.LocalAudioVersion(f.Session.Manifest.Difficulties[0], f.Diff.Document, f.Session.Directory)!;
            Check(File.ReadAllText(path) == "original audio" && Path.GetExtension(path) == ".mp3", "original playable audio restored");
        }));
        yield return ("Sync: retained FA audio remains its own version after restart and another replacement", () => Run(f =>
        {
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "external replacement");
            var entry = f.Session.Manifest.Difficulties[0];
            f.Diff.Document.AudioPath = WorkspaceSynchronization.LocalAudioVersion(entry, f.Diff.Document, f.Session.Directory);
            WorkspaceSynchronization.Accept(f.Session, entry, WorkspaceSynchronization.ReadStable(f.Source), f.Diff.Document, true);
            WorkspaceProject.Save(f.Session, f.Session.Project);
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            var local = reopened.Project.Difficulties[0].Document;
            File.WriteAllText(local.AudioPath!, "overwritten retained audio");
            string recovered = WorkspaceSynchronization.LocalAudioVersion(reopened.Manifest.Difficulties[0], local, reopened.Directory)!;
            Check(File.ReadAllText(recovered) == "original audio", "authoring hash is independent of external baseline");
        }));
        yield return ("Sync: library and synchronization only accept Catch difficulties", () => Run(f =>
        {
            var db = new LibraryDatabase(f.Workspace, f.Songs);
            foreach (int mode in new[] { 0, 1, 3 })
            {
                string directory = mode == 3 ? Path.Combine(f.Songs, "mania-only") : f.Set;
                Directory.CreateDirectory(directory);
                string other = Path.Combine(directory, $"mode-{mode}.osu");
                File.WriteAllText(other, Fixture().Replace("Mode:2", $"Mode:{mode}"));
            }
            Check(db.Scan().Count == 1 && db.Scan().Count == 1, "fresh and cached scans only count Catch");
            Check(db.Search("").All(m => m.Mode == 2), "only Catch indexed");
            using (var snapshot = db.SearchSnapshot(""))
                Check(snapshot.Count == 1, "non-Catch sets excluded from paged library");
            Check(f.Scan().Additions.Count == 0, "not imported as Catch");

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = Path.Combine(f.Workspace, "library.db"), Pooling = false }.ToString()))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO maps SELECT path || '.other',root,stamp,size,json_set(data,'$.Mode',3),search FROM maps; PRAGMA user_version=2;";
                command.ExecuteNonQuery();
            }
            db = new LibraryDatabase(f.Workspace, f.Songs);
            Check(db.Search("").Count == 1, "reopening removes cached non-Catch entries before scanning");
            using (var snapshot = db.SearchSnapshot(""))
                Check(snapshot.Page(0).Count == 1, "paged library excludes migrated entries");
            Check(db.Scan().Count == 1, "rescanning preserves Catch-only catalog");
        }));
        yield return ("Sync: metadata, file and folder rename retain authoring identities", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 401;
            string moved = Path.Combine(f.Songs, "renamed"); Directory.Move(f.Set, moved);
            string path = Path.Combine(moved, "new name.osu"); File.Move(Path.Combine(moved, "map.osu"), path);
            File.WriteAllText(path, Fixture().Replace("Title:Title", "Title:New title").Replace("Artist:Artist", "Artist:New artist").Replace("Creator:Mapper", "Creator:New mapper"));
            var status = f.Scan().Difficulties.Single(); Check(status.State == WorkspaceSyncState.Changed && status.Candidate!.Path == path, "rename association");
            var merged = f.Resolve(status.Candidate!);
            Check(merged.Fruits[0].Id == f.Diff.Document.Fruits[0].Id && merged.Fruits[0].X == 401, "pending FA objects retained");
            Check(OsuBeatmapReader.Setting(merged, "Metadata", "Title") == "New title", "metadata copied");
            Check(merged.AudioPath == Path.Combine(moved, "audio.mp3"), "audio relocated");
            Check(WorkspaceAssociations.FindProject(f.Workspace, path) == f.Session.Directory, "reopening rename finds old project");
        }));
        yield return ("Sync: uploaded IDs update without replacing objects", () => Run(f =>
        {
            File.WriteAllText(f.Source, Fixture().Replace("BeatmapID:0", "BeatmapID:12345").Replace("BeatmapSetID:-1", "BeatmapSetID:789"));
            var resolved = f.Resolve(WorkspaceSynchronization.ReadStable(f.Source));
            Check(resolved.Fruits[0].Id == f.Diff.Document.Fruits[0].Id, "identity");
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "BeatmapID") == "12345", "ID");
        }));
        yield return ("Sync: all differing metadata fields require individual choices", () => Run(f =>
        {
            Set(f.Diff.Document, "Title", "FA title"); Set(f.Diff.Document, "Tags", "local tags");
            File.WriteAllText(f.Source, Fixture().Replace("Title:Title", "Title:osu title").Replace("Creator:Mapper", "Creator:External mapper"));
            var merge = f.Merge(); Check(merge.Conflicts.Select(c => c.Key).ToHashSet().SetEquals(new[] { "Metadata/Title", "Metadata/Creator", "Metadata/Tags" }), "single-sided and two-sided differences require choices");
            Reject(() => WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool>()));
            var resolved = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool> { ["Metadata/Title"] = false, ["Metadata/Creator"] = true, ["Metadata/Tags"] = false });
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "Title") == "FA title", "local choice");
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "Creator") == "External mapper", "external disjoint");
            Check(OsuBeatmapReader.Setting(resolved, "Metadata", "Tags") == "local tags", "local disjoint");
        }));
        yield return ("Sync: external deletion retains authoring and new files are visible", () => Run(f =>
        {
            File.Delete(f.Source);
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Missing, "missing state");
            Check(WorkspaceProject.Open(f.Session.Directory).Project.Difficulties.Count == 1, "authoring retained");
            File.WriteAllText(Path.Combine(f.Set, "new.osu"), Fixture().Replace("100,192", "220,192"));
            Check(f.Scan().Additions.Count == 1, "new difficulty");
        }));
        yield return ("Sync: unavailable Songs is not a deletion", () => Run(f =>
        {
            Directory.Move(f.Songs, f.Songs + "-offline");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Unavailable, "offline state");
        }));
        yield return ("Sync: identical relocation candidates require explicit association", () => Run(f =>
        {
            File.Delete(f.Source);
            File.WriteAllText(Path.Combine(f.Set, "copy1.osu"), Fixture()); File.WriteAllText(Path.Combine(f.Set, "copy2.osu"), Fixture());
            var scan = f.Scan(); Check(scan.Difficulties.Single().State == WorkspaceSyncState.Ambiguous && scan.Additions.Count == 0, "ambiguity not additions");
        }));
        yield return ("Sync: a live association is not stolen by an identical copy", () => Run(f =>
        {
            File.WriteAllText(Path.Combine(f.Set, "copy.osu"), Fixture());
            var scan = f.Scan(); Check(scan.Difficulties.Single().Candidate!.Path == f.Source && scan.Additions.Count == 1, "copy stays separate");
        }));
        yield return ("Sync: renamed and replaced audio synchronizes with metadata", () => Run(f =>
        {
            File.Delete(Path.Combine(f.Set, "audio.mp3")); File.WriteAllText(Path.Combine(f.Set, "new.mp3"), "replacement");
            File.WriteAllText(f.Source, Fixture().Replace("audio.mp3", "new.mp3"));
            var merge = f.Merge(); Check(merge.Conflicts.Count == 1 && merge.Conflicts[0].Key == "General/AudioFilename", "renamed filename is reviewed without a spurious audio-content conflict");
            var result = WorkspaceSynchronization.Resolve(merge, new Dictionary<string, bool> { ["General/AudioFilename"] = true });
            Check(result.AudioPath == Path.Combine(f.Set, "new.mp3"), "new audio");
            Check(result.Fruits[0].TimeMs == 1000, "no implicit retiming");
        }));
        yield return ("Sync: same-name audio replacement is detected and prior bytes retained", () => Run(f =>
        {
            string hash = f.Session.Manifest.Difficulties[0].Sync!.AudioHash!;
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "replaced audio");
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Changed, "audio changed");
            Check(File.ReadAllText(Path.Combine(f.Workspace, ".sync-history", "resources", hash)) == "original audio", "old audio saved");
        }));
        yield return ("Sync: independently replaced audio requires resource choice", () => Run(f =>
        {
            string local = Path.Combine(f.Set, "local.mp3"); File.WriteAllText(local, "local audio"); f.Diff.Document.AudioPath = local;
            File.WriteAllText(Path.Combine(f.Set, "audio.mp3"), "external audio");
            var merge = f.Merge(); Check(merge.Conflicts.Any(c => c.Key == "$audio"), "audio conflict");
            var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => false));
            Check(result.AudioPath == local, "local audio retained");
        }));
        yield return ("Sync: changes to separate objects can be resolved independently", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 300;
            File.WriteAllText(f.Source, Fixture().Replace("100,192", "250,192").Replace("200,192", "400,192"));
            var merge = f.Merge(); Check(merge.Conflicts.Count >= 1, "object conflict");
            var result = WorkspaceSynchronization.Resolve(merge, merge.Conflicts.ToDictionary(c => c.Key, _ => true));
            Check(result.Fruits.Any(o => o.X == 250) && result.Fruits.Any(o => o.X == 400), "external objects selected");
        }));
        yield return ("Sync: plain local save does not advance synchronization baseline", () => Run(f =>
        {
            string text = f.Session.Manifest.Difficulties[0].Sync!.Text;
            f.Diff.Document.Fruits[0].X = 400; WorkspaceProject.Save(f.Session, f.Session.Project);
            Check(f.Session.Manifest.Difficulties[0].Sync!.Text == text, "baseline unchanged");
        }));
        yield return ("Sync: export records actual emitted objects and authoring mapping", () => Run(f =>
        {
            f.Diff.Document.Fruits[0].X = 350;
            var plan = WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true); WorkspaceExport.Commit(f.Session, plan);
            var sync = f.Session.Manifest.Difficulties[0].Sync!;
            Check(sync.Text == File.ReadAllText(f.Source) && sync.ObjectSources[0] == f.Diff.Document.Fruits[0].Id, "export baseline");
        }));
        yield return ("Sync: late external edits reject stale resolution", () => Run(f =>
        {
            var candidate = WorkspaceSynchronization.ReadStable(f.Source);
            File.AppendAllText(f.Source, "\n// later change");
            Reject(() => WorkspaceSynchronization.Accept(f.Session, f.Session.Manifest.Difficulties[0], candidate, f.Diff.Document, true));
        }));
        yield return ("Sync: difficulty deletion removes both files and retains recovery data", () => Run(f =>
        {
            string diff = Path.Combine(f.Session.Directory, f.Session.Manifest.Difficulties[0].File);
            WorkspaceAssociations.DeleteDifficulty(f.Session, f.Session.Project, f.Diff.Id);
            Check(!File.Exists(f.Source) && !File.Exists(diff) && !File.Exists(Path.Combine(f.Session.Directory, WorkspaceProject.ManifestName)), "both removed");
            Check(Directory.EnumerateFiles(Path.Combine(f.Workspace, ".sync-history"), "external.osu", SearchOption.AllDirectories).Any(), "external recovery");
        }));
        yield return ("Sync: duplicate ownership blocks export and ordinary deletion", () => Run(f =>
        {
            f.CopyProject();
            Check(f.Scan().Difficulties.Single().State == WorkspaceSyncState.Duplicate, "duplicate state");
            Reject(() => WorkspaceExport.Plan(f.Session, f.Diff, f.Songs, true, "", true));
            Reject(() => WorkspaceAssociations.DeleteDifficulty(f.Session, f.Session.Project, f.Diff.Id));
            Check(File.Exists(f.Source), "shared external untouched");
        }));
        yield return ("Sync: resolving duplicate projects leaves one owner and preserves osu", () => Run(f =>
        {
            f.CopyProject(); var keep = WorkspaceAssociations.Claims(f.Workspace).First(c => c.Project == f.Session.Directory);
            WorkspaceAssociations.KeepOnly(f.Session, f.Session.Project, f.Source, keep);
            Check(WorkspaceAssociations.Claims(f.Workspace).Count == 1 && File.Exists(f.Source), "one owner, external retained");
        }));
        yield return ("Sync: baseline survives project save and restart", () => Run(f =>
        {
            var reopened = WorkspaceProject.Open(f.Session.Directory);
            Check(reopened.Manifest.Difficulties[0].Sync!.ObjectSources.SequenceEqual(f.Session.Manifest.Difficulties[0].Sync!.ObjectSources), "mapping persists");
            Check(WorkspaceSynchronization.Scan(reopened, f.Songs).Difficulties.Single().State == WorkspaceSyncState.Current, "restart current");
        }));
    }
    private sealed class FixtureContext
    {
        public string Root = Path.GetFullPath(Path.Combine("artifacts/tests/synchronization", Guid.NewGuid().ToString("N")));
        public string Workspace => Path.Combine(Root, "Workspace");
        public string Songs => Path.Combine(Root, "Songs");
        public string Set => Path.Combine(Songs, "original");
        public string Source => Path.Combine(Set, "map.osu");
        public WorkspaceSession Session;
        public ProjectDifficulty Diff => Session.Project.Difficulties[0];
        public FixtureContext()
        {
            Directory.CreateDirectory(Set); File.WriteAllText(Source, Fixture()); File.WriteAllText(Path.Combine(Set, "audio.mp3"), "original audio");
            Session = WorkspaceProject.Create(Workspace, BeatmapProject.FromDocuments([OsuBeatmapReader.ReadFile(Source)]), Songs);
        }
        public WorkspaceSyncScan Scan() => WorkspaceSynchronization.Scan(Session, Songs);
        public WorkspaceMerge Merge() => WorkspaceSynchronization.Merge(Session.Manifest.Difficulties[0], Diff.Document, WorkspaceSynchronization.ReadStable(Source), Session.Directory, true);
        public MapDocument Resolve(WorkspaceSyncCandidate candidate)
        {
            var merge = WorkspaceSynchronization.Merge(Session.Manifest.Difficulties[0], Diff.Document, candidate, Session.Directory, true);
            return WorkspaceSynchronization.Resolve(merge, merge.Conflicts.Where(c => !c.Key.StartsWith('$')).ToDictionary(c => c.Key, _ => true));
        }
        public void CopyProject()
        {
            string copy = Path.Combine(Workspace, "copied-project"); Directory.CreateDirectory(copy);
            foreach (string file in Directory.EnumerateFiles(Session.Directory)) File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }
    }
    private static void Run(Action<FixtureContext> action) => action(new FixtureContext());
    private static OsuWriteResult CaptureCurveTiming(FixtureContext f, bool sampleBoundary = false)
    {
        f.Diff.Document.DurationMs = 10000;
        f.Diff.Document.TimingPoints.Add(new TimingPoint { TimeMs = 3000, BeatLengthMs = -100, Uninherited = false, SampleSet = 1 });
        if (sampleBoundary) f.Diff.Document.TimingPoints.Add(new TimingPoint { TimeMs = 4000, BeatLengthMs = -100, Uninherited = false, SampleSet = 1 });
        foreach (var (start, end) in new[] { (3000, 3500), (4000, 4250) })
        {
            var track = new CurveTrack(); track.Nodes.AddRange([new() { TimeMs = start, X = 100 }, new() { TimeMs = end, X = 400 }]);
            f.Diff.Document.Tracks.Add(track);
        }
        var output = OsuBeatmapWriter.Serialize(f.Diff.Document);
        File.WriteAllText(f.Source, output.Text);
        f.Session.Manifest.Difficulties[0].Sync = WorkspaceSynchronization.Capture(f.Source, f.Diff.Document, f.Session.Directory,
            output.Text, output.ObjectSources);
        return output;
    }
    private static string TimingValues(IEnumerable<TimingPoint> points) => string.Join('\n', points.Select(t => string.Join(',',
        t.TimeMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture), t.BeatLengthMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        t.Meter, t.SampleSet, t.SampleIndex, t.Volume, t.Uninherited, t.Effects)));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException) { return; } throw new Exception("Expected rejection"); }
    private static void Set(MapDocument document, string key, string value)
    {
        var section = document.OriginalSections.Single(s => s.Name == "Metadata"); section.Lines.RemoveAll(l => l.StartsWith(key + ":")); section.Lines.Add(key + ":" + value);
    }
    private static string Fixture() => """
osu file format v14
[General]
AudioFilename:audio.mp3
Mode:2
[Metadata]
Title:Title
Artist:Artist
Creator:Mapper
Version:Rain
BeatmapID:0
BeatmapSetID:-1
[Difficulty]
CircleSize:5
ApproachRate:5
SliderMultiplier:1.4
SliderTickRate:1
[TimingPoints]
0,500,4,1,0,100,1,0
[HitObjects]
100,192,1000,1,0,0:0:0:0:
150,192,1500,1,0,0:0:0:0:
200,192,2000,1,0,0:0:0:0:
""".Replace("\r", "");
}
