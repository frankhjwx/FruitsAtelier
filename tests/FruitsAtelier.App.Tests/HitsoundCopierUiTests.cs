using FruitsAtelier.Core;
using L=FruitsAtelier.Localization.Strings;

internal static class HitsoundCopierUiTests
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Run()
    {
        string language=L.Language;
        try
        {
            foreach(string lang in new[]{"en","zh-CN"})
            {
                L.SetLanguage(lang);
                var target=new MapDocument { IsDemo=false };
                target.Fruits.Add(new Fruit {TimeMs=1000,X=100,OriginalLine="100,192,1000,1,8,2:3:2:70:"});
                SongSetup.Set(target,"Metadata","Version","Target");
                var source=target.DeepClone();
                source.TimingPoints.Add(new TimingPoint { TimeMs=0, BeatLengthMs=source.BeatLengthMs, Uninherited=true });
                source.TimingPoints.Add(new TimingPoint { TimeMs=500, BeatLengthMs=-50, Uninherited=false, SampleSet=3, SampleIndex=3, Volume=40 });
                source.Fruits[0].OriginalLine="100,192,1000,1,2,3:3:3:40:";
                var ui=new Ui(); ui.LoadDocument(target); var before=ui.View.Document.DeepClone();
                ui.View.OpenHitsoundCopier(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t=>t.Value==L.Get("copier.title")),"Copier visible");
                Check(ui.Canvas.Texts.Single(t=>t.Value==L.Get("copier.clear")).X > ui.Canvas.Texts.Single(t=>t.Value==L.Get("copier.set")).X,
                    "Clear is the rightmost tab");
                Check(!ui.Canvas.Texts.Any(t=>t.Value==L.Get("copier.clearHelp") || t.Value==L.Get("copier.matchHelp")),"No explanatory text");
                ui.Key(90,ctrl:true); Check(ui.View.HitsoundCopierVisible,"Window blocks editing shortcuts");
                ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
                Check(!ui.View.HitsoundCopierVisible && ObjectFlags.Sounds(ui.View.Document,ui.View.Document.Fruits[0].Id)[0]==0,"Clear applies and closes");
                ui.Key(90,ctrl:true); Check(ui.View.Document.ContentEquals(before),"Clear undo");
                ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.external")); ui.View.SetHitsoundSource(source); ui.Paint();
                ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("copier.newDiff")); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.CaptureProject().Difficulties.Count==2,"New Diff added");
                Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions==2,"Copied sound on new Diff");
                ui.View.OpenTimingSetup(); ui.Paint();
                Check(ui.View.Document.TimingPoints.Any(p=>p.TimeMs==500 && !p.Uninherited && p.SampleIndex==3),"Copied sample green appears in Timing Setup content");
                ui.Key(27);
                Check(ui.View.CaptureProject().Difficulties[0].Document.ContentEquals(before),"Original Diff preserved");
                ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.set")); ui.ClickText(L.Get("copier.chooseDiff")); ui.Click(ui.View.HitsoundCopierBounds.X+32,ui.View.HitsoundCopierBounds.Y+154);
                ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions==8,"Same-set copy uses selected Diff");
                ui.Key(90,ctrl:true); Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions==2,"Copy undo");
                ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.preview"));
                ui.View.Document.Fruits[0].X=125;
                ui.ClickText(L.Get("song.ok"));
                Check(ui.View.HitsoundCopierVisible && ui.View.Document.Fruits[0].X==125,"Stale preview cannot overwrite newer edits");
                ui.Key(27);
                ui.View.OpenHitsoundCopier(); ui.Key(27); Check(!ui.View.HitsoundCopierVisible,"Escape closes");
                Batch(source,target);
                SliderTargets();
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Batch(MapDocument source,MapDocument target)
    {
        var other=target.DeepClone(); SongSetup.Set(other,"Metadata","Version","Other");
        SongSetup.Set(source,"Metadata","Version","Source");
        var ui=new Ui(); ui.View.LoadProject(BeatmapProject.FromDocuments([target,source,other])); ui.Paint();
        var before=ui.View.CaptureProject();
        ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.set")); ui.ClickText(L.Get("copier.chooseDiff"));
        ui.Click(ui.View.HitsoundCopierBounds.X+32,ui.View.HitsoundCopierBounds.Y+154);
        ui.ClickText(L.Get("copier.allTargets")); ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
        var copied=ui.View.CaptureProject();
        Check(copied.Difficulties[0].Document.HitsoundOverrides.Single().Sample.Additions==2
            && copied.Difficulties[2].Document.HitsoundOverrides.Single().Sample.Additions==2,"All targets receive sounds");
        Check(copied.Difficulties[1].Document.ContentEquals(before.Difficulties[1].Document),"Same-set source excluded");
        ui.Key(90,ctrl:true); Check(ui.View.Document.ContentEquals(before.Difficulties[0].Document),"Active Diff batch undo");
        ui.View.SwitchDifficulty(2); ui.Paint(); ui.Key(90,ctrl:true);
        Check(ui.View.Document.ContentEquals(before.Difficulties[2].Document),"Other Diff batch undo");
        ui.Key(89,ctrl:true); Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions==2,"Other Diff batch redo");
        ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.external")); ui.View.SetHitsoundSource(source); ui.Paint();
        ui.ClickText(L.Get("copier.allTargets")); ui.ClickText(L.Get("copier.newDiff")); ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
        Check(ui.View.CaptureProject().Difficulties.Count==6,"External batch creates one Diff per target");
        var names=ui.View.CaptureProject().Difficulties.Select(d=>d.Name).ToArray();
        Check(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==6,"Batch names unique");
        ui.View.LoadProject(before); ui.Paint();
        ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.external")); ui.View.SetHitsoundSource(source); ui.Paint();
        ui.ClickText(L.Get("copier.allTargets")); ui.ClickText(L.Get("copier.preview"));
        ui.View.CaptureProject(); ui.View.Document.Fruits[0].X=200; ui.ClickText(L.Get("song.ok"));
        Check(ui.View.HitsoundCopierVisible && ui.View.CaptureProject().Difficulties[2].Document.HitsoundOverrides.Count==0,
            "Stale batch applies no target changes"); ui.Key(27);
    }

    private static void SliderTargets()
    {
        foreach (bool fslider in new[] { false, true })
        foreach (bool osu in new[] { false, true })
        foreach (bool sameSet in new[] { false, true })
        {
            var target = OsuBeatmapReader.Read("osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,80,1000,2,0,L|200:80,1,100,0|0,0:0|0:0,0:0:0:0:\n");
            Guid id = target.ImportedSliders.Single().Id;
            if (fslider)
            {
                target.ImportedSliders.Clear();
                var track = new CurveTrack { Id = id, Kind = CurveKind.Linear };
                track.Nodes.AddRange([new() { TimeMs = 1000, X = 100 }, new() { TimeMs = 1500, X = 200 }]);
                target.Tracks.Add(track);
            }
            var source = target.DeepClone();
            ObjectFlags.SetSound(source, id, 2, true);
            SongSetup.Set(target, "Metadata", "Version", "Target");
            SongSetup.Set(source, "Metadata", "Version", "Source");
            var ui = new Ui();
            if (sameSet) { ui.View.LoadProject(BeatmapProject.FromDocuments([target, source])); ui.Paint(); }
            else ui.LoadDocument(target);
            if (osu) { ui.View.ToggleOsuCanvas(); ui.Paint(); }
            var before = ui.View.Document.DeepClone();
            ui.View.OpenHitsoundCopier(); ui.Paint();
            if (sameSet)
            {
                ui.ClickText(L.Get("copier.set")); ui.ClickText(L.Get("copier.chooseDiff"));
                ui.Click(ui.View.HitsoundCopierBounds.X+32, ui.View.HitsoundCopierBounds.Y+154);
            }
            else { ui.ClickText(L.Get("copier.external")); ui.View.SetHitsoundSource(source); ui.Paint(); }
            ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
            Check(!ui.View.HitsoundCopierVisible && ui.View.Document.HitsoundOverrides.Any(h => h.SourceId == id && (h.Sample.Additions & 2) != 0),
                "Slider hitsound copy must apply in either canvas for either representation.");
            Check(before.UnchangedObjectIds(ui.View.Document).Contains(id)
                && ui.View.Document.Tracks.Count == before.Tracks.Count && ui.View.Document.ImportedSliders.Count == before.ImportedSliders.Count,
                "Hitsound copy changed slider geometry or representation.");
            Check(OsuBeatmapWriter.Serialize(ui.View.Document).ObjectSequenceMatches, "Copied slider samples did not validate on export.");
            var copied = ui.View.Document.DeepClone();
            ui.Key('Z', ctrl: true); Check(before.ContentEquals(ui.View.Document), "Slider hitsound copy did not undo atomically.");
            ui.Key('Y', ctrl: true); Check(copied.ContentEquals(ui.View.Document), "Slider hitsound copy did not redo atomically.");
            ui.View.OpenHitsoundCopier(); ui.Paint();
            ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
            Check(!ui.View.HitsoundCopierVisible && ObjectFlags.Sounds(ui.View.Document, id).All(s => s == 0),
                "Slider hitsound clearing must apply in either canvas for either representation.");
            ui.Key('Z', ctrl: true); Check(copied.ContentEquals(ui.View.Document), "Slider hitsound clearing did not undo atomically.");
        }
    }

}
