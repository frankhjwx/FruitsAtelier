using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public int HitsoundCopierInputSession { get; private set; }
    public bool HitsoundCopierVisible { get; private set; }
    public Action? RequestHitsoundSource { get; set; }
    public Action? RequestPasteHitsoundName { get; set; }
    private int copierMode, copierSourceIndex = -1, copierFileScroll, copierSourceScroll;
    private bool copierSourcesOpen, copierFiles, copierNewDiff, copierNameFocused;
    private string copierName = "", copierSourceName = "", copierError = "", copierSummary = "";
    private MapDocument? copierSource, copierResult;
    private Dictionary<Guid,MapDocument>? copierPreviewDocuments;
    private HitsoundResourcePlan? copierResources;
    private Rect copierNameBounds, copierSourceBounds;
    private Rect HitsoundSourceSelectorBounds => new(HitsoundCopierBounds.X+22,HitsoundCopierBounds.Y+106,HitsoundCopierBounds.Width-44,30);
    internal Rect HitsoundCopierBounds => new((width - Math.Min(700, width - 32)) / 2,
        (height - 540) / 2, Math.Min(700, width - 32), 540);

    internal void OpenHitsoundCopier()
    {
        if (LibraryVisible || IsTestplaying || !HasEditorProject || !PrepareFileOperation()) return;
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        copierSourcesOpen = false; copierSourceScroll = copierFileScroll = 0;
        copierMode = 0; copierFiles = copierNewDiff = copierNameFocused = false;
        copierSourceIndex = -1; copierSource = copierResult = null; copierResources = null;
        copierName = L.Get("copier.defaultName",CurrentDifficultyName);
        copierSourceName = copierError = copierSummary = "";
        HitsoundCopierVisible = true; HitsoundCopierInputSession++;
    }

    private void CloseHitsoundCopier()
    {
        HitsoundCopierInputSession++;
        HitsoundCopierVisible = false; copierResult = copierSource = null; copierResources = null;
        copierNameFocused = false; hits.Clear(); fields.Clear();
    }

    public void SetHitsoundSource(MapDocument source)
    {
        if (!HitsoundCopierVisible || copierMode != 1) return;
        copierSource = source.DeepClone();
        copierSourceName = source.SourcePath ?? SongSetup.Get(source, "Metadata", "Version", source.Name);
        InvalidateHitsoundPlan();
    }

    private void InvalidateHitsoundPlan()
    { copierResult = null; copierResources = null; copierPreviewDocuments = null; copierError = copierSummary = ""; copierFileScroll = 0; }

    private void DrawHitsoundCopier(ICanvas c)
    {
        if (!HitsoundCopierVisible) return;
        hits.Clear(); fields.Clear(); var r = HitsoundCopierBounds;
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("copier.title"), r.X + 22, r.Y + 16, 19, Foreground, r.Width - 80, true);
        Button(c, new(r.Right - 54, r.Y + 10, 32, 28), "\u00d7", CloseHitsoundCopier);
        float w = (r.Width - 44) / 3;
        string[] modes = ["copier.clear", "copier.external", "copier.set"];
        for (int i = 0; i < 3; i++)
        {
            int mode = i;
            Button(c, new(r.X + 22 + i*w, r.Y + 58, w - 6, 32), L.Get(modes[i]), () =>
            { copierMode = mode; copierSourcesOpen = false; copierFiles = false; copierSource = null; copierSourceIndex = -1; copierSourceName = ""; InvalidateHitsoundPlan(); }, copierMode == i);
        }
        if (copierMode == 0)
            c.Text(L.Get("copier.clearHelp"), r.X + 22, r.Y + 112, 13, Muted, r.Width - 44);
        else if (copierMode == 1)
        {
            Button(c, new(r.X + 22, r.Y + 106, 180, 30), L.Get("copier.choose"), () => RequestHitsoundSource?.Invoke());
            c.Text(copierSourceName, r.X + 212, r.Y + 113, 12, Foreground, r.Width - 234);
        }
        else
        {
            var available = difficulties.Select((d,i) => i).Where(i => i != activeDifficulty).ToArray();
            string name = copierSourceIndex >= 0 ? difficulties[copierSourceIndex].Name : L.Get("copier.chooseDiff");
            Button(c, new(r.X + 22, r.Y + 106, r.Width - 44, 30), name,
                () => copierSourcesOpen = !copierSourcesOpen, enabled: available.Length > 0);
        }
        if (copierMode != 2)
            TimingCheck(c, new(r.X + 22, r.Y + 152, r.Width - 44, 30),
                copierMode == 0 ? "copier.deleteFiles" : "copier.copyFiles", copierFiles,
                () => { copierFiles = !copierFiles; InvalidateHitsoundPlan(); });
        if (copierMode != 0)
            c.Text(L.Get("copier.matchHelp"), r.X + 22, r.Y + 192, 12, Muted, r.Width - 44);
        Button(c, new(r.X + 22, r.Y + 226, 180, 30), L.Get("copier.overwrite"), () => { copierNewDiff = false; copierNameFocused = false; }, !copierNewDiff);
        Button(c, new(r.X + 212, r.Y + 226, 180, 30), L.Get("copier.newDiff"), () => copierNewDiff = true, copierNewDiff);
        copierNameBounds = new(r.X + 22, r.Y + 268, r.Width - 44, 30);
        if (copierNewDiff)
        {
            c.Fill(copierNameBounds, Surface, 4);
            DrawInputText(c, new(copierNameBounds.X+8,copierNameBounds.Y+6,copierNameBounds.Width-16,20), copierName, 13, copierNameFocused, "copier:name");
            hits.Add(new(copierNameBounds, () => { copierNameFocused = true; HitsoundCopierInputSession++; SelectInput("copier:name", copierName); }, true));
        }
        c.Text(copierError.Length > 0 ? copierError : copierSummary, r.X + 22, r.Y + 314, 12,
            copierError.Length > 0 ? Error : Foreground, r.Width - 44);
        if (copierResources is { } resources)
        {
            var names = resources.DisplayNames;
            copierFileScroll = Math.Clamp(copierFileScroll, 0, Math.Max(0,names.Count - 6));
            for (int i = 0; i < Math.Min(6,names.Count - copierFileScroll); i++)
                c.Text(names[copierFileScroll+i], r.X+22, r.Y+350+i*20, 11, Muted, r.Width-44);
        }
        Button(c, new(r.X + 22, r.Bottom - 48, 110, 32), L.Get("mac.cancel"), CloseHitsoundCopier);
        Button(c, new(r.Right - 330, r.Bottom - 48, 140, 32), L.Get("copier.preview"), PreviewHitsoundCopy);
        Button(c, new(r.Right - 180, r.Bottom - 48, 158, 32), L.Get("song.ok"), ApplyHitsoundCopy, enabled: copierResult is not null);
        if (copierSourcesOpen && copierMode == 2)
        {
            var available = difficulties.Select((d,i) => i).Where(i => i != activeDifficulty).ToArray();
            copierSourceScroll = Math.Clamp(copierSourceScroll,0,Math.Max(0,available.Length-7));
            int rows = Math.Min(7,available.Length-copierSourceScroll);
            copierSourceBounds = new(r.X+22,r.Y+140,r.Width-44,rows*30+8);
            c.Fill(copierSourceBounds, Surface,4); c.Stroke(copierSourceBounds,Muted,radius:4);
            for(int row=0;row<rows;row++)
            {
                int index=available[copierSourceScroll+row];
                Button(c,new(copierSourceBounds.X+4,copierSourceBounds.Y+4+row*30,copierSourceBounds.Width-8,28),
                    difficulties[index].Name,()=>
                    {
                        copierSourceIndex=index; copierSourcesOpen=false; InvalidateHitsoundPlan();
                        copierSource=difficulties[index].History.Document.DeepClone();
                    },index==copierSourceIndex);
            }
        }
    }

    internal void PreviewHitsoundCopy()
    {
        InvalidateHitsoundPlan();
        try
        {
            if (copierMode != 0 && copierSource is null) throw new InvalidOperationException(L.Get("copier.sourceRequired"));
            copierPreviewDocuments = difficulties.ToDictionary(d=>d.Id,d=>d.History.Document.DeepClone());
            int count = 0;
            if (copierMode == 0) copierResult = HitsoundCopier.Clear(Document);
            else { var result = HitsoundCopier.Copy(copierSource!, Document, compensateTinyDroplets); copierResult = result.Document; count = result.MatchedEvents; }
            copierResources = copierFiles ? copierMode == 0
                ? HitsoundResourcePlan.Delete(Document, difficulties.Select(d => d.History.Document))
                : HitsoundResourcePlan.Copy(copierSource!, copierResult, Document, difficulties.Select(d=>d.History.Document))
                : HitsoundResourcePlan.Empty;
            copierSummary = L.Get("copier.summary", count, copierResources.DisplayNames.Count);
        }
        catch (Exception error) { copierResult = null; copierResources = null; copierPreviewDocuments = null; copierError = error.Message; }
    }

    internal void ApplyHitsoundCopy()
    {
        if (copierResult is null || copierResources is null) return;
        if (copierPreviewDocuments is null || copierPreviewDocuments.Count != difficulties.Count
            || difficulties.Any(d=>!copierPreviewDocuments.TryGetValue(d.Id,out var before) || !before.ContentEquals(d.History.Document)))
        { InvalidateHitsoundPlan(); copierError=L.Get("copier.stalePreview"); return; }
        if (copierNewDiff && (string.IsNullOrWhiteSpace(copierName) || copierName.Any(char.IsControl)
            || difficulties.Any(d => d.Name.Equals(copierName.Trim(), StringComparison.OrdinalIgnoreCase)) || difficulties.Count >= 256))
        { copierError = L.Get("copier.nameRequired"); return; }
        if (copierNewDiff && copierMode == 0 && copierFiles)
        { copierError = L.Get("copier.keepOriginalFiles"); return; }
        var result = copierResult; var resources = copierResources; bool applied = false;
        try
        {
            if (copierMode == 0 && copierFiles && !HitsoundResourcePlan.Delete(Document,difficulties.Select(d=>d.History.Document)).DisplayNames.SequenceEqual(resources.DisplayNames))
                throw new IOException(L.Get("copier.stalePreview"));
            resources.Apply(); applied = true;
            if (copierNewDiff)
            {
                SongSetup.Set(result, "Metadata", "Version", copierName.Trim());
                SongSetup.Set(result, "Metadata", "BeatmapID", "0");
                var added = new ProjectDifficulty { Name = copierName.Trim(), Document = result.DeepClone() };
                difficulties.Add(new DifficultySession(added));
                WorkspaceSession?.Manifest.Difficulties.Add(new WorkspaceDifficulty { Id = added.Id, Name = added.Name });
                projectStructureDirty = true;
                CloseHitsoundCopier(); SwitchDifficultyCore(difficulties.Count - 1, reloadAudio: true);
            }
            else
            {
                history.RestoreVersion(L.Get("copier.title"), result, (redo, before, after) => { if (redo) resources.Apply(difficulties.Select(d=>d.History.Document)); else resources.Restore(difficulties.Select(d=>d.History.Document)); });
                CloseHitsoundCopier(); convertedSnapshot = null;
            }
            ResetHitsounds(); PreloadProjectHitsounds();
        }
        catch (Exception error)
        {
            if (applied)
                try { resources.Restore(); } catch (Exception restoreError) { error = new IOException(error.Message, restoreError); }
            copierError = error.Message;
        }
    }

    private void HitsoundCopierKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { if(copierSourcesOpen) copierSourcesOpen=false; else CloseHitsoundCopier(); return; }
        if (copierNameFocused && copierNewDiff)
        {
            if (ctrl && key == 86) { RequestPasteHitsoundName?.Invoke(); return; }
            InputKey("copier:name", ref copierName, key, ctrl, shift, 128);
        }
    }

    public void PasteHitsoundName(string text, int? session = null)
    {
        if (!HitsoundCopierVisible || !copierNameFocused || !copierNewDiff || session is { } token && token != HitsoundCopierInputSession) return;
        copierName = InsertInput("copier:name", copierName, new string(text.Where(c => !char.IsControl(c)).ToArray()), 128);
    }
}
