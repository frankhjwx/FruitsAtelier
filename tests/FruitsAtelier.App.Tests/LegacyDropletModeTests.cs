using System.Reflection;
using FruitsAtelier.App.Editor;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LegacyDropletModeTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (bool mapDerandomize in new[] { false, true })
            foreach (bool initialized in new[] { false, true })
            {
                L.SetLanguage(locale);
                var source = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n[TimingPoints]\n0,500\n[HitObjects]\n256,192,1000,2,0,L|256:392,1,200\n");
                source.DurationMs = 6000;
                source.DerandomizeFSliderDroplets = mapDerandomize;
                source.DerandomizeDropletsForHardRock = true;
                if (initialized)
                {
                    source.SliderImportState = SliderImportState.Pending;
                    var result = SliderImportEditing.Initialize(source, false);
                    Check(result.Success, string.Join("; ", result.Diagnostics));
                }
                var track = new CurveTrack { Kind = CurveKind.Linear, DropletMode = SliderDropletMode.FollowMap };
                track.Nodes.AddRange([new() { TimeMs = 3000, X = 256 }, new() { TimeMs = 4500, X = 256 }]);
                source.Tracks.Add(track);
                var ui = new Ui(false); ui.LoadDocument(source); ui.Resize(980, 620);
                var legacyId = source.ImportedSliders.Single().Id;
                var targets = new[] { legacyId, track.Id };
                Select(targets);
                ui.View.ShowSliderInspector(); ui.Paint();
                foreach (var mode in new[] { SliderDropletMode.Randomize, SliderDropletMode.Derandomize, SliderDropletMode.FollowMap })
                {
                    var before = ui.View.Document.DeepClone();
                    Choose(mode);
                    var after = ui.View.Document.DeepClone();
                    Check(after.ImportedSliders.Single().DropletMode == mode && after.Tracks.Single().DropletMode == mode,
                        $"Mixed selection mode {mode} (map={mapDerandomize}, initialized={initialized}): Legacy={after.ImportedSliders.Single().DropletMode}, FSlider={after.Tracks.Single().DropletMode}; {ui.View.StatusMessage}");
                    Check(after.DerandomizeFSliderDroplets == mapDerandomize && after.ImportedSliders.Count == 1,
                        "Legacy mode changed the map default or representation");
                    bool random = mode == SliderDropletMode.Randomize || mode == SliderDropletMode.FollowMap && !mapDerandomize;
                    Check(after.ImportedSliders.Single().RandomizesDroplets(after) == random, "Legacy effective mode ignores the override");
                    var output = OsuBeatmapWriter.Serialize(after);
                    Check(output.ObjectSequenceMatches, "Legacy mode export failed read-back validation");
                    var tinies = (random ? output.PlayableObjects : output.PlayableHardRockObjects)
                        .Where(o => o.SourceId == legacyId && o.Kind == CatchObjectKind.TinyDroplet).ToArray();
                    Check(tinies.Length > 0 && (random ? tinies.Any(o => Math.Abs(o.X - 256) > 1)
                        : tinies.All(o => Math.Abs(o.X - 256) <= 1)),
                        $"Legacy tiny output {mode} (map={mapDerandomize}, initialized={initialized}): {string.Join(",", tinies.Select(o => o.X))}; tolerance={output.MaxConvertedXError}");
                    Check(after.ContentEquals(ProjectSerializer.Read(ProjectSerializer.Serialize(after))), "Legacy mode single-project persistence failed");
                    Check(after.ContentEquals(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(BeatmapProject.FromDocuments([after])))
                        .Difficulties.Single().Document), "Legacy mode multi-project persistence failed");
                    var converted = after.DeepClone();
                    Check(SliderRepresentationEditing.ConvertToTrack(converted, legacyId).Success
                        && converted.Tracks.Single(t => t.Id == legacyId).DropletMode == mode, "Legacy-to-FSlider conversion lost its mode");
                    ui.Key('Z', ctrl: true); ui.Paint();
                    Check(ui.View.Document.ContentEquals(before), "Mixed mode change did not undo atomically");
                    ui.Key('Y', ctrl: true); ui.Paint();
                    Check(ui.View.Document.ContentEquals(after), "Mixed mode redo changed prepared output");
                    Select(targets);
                }
                // Independent Legacy edits must leave the FSlider override intact and display Mixed.
                Select([legacyId]); Choose(SliderDropletMode.Derandomize);
                Check(ui.View.Document.Tracks.Single().DropletMode == SliderDropletMode.FollowMap, "Legacy-only mode changed an unselected FSlider");
                Select(targets); ReachMode();
                Check(ui.Canvas.Texts.Any(t => t.Value == L.Get("inspector.dropletMode", L.Get("inspector.mixed"))),
                    "Mixed Legacy/FSlider modes are not labelled Mixed");

                void Select(Guid[] ids)
                {
                    typeof(EditorView).GetMethod("SelectObjects", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(ui.View, [ids, ids[0]]);
                    ui.Paint();
                }
                bool IsModeLabel(string text) => Enum.GetValues<SliderDropletMode>().Select(m => L.Get("inspector.dropletMode", L.Get("droplet.mode." + m)))
                    .Append(L.Get("inspector.dropletMode", L.Get("inspector.mixed"))).Contains(text);
                void ReachMode()
                {
                    for (int i = 0; i < 25; i++)
                    {
                        var label = ui.Canvas.Texts.FirstOrDefault(t => IsModeLabel(t.Value));
                        var panel = ui.View.SliderInspectorViewport;
                        if (label.Value is not null && label.Y >= panel.Y && label.Y + 32 <= panel.Bottom) return;
                        ui.View.Wheel(panel.X + 10, panel.Y + 10, -120, false, false); ui.Paint();
                    }
                    throw new Exception("Legacy droplet mode control is unreachable");
                }
                void Choose(SliderDropletMode mode)
                {
                    ReachMode();
                    var label = ui.Canvas.Texts.Single(t => IsModeLabel(t.Value));
                    ui.Click(label.X + 4, label.Y + 4);
                    var choice = ui.Canvas.Texts.Last(t => t.Value == L.Get("droplet.mode." + mode));
                    ui.Click(choice.X + 4, choice.Y + 4); ui.Paint();
                }
            }
        }
        finally { L.SetLanguage(language); }
        SettingsAndCompatibility();
    }

    private static void SettingsAndCompatibility()
    {
        var source = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[TimingPoints]\n0,500\n[HitObjects]\n256,192,1000,2,0,L|256:392,1,200\n");
        var old = ProjectSerializer.Read(ProjectSerializer.Serialize(source));
        Check(old.ImportedSliders.Single().DropletMode is null && old.ContentEquals(source), "Older Legacy projects gained an override on load");
        foreach (var mode in Enum.GetValues<SliderDropletMode>())
        {
            var map = source.DeepClone();
            var cache = new OsuWriteCache();
            foreach (bool derandomize in new[] { false, true })
            foreach (double strength in new[] { 20d, 12d })
            {
                var before = map.DeepClone();
                map.ImportedSliders.Single().DropletMode = mode;
                map.DerandomizeFSliderDroplets = derandomize;
                map.RandomizeDropletStrength = strength;
                map.RandomizeDropletSeed = strength == 20 ? 1337 : -123;
                Check(SliderRepresentationEditing.NeedsLegacyDropletRefresh(before, map), "Mode/default/FX changes did not invalidate Legacy processing");
                var result = SliderRepresentationEditing.RefreshLegacyDroplets(map, previous: before, cache: cache);
                Check(result.Success, string.Join("; ", result.Diagnostics));
                var output = OsuBeatmapWriter.Serialize(map, cache: cache);
                Check(output.ObjectSequenceMatches && output.Text == OsuBeatmapWriter.Serialize(map).Text, "Cached Legacy mode export differs from uncached output");
                bool random = mode == SliderDropletMode.Randomize || mode == SliderDropletMode.FollowMap && !derandomize;
                var tinies = output.PlayableObjects.Where(o => o.Kind == CatchObjectKind.TinyDroplet).ToArray();
                Check(tinies.Length > 0 && (random ? tinies.Any(o => Math.Abs(o.X - 256) > 1) : tinies.All(o => Math.Abs(o.X - 256) <= 1)),
                    "Legacy mode did not survive later map default, strength or seed changes");
                var guard = new EditorHistory(map);
                var id = map.ImportedSliders.Single().Id;
                guard.Begin("Mode"); guard.Document.ImportedSliders.Single().DropletMode = (SliderDropletMode)(((int)mode + 1) % 3);
                Check(!guard.TransactionChangesSliderGeometry([id], ignoreDropletMode: true), "Mode-only edit was treated as authored geometry");
                guard.Document.ImportedSliders.Single().X += 1;
                Check(guard.TransactionChangesSliderGeometry([id], ignoreDropletMode: true), "Mode exemption bypassed the canvas geometry guard");
            }
        }
        source.ImportedSliders.Single().DropletMode = (SliderDropletMode)99;
        try { ProjectSerializer.Serialize(source); throw new Exception("Invalid Legacy droplet mode was saved"); }
        catch (InvalidDataException) { }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
