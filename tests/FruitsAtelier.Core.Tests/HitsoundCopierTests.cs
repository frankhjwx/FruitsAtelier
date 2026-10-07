using FruitsAtelier.Core;

internal static class HitsoundCopierTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static MapDocument Read(string objects, string timing = "0,500,4,1,0,100,1,0") => OsuBeatmapReader.Read(
        "osu file format v14\n[General]\nMode:2\n[Difficulty]\nSliderMultiplier:1\nSliderTickRate:2\n[TimingPoints]\n" + timing + "\n[HitObjects]\n" + objects);

    public static void Run()
    {
        var source = Read("80,192,1000,1,8,2:3:2:70:\n120,192,1250,1,0,2:2:2:35:\n180,192,1500,1,2,3:3:3:60:\n200,192,4000,1,4,0:0:0:0:",
            "0,500,4,1,0,100,1,0\n1000,-25,4,2,2,70,0,1");
        var target = Read("100,192,1000,2,4,L|200:192,1,100,4|4,1:1|1:1,0:0:0:0:\n300,192,2000,1,8,0:0:0:0:");
        var before = target.DeepClone();
        var result = HitsoundCopier.Copy(source, target);
        Check(result.MatchedEvents == 3, "Match head, tick and tail by time");
        Check(target.ContentEquals(before), "Copy must not change either input");
        Check(result.Document.ImportedSliders[0].OriginalLine == target.ImportedSliders[0].OriginalLine, "Authored slider remains intact");
        Check(result.Document.TimingPoints.Zip(target.TimingPoints).All(p => p.First.BeatLengthMs == p.Second.BeatLengthMs), "Do not import source SV");
        var exported = OsuBeatmapWriter.Serialize(result.Document);
        var parsed = OsuBeatmapReader.Read(exported.Text);
        var events = CatchStreamConverter.Convert(parsed).Objects;
        var resolver = new HitsoundResolver(parsed, events);
        var head = resolver.Describe(events.Single(o => o.Kind == CatchObjectKind.Fruit && o.TimeMs == 1000));
        var tick = resolver.Describe(events.Single(o => o.Kind == CatchObjectKind.Droplet));
        var tail = resolver.Describe(events.Single(o => o.Kind == CatchObjectKind.Fruit && o.TimeMs == 1500));
        Check(head.NormalSet == 2 && head.AdditionSet == 3 && head.Additions == 8 && head.Index == 2 && head.Volume == 70, "Export slider head sample");
        Check(tick.NormalSet == 2 && tick.Index == 2 && tick.Volume == 35, "Export tick sample");
        Check(tail.NormalSet == 3 && tail.Additions == 2 && tail.Index == 3 && tail.Volume == 60, "Export slider tail sample");
        var unmatched = resolver.Describe(events.Single(o => o.TimeMs == 2000));
        Check(unmatched.Additions == 8 && unmatched.NormalSet == 1 && unmatched.Index == 0 && unmatched.Volume == 100, "Unmatched target sound remains unchanged");
        var originalEvents = CatchStreamConverter.Convert(target).Objects;
        Check(events.Select(e => (e.Kind,e.TimeMs,e.X)).SequenceEqual(originalEvents.Select(e => (e.Kind,e.TimeMs,e.X))), "Export sound changes preserve geometry and event times");
        Check(ProjectSerializer.Read(ProjectSerializer.Serialize(result.Document)).ContentEquals(result.Document), "Persist overrides");
        var project = BeatmapProject.FromDocuments([result.Document]);
        Check(ProjectSerializer.ReadProject(ProjectSerializer.Serialize(project)).Difficulties[0].Document.ContentEquals(result.Document), "Persist multi-diff overrides");
        var history = new EditorHistory(target); history.RestoreVersion("Copy",result.Document); history.Undo();
        Check(history.Document.ContentEquals(target), "Undo copy"); history.Redo(); Check(history.Document.ContentEquals(result.Document), "Redo copy");
        var cleared = HitsoundCopier.Clear(result.Document);
        var clearExport = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(cleared).Text);
        var clearEvents = CatchStreamConverter.Convert(clearExport).Objects;
        var clearResolver = new HitsoundResolver(clearExport,clearEvents);
        Check(clearEvents.Where(o => o.Kind is CatchObjectKind.Fruit or CatchObjectKind.Droplet).All(o => clearResolver.Describe(o) == new HitSampleSettings()), "Clear to normal default sounds");
        var mismatch=target.DeepClone(); mismatch.TimingPoints[0].Meter=3;
        Check(!HitsoundCopier.TimingMatches(source,mismatch), "Reject red timing mismatch");
        var sourceSlider = Read("100,192,1000,2,0,L|200:192,1,100,0|8,1:1|3:3,0:0:0:0:",
            "0,500,4,1,0,100,1,0\n1000,-50,4,1,2,80,0,0");
        var fruitTarget=Read("100,192,1250,1,0,0:0:0:0:\n100,192,1500,1,4,0:0:0:0:");
        var svCopy=HitsoundCopier.Copy(sourceSlider,fruitTarget);
        Check(svCopy.MatchedEvents==1, "Source SV determines real tail at 1250, not 1500");
        Check(svCopy.Document.HitsoundOverrides.Single().Sample.Additions==8, "Source SV tail sound copied");
        var stream=target.DeepClone(); stream.ImportedSliders.Clear();
        var track=new CurveTrack { StreamSnapDivisor=2 }; track.Nodes.Add(new Anchor{TimeMs=1000,X=100}); track.Nodes.Add(new Anchor{TimeMs=1500,X=200}); stream.Tracks.Add(track);
        var streamCopy=HitsoundCopier.Copy(source,stream);
        var streamExport=OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(streamCopy.Document).Text);
        Check(streamExport.Fruits.Select(f=>ObjectFlags.Sounds(streamExport,f.Id)[0]).SequenceEqual(new[]{8,0,2,8}), "Stream fruits have independent copied samples");
        ObjectStructureEditing.BreakStreams(streamCopy.Document,[track.Id]);
        Check(streamCopy.Document.HitsoundOverrides.All(o=>streamCopy.Document.Fruits.Any(f=>f.Id==o.SourceId)),"Breaking streams transfers copied samples to child identities");
        var broken = OsuBeatmapReader.Read(OsuBeatmapWriter.Serialize(streamCopy.Document).Text);
        Check(broken.Fruits.Select(f=>ObjectFlags.Sounds(broken,f.Id)[0]).SequenceEqual(new[]{8,0,2,8}),"Breaking streams preserves copied sound flags");
        ObjectFlags.SetSound(result.Document,result.Document.ImportedSliders[0].Id,8,false,0);
        Check(!ObjectFlags.Sounds(result.Document,result.Document.ImportedSliders[0].Id,0).Any(s=>(s&8)!=0),"Existing sound controls update copied samples");
        Tolerance();
        Resources();
    }

    private static void Tolerance()
    {
        var source = Read("100,192,1000,1,8,0:0:0:0:\n100,192,1004,1,2,0:0:0:0:");
        var target = Read("100,192,998,1,0,0:0:0:0:\n100,192,1002,1,0,0:0:0:0:\n100,192,1003,1,0,0:0:0:0:\n100,192,1004,1,0,0:0:0:0:\n100,192,1007,1,4,0:0:0:0:");
        var result = HitsoundCopier.Copy(source,target);
        Check(result.MatchedEvents==4,"Inclusive 2ms tolerance and reject outside window");
        Check(result.Document.HitsoundOverrides.Select(o=>o.Sample.Additions).SequenceEqual(new[]{8,8,2,2}),
            "Nearest source, earlier on tie, exact match preferred");
        Check(!result.Document.HitsoundOverrides.Any(o=>o.SourceId==target.Fruits.Last().Id),"Unmatched target retained");
        var rounded = Read("100,192,1000,1,0,0:0:0:0:","315,434.782608695652,4,1,0,100,1,0");
        var authored = rounded.DeepClone(); authored.TimingPoints[0].BeatLengthMs=60000.0/138;
        Check(HitsoundCopier.TimingMatches(rounded,authored),"Tolerate BPM round-trip precision at 138 BPM");
        authored.TimingPoints[0].BeatLengthMs+=.001;
        Check(!HitsoundCopier.TimingMatches(rounded,authored),"Do not accept changed BPM");
        authored=rounded.DeepClone(); authored.TimingPoints[0].TimeMs+=1;
        Check(!HitsoundCopier.TimingMatches(rounded,authored),"Object tolerance does not loosen red timing offsets");
        Check(HitsoundCopier.Copy(Read(""),target).MatchedEvents==0,"Empty source skips all events");
    }

    private static void Resources()
    {
        string root=Path.Combine("artifacts","hitsound-copier",Guid.NewGuid().ToString("N"));
        string a=Path.GetFullPath(Path.Combine(root,"source")), b=Path.GetFullPath(Path.Combine(root,"target"));
        Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        var source=Read("100,192,1000,1,8,1:1:2:70:"); source.SourcePath=Path.Combine(a,"source.osu");
        var target=Read("100,192,1002,1,0,0:0:0:0:"); target.SourcePath=Path.Combine(b,"target.osu");
        File.WriteAllBytes(Path.Combine(a,"normal-hitnormal2.wav"),[1,2,3]);
        File.WriteAllBytes(Path.Combine(a,"normal-hitclap2.wav"),[4,5,6]);
        File.WriteAllBytes(Path.Combine(a,"song.mp3"),[7,8,9]);
        File.WriteAllBytes(Path.Combine(b,"normal-hitnormal2.wav"),[9,9]);
        var result=HitsoundCopier.Copy(source,target).Document;
        var second=target.DeepClone(); second.Fruits[0].Id=Guid.NewGuid();
        var secondResult=HitsoundCopier.Copy(source,second).Document;
        var batchPlan=HitsoundResourcePlan.CopyBatch(source,[(result,target),(secondResult,second)],[target,second]);
        Check(batchPlan.DisplayNames.Count==2 && result.HitsoundOverrides.Single().Sample.Index==secondResult.HitsoundOverrides.Single().Sample.Index,
            "Batch shares one renumbered sample group and includes files for 2ms matches");
        batchPlan.Apply(); batchPlan.Restore();
        result=HitsoundCopier.Copy(source,target).Document;
        var plan=HitsoundResourcePlan.Copy(source,result,target); plan.Apply();
        int index=result.HitsoundOverrides.Single().Sample.Index;
        Check(index>2 && File.ReadAllBytes(Path.Combine(b,$"normal-hitnormal{index}.wav")).SequenceEqual(new byte[]{1,2,3}), "Renumber conflicting sample group");
        Check(File.ReadAllBytes(Path.Combine(b,"normal-hitnormal2.wav")).SequenceEqual(new byte[]{9,9}), "Preserve existing conflict");
        Check(!File.Exists(Path.Combine(b,"song.mp3")), "Only copy used hitsound files");
        plan.Restore(); Check(!File.Exists(Path.Combine(b,$"normal-hitnormal{index}.wav")),"Undo copied files"); plan.Apply();
        var fileHistory = new EditorHistory(target);
        fileHistory.RestoreVersion("Copy",result,(redo,before,after)=> { if(redo) plan.Apply(); else plan.Restore(); });
        string copiedPath=Path.Combine(b,$"normal-hitnormal{index}.wav");
        File.WriteAllBytes(copiedPath,[42]);
        bool rejected=false;
        try { fileHistory.Undo(); } catch(IOException) { rejected=true; }
        Check(rejected && fileHistory.Document.ContentEquals(result) && fileHistory.CanUndo,"Failed file undo preserves history and document");
        File.WriteAllBytes(copiedPath,[1,2,3]);
        fileHistory.Undo(); fileHistory.Redo();
        Check(File.ReadAllBytes(copiedPath).SequenceEqual(new byte[]{1,2,3}),"File undo redo retains samples");
        var shared=result.DeepClone();
        plan.Restore([shared]); Check(File.Exists(copiedPath),"Undo retains copied files now shared by another Diff");
        var deletion=HitsoundResourcePlan.Delete(result,[result,shared]);
        Check(deletion.DisplayNames.Count==0,"Protect other Diff references");
        deletion=HitsoundResourcePlan.Delete(result,[result]); deletion.Apply();
        Check(!File.Exists(Path.Combine(b,$"normal-hitnormal{index}.wav")),"Delete unshared used sound");
        deletion.Restore(); Check(File.Exists(Path.Combine(b,$"normal-hitnormal{index}.wav")),"Restore deleted sound");
        var custom=Read("100,192,1000,1,0,1:1:0:80:custom.wav"); custom.SourcePath=source.SourcePath;
        File.WriteAllBytes(Path.Combine(a,"custom.wav"),[5,6]); File.WriteAllBytes(Path.Combine(b,"custom.wav"),[8,9]);
        result=HitsoundCopier.Copy(custom,target).Document; plan=HitsoundResourcePlan.Copy(custom,result,target); plan.Apply();
        Check(result.HitsoundOverrides.Single().Sample.FileName!="custom.wav","Rename conflicting explicit custom file");
        Check(File.ReadAllBytes(Path.Combine(b,"custom.wav")).SequenceEqual(new byte[]{8,9}),"Preserve custom conflict");
        File.WriteAllText(Path.Combine(a,"standard.osu"),"osu file format v14\n[General]\nMode:0\n[TimingPoints]\n0,500,4,1,0,100,1,0\n[HitObjects]\n100,192,1000,1,8,0:0:0:0:");
        Check(HitsoundCopier.ReadSource(Path.Combine(a,"standard.osu")).Fruits.Count==1,"Read standard hitsound source without importing it");
        Check(File.ReadAllText(Path.Combine(a,"standard.osu")).Contains("Mode:0"),"Source file stays unchanged");
        Directory.Delete(Path.GetFullPath(root),true);
    }
}
