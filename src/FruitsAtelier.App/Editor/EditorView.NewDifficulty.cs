using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool newDifficultyUseSource, newDifficultyTemplateTab;
    private int newDifficultySource, newDifficultyTemplate;
    private string newDifficultyName = "";
    private bool newDifficultySourcesOpen;
    private int newDifficultySourceScroll, newDifficultySourceFocus;
    private Rect newDifficultySourceBounds, newDifficultySourceList;
    private int NewDifficultySourceCount => newDifficultyTemplateTab ? LibrarySettings.MapTemplates.Count : difficulties.Count;
    private int NewDifficultySelectedSource => newDifficultyTemplateTab ? newDifficultyTemplate : newDifficultySource;

    private void SelectNewDifficultySource(int index)
    {
        if (newDifficultyTemplateTab) newDifficultyTemplate = index;
        else newDifficultySource = index;
        newDifficultySourcesOpen = false;
    }

    private bool NewDifficultySourcePointer(float x, float y)
    {
        if (!newDifficultySourcesOpen || newDifficultySourceBounds.Contains(x, y) || newDifficultySourceList.Contains(x, y)) return false;
        newDifficultySourcesOpen = false;
        return true;
    }
    private bool CanCreateDifficulty => !string.IsNullOrWhiteSpace(newDifficultyName)
        && (!newDifficultyUseSource || !newDifficultyTemplateTab || newDifficultyTemplate < LibrarySettings.MapTemplates.Count);

    private void NewDifficultyText(string text)
    {
        if (newDifficultySourcesOpen) return;
        text = new string(text.Where(ch => !char.IsControl(ch)).ToArray());
        newDifficultyName = InsertInput("newDiff:0", newDifficultyName, text, 128);
    }

    private void NewDifficultyKey(int key, bool ctrl, bool shift)
    {
        if (newDifficultySourcesOpen)
        {
            if (key == 27 || key == 9) { newDifficultySourcesOpen = false; return; }
            if (key == 13) { SelectNewDifficultySource(newDifficultySourceFocus); return; }
            if (key is 38 or 40 or 36 or 35)
            {
                int count = NewDifficultySourceCount;
                newDifficultySourceFocus = key == 36 ? 0 : key == 35 ? count - 1
                    : Math.Clamp(newDifficultySourceFocus + (key == 38 ? -1 : 1), 0, count - 1);
                newDifficultySourceScroll = Math.Clamp(newDifficultySourceScroll,
                    Math.Max(0, newDifficultySourceFocus - 5), newDifficultySourceFocus);
            }
            return;
        }
        if (key == 27) { AnswerDiscard(2); return; }
        if (key == 13) { if (CanCreateDifficulty) AnswerDiscard(6); return; }
        if (key == 9)
        {
            SelectInput("newDiff:0", newDifficultyName);
            return;
        }
        InputKey("newDiff:0", ref newDifficultyName, key, ctrl, shift, 128, () => RequestPasteField?.Invoke());
    }

    private void DrawNewDifficulty(ICanvas c)
    {
        float w = Math.Min(660, width - 32), h = 552;
        float x = (width - w) / 2, y = (height - h) / 2, inner = w - 44;
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("project.add"), x + 22, y + 20, 18, Foreground, inner, true);
        c.Text(L.Get("difficulty.name"), x + 22, y + 56, 13, Muted, inner);
        var nameBounds = new Rect(x + 22, y + 82, inner, 34);
        c.Fill(nameBounds, Surface, 5); c.Stroke(nameBounds, Accent, radius: 5);
        DrawInputText(c, new(nameBounds.X + 10, nameBounds.Y + 8, inner - 20, 20), newDifficultyName, 14, true, "newDiff:0");
        hits.Add(new(nameBounds, () => FocusInput("newDiff:0", newDifficultyName, mouseX), true));
        ToggleSwitch(c, new(x + 22, y + 124, inner, 34), L.Get("difficulty.useSource"), newDifficultyUseSource,
            () => { newDifficultyUseSource = !newDifficultyUseSource; newDifficultySourcesOpen = false; });
        var sourceRegion = new Rect(x + 22, y + 166, inner, 216);
        c.Fill(new(sourceRegion.X + 1, sourceRegion.Y + 1, sourceRegion.Width - 2, 43), Surface, 5);
        c.Fill(new(sourceRegion.X + 1, sourceRegion.Y + 7, sourceRegion.Width - 2, 37), Surface);
        c.Stroke(sourceRegion, Grid, radius: 6);
        c.Line(sourceRegion.X + 1, y + 210, sourceRegion.Right - 1, y + 210, Grid);
        float sourceX = x + 32, sourceWidth = inner - 20;
        SourceTab(new(sourceX, y + 172, (sourceWidth - 6) / 2, 38), "difficulty.copyTab", false);
        SourceTab(new(sourceX + (sourceWidth + 6) / 2, y + 172, (sourceWidth - 6) / 2, 38), "difficulty.templateTab", true);
        int count = NewDifficultySourceCount, selected = NewDifficultySelectedSource;
        string label = count == 0 ? L.Get("difficulty.noTemplates") : SourceName(selected);
        newDifficultySourceBounds = new(sourceX, y + 222, sourceWidth, 38);
        c.Fill(newDifficultySourceBounds, Surface, 5);
        c.Stroke(newDifficultySourceBounds, newDifficultySourcesOpen ? Accent : Grid, radius: 5);
        Button(c, newDifficultySourceBounds, label, () =>
        {
            newDifficultySourcesOpen = !newDifficultySourcesOpen;
            newDifficultySourceFocus = selected;
            newDifficultySourceScroll = Math.Clamp(selected, 0, Math.Max(0, count - 6));
        }, enabled: newDifficultyUseSource && count > 0, fontSize: 14,
            textPadding: newDifficultyTemplateTab && count > 0 ? 42 : 12, textRightPadding: 30);
        if (newDifficultyTemplateTab && count > 0)
            DrawTemplateIcon(c, LibrarySettings.MapTemplates[selected].Icon, new(sourceX + 10, y + 230, 22, 22));
        c.Text("▾", newDifficultySourceBounds.Right - 23, y + 233, 13, Muted, 18);
        if (count > 0)
        {
            var settings = newDifficultyTemplateTab ? LibrarySettings.MapTemplates[selected]
                : MapTemplate.Capture(label, difficulties[selected].History.Document);
            c.Text(L.Get("difficulty.parameters", settings.ApproachRate, settings.CircleSize, settings.HP, settings.OD,
                settings.SliderMultiplier, settings.SliderTickRate), sourceX, y + 278, 13, Muted, sourceWidth);
        }
        if (newDifficultyTemplateTab)
            Button(c, new(sourceX, y + 328, sourceWidth, 36), L.Get("template.manage"), OpenMapTemplates, enabled: newDifficultyUseSource);
        if (!newDifficultyUseSource)
            c.Fill(new(sourceRegion.X + 1, sourceRegion.Y + 1, sourceRegion.Width - 2, sourceRegion.Height - 2), Background, 5, .55f);
        ToggleSwitch(c, new(x + 22, y + 404, inner, 34), L.Get("song.faOnly"), newDifficultyFaOnly,
            () => newDifficultyFaOnly = !newDifficultyFaOnly);
        Button(c, new(x + 22, y + h - 60, (inner - 10) / 2, 36), L.Get("mac.cancel"), () => AnswerDiscard(2));
        Button(c, new(x + 32 + (inner - 10) / 2, y + h - 60, (inner - 10) / 2, 36), L.Get("project.add"),
            () => { if (CanCreateDifficulty) AnswerDiscard(6); }, true, CanCreateDifficulty);

        if (newDifficultySourcesOpen && newDifficultyUseSource && count > 0)
        {
            int visible = Math.Min(6, count);
            newDifficultySourceScroll = Math.Clamp(newDifficultySourceScroll, 0, count - visible);
            newDifficultySourceList = new(sourceX, newDifficultySourceBounds.Bottom + 4, sourceWidth, visible * 32 + 8);
            c.Fill(newDifficultySourceList, Surface, 5); c.Stroke(newDifficultySourceList, Grid, radius: 5);
            hits.Add(new(newDifficultySourceList, () => { }, true));
            for (int row = 0; row < visible; row++)
            {
                int index = newDifficultySourceScroll + row;
                var bounds = new Rect(sourceX + 4, newDifficultySourceList.Y + 4 + row * 32, sourceWidth - 14, 32);
                Button(c, bounds, SourceName(index), () => SelectNewDifficultySource(index), index == newDifficultySourceFocus,
                    textPadding: newDifficultyTemplateTab ? 38 : 9);
                if (newDifficultyTemplateTab)
                    DrawTemplateIcon(c, LibrarySettings.MapTemplates[index].Icon, new(bounds.X + 8, bounds.Y + 6, 20, 20));
            }
            if (count > visible)
            {
                float trackHeight = newDifficultySourceList.Height - 8;
                float thumb = trackHeight * visible / count;
                c.Fill(new(newDifficultySourceList.Right - 7, newDifficultySourceList.Y + 4
                    + (trackHeight - thumb) * newDifficultySourceScroll / (count - visible), 3, thumb), Muted, 2);
            }
        }
        string SourceName(int index) => newDifficultyTemplateTab ? LibrarySettings.MapTemplates[index].Name : difficulties[index].Name;

        void SourceTab(Rect bounds, string key, bool template)
        {
            bool active = newDifficultyTemplateTab == template;
            if (active)
            {
                c.Fill(bounds, Panel, 6); c.Stroke(bounds, Grid, radius: 6);
                // The open bottom edge joins the active tab to its content panel.
                c.Fill(new(bounds.X + 1, bounds.Bottom - 6, bounds.Width - 2, 7), Panel);
                c.Line(bounds.X, bounds.Bottom - 6, bounds.X, bounds.Bottom, Grid);
                c.Line(bounds.Right, bounds.Bottom - 6, bounds.Right, bounds.Bottom, Grid);
            }
            else if (newDifficultyUseSource && bounds.Contains(mouseX, mouseY)) c.Fill(bounds, 0x2B3542, 6);
            c.Text(L.Get(key), bounds.X + 12, bounds.Y + 12, 13,
                !newDifficultyUseSource ? 0x5B6777u : active ? Accent : Muted, bounds.Width - 24, active);
            hits.Add(new(bounds, () => { newDifficultyTemplateTab = template; newDifficultySourcesOpen = false; }, newDifficultyUseSource));
        }
    }
}
