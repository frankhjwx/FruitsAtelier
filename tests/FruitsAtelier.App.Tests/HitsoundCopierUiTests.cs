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
                var source=target.DeepClone(); source.Fruits[0].OriginalLine="100,192,1000,1,2,3:3:3:40:";
                var ui=new Ui(); ui.LoadDocument(target); var before=ui.View.Document.DeepClone();
                ui.View.OpenHitsoundCopier(); ui.Paint();
                Check(ui.Canvas.Texts.Any(t=>t.Value==L.Get("copier.title")),"Copier visible");
                ui.Key(90,ctrl:true); Check(ui.View.HitsoundCopierVisible,"Window blocks editing shortcuts");
                ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("song.ok"));
                Check(!ui.View.HitsoundCopierVisible && ObjectFlags.Sounds(ui.View.Document,ui.View.Document.Fruits[0].Id)[0]==0,"Clear applies and closes");
                ui.Key(90,ctrl:true); Check(ui.View.Document.ContentEquals(before),"Clear undo");
                ui.View.OpenHitsoundCopier(); ui.Paint(); ui.ClickText(L.Get("copier.external")); ui.View.SetHitsoundSource(source); ui.Paint();
                ui.ClickText(L.Get("copier.preview")); ui.ClickText(L.Get("copier.newDiff")); ui.ClickText(L.Get("song.ok"));
                Check(ui.View.CaptureProject().Difficulties.Count==2,"New Diff added");
                Check(ui.View.Document.HitsoundOverrides.Single().Sample.Additions==2,"Copied sound on new Diff");
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
            }
        }
        finally { L.SetLanguage(language); }
    }
}
