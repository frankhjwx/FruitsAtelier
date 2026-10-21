using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool newDifficultyUseSource, newDifficultyTemplateTab;
    private int newDifficultySource, newDifficultyTemplate;
    private string newDifficultyName = "";
    private bool CanCreateDifficulty => !string.IsNullOrWhiteSpace(newDifficultyName)
        && (!newDifficultyUseSource || !newDifficultyTemplateTab || newDifficultyTemplate < LibrarySettings.MapTemplates.Count);

    private void NewDifficultyText(string text)
    {
        text = new string(text.Where(ch => !char.IsControl(ch)).ToArray());
        newDifficultyName = InsertInput("newDiff:0", newDifficultyName, text, 128);
    }

    private void NewDifficultyKey(int key, bool ctrl, bool shift)
    {
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
            () => newDifficultyUseSource = !newDifficultyUseSource);
        Button(c, new(x + 22, y + 166, (inner - 10) / 2, 34), L.Get("difficulty.copyTab"),
            () => newDifficultyTemplateTab = false, !newDifficultyTemplateTab);
        Button(c, new(x + 32 + (inner - 10) / 2, y + 166, (inner - 10) / 2, 34), L.Get("difficulty.templateTab"),
            () => newDifficultyTemplateTab = true, newDifficultyTemplateTab);
        int count = newDifficultyTemplateTab ? LibrarySettings.MapTemplates.Count : difficulties.Count;
        int selected = newDifficultyTemplateTab ? newDifficultyTemplate : newDifficultySource;
        string label = count == 0 ? L.Get("difficulty.noTemplates") : newDifficultyTemplateTab
            ? LibrarySettings.MapTemplates[selected].Name : difficulties[selected].Name;
        if (newDifficultyTemplateTab && count > 0)
            DrawTemplateIcon(c, LibrarySettings.MapTemplates[selected].Icon, new(x + 68, y + 222, 24, 24));
        c.Text(label, x + (newDifficultyTemplateTab && count > 0 ? 104 : 68), y + 226, 15, Foreground, inner - 128, true);
        Button(c, new(x + 22, y + 216, 34, 34), "‹", () => Choose(-1), enabled: count > 1);
        Button(c, new(x + w - 56, y + 216, 34, 34), "›", () => Choose(1), enabled: count > 1);
        if (count > 0)
        {
            var settings = newDifficultyTemplateTab ? LibrarySettings.MapTemplates[selected]
                : MapTemplate.Capture(label, difficulties[selected].History.Document);
            c.Text(L.Get("difficulty.parameters", settings.ApproachRate, settings.CircleSize, settings.HP, settings.OD,
                settings.SliderMultiplier, settings.SliderTickRate), x + 22, y + 260, 13, Muted, inner);
        }
        c.Text(L.Get(newDifficultyTemplateTab ? "difficulty.templateHelp" : "difficulty.copyHelp"), x + 22, y + 288, 13, Muted, inner);
        if (newDifficultyTemplateTab)
        {
            Button(c, new(x + 22, y + 332, inner, 36), L.Get("template.manage"), OpenMapTemplates);
        }
        ToggleSwitch(c, new(x + 22, y + 404, inner, 34), L.Get("song.faOnly"), newDifficultyFaOnly,
            () => newDifficultyFaOnly = !newDifficultyFaOnly);
        c.Text(L.Get("song.newDifficultySyncHelp"), x + 22, y + 446, 12, Muted, inner);
        Button(c, new(x + 22, y + h - 60, (inner - 10) / 2, 36), L.Get("mac.cancel"), () => AnswerDiscard(2));
        Button(c, new(x + 32 + (inner - 10) / 2, y + h - 60, (inner - 10) / 2, 36), L.Get("project.add"),
            () => { if (CanCreateDifficulty) AnswerDiscard(6); }, true, CanCreateDifficulty);

        void Choose(int offset)
        {
            if (newDifficultyTemplateTab) newDifficultyTemplate = (selected + offset + count) % count;
            else newDifficultySource = (selected + offset + count) % count;
        }
    }
}
