using System.Globalization;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    public bool MapTemplatesVisible { get; private set; }
    private List<MapTemplate> templateDraft = [];
    private int templateIndex = -1, templateField, templatePage;
    private readonly string[] templateValues = new string[7];
    private static readonly int[] templateFieldOrder = [0, 3, 2, 1, 4, 5, 6];
    private string templateError = "";
    private static readonly double?[] templateIconStars = [null, .1, 1.25, 2, 2.5, 3.3, 4.2, 4.9, 5.8, 6.7, 7.7, 9];

    private static void DrawTemplateIcon(ICanvas c, MapTemplateIcon icon, Rect bounds)
    {
        if (icon >= MapTemplateIcon.Violet)
            c.Circle(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, bounds.Width / 2, 0xE7EBF2);
        c.Image(catchIconPath, bounds, DifficultyColour(templateIconStars[(int)icon]));
    }

    public void OpenMapTemplates()
    {
        if (MapTemplatesVisible || (!addDifficultyConfirmation && (DiscardConfirmationVisible || !PrepareFileOperation()))) return;
        templateDraft = LibrarySettings.MapTemplates.Select(t => t with { }).ToList();
        templateIndex = -1; templatePage = 0; templateError = "";
        if (templateDraft.Count > 0) SelectMapTemplate(0);
        MapTemplatesVisible = true; menu = -1;
        hits.Clear(); fields.Clear();
    }

    private void SelectMapTemplate(int index)
    {
        templateIndex = index; templateField = 0; templateError = "";
        var t = templateDraft[index];
        string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string[] values = [t.Name, N(t.ApproachRate), N(t.CircleSize), t.HP, t.OD, N(t.SliderMultiplier), N(t.SliderTickRate)];
        values.CopyTo(templateValues, 0);
        SelectInput("template:0", templateValues[0]);
    }

    private bool CommitMapTemplate()
    {
        if (templateIndex < 0) return true;
        string name = templateValues[0].Trim();
        double[] numbers = new double[6];
        bool valid = name.Length > 0 && !templateDraft.Where((_, i) => i != templateIndex)
            .Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < numbers.Length; i++)
            valid &= double.TryParse(templateValues[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])
                && double.IsFinite(numbers[i]) && (i >= 4 ? numbers[i] > 0 : numbers[i] >= 0 && numbers[i] <= 10);
        if (!valid) { templateError = L.Get("template.invalid"); return false; }
        templateDraft[templateIndex] = new MapTemplate { Name = name, ApproachRate = numbers[0], CircleSize = numbers[1],
            Icon = templateDraft[templateIndex].Icon,
            HP = numbers[2].ToString("R", CultureInfo.InvariantCulture), OD = numbers[3].ToString("R", CultureInfo.InvariantCulture),
            SliderMultiplier = numbers[4], SliderTickRate = numbers[5] };
        templateError = ""; return true;
    }

    private void AddMapTemplate(bool fromCurrent)
    {
        if (!CommitMapTemplate()) return;
        int suffix = 1;
        string name;
        do { name = L.Get("template.defaultName", suffix++); }
        while (templateDraft.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)));
        templateDraft.Add(fromCurrent ? MapTemplate.Capture(name, Document) : new MapTemplate { Name = name });
        SelectMapTemplate(templateDraft.Count - 1); templatePage = templateIndex / 8;
    }

    private void CloseMapTemplates(bool save)
    {
        if (save)
        {
            if (!CommitMapTemplate()) return;
            LibrarySettings.MapTemplates = templateDraft.Select(t => t with { }).ToList();
            newDifficultyTemplate = Math.Clamp(templateIndex, 0, Math.Max(0, templateDraft.Count - 1));
            RequestViewPreference?.Invoke();
        }
        MapTemplatesVisible = false; templateDraft = []; templateIndex = -1;
        hits.Clear(); fields.Clear();
        if (addDifficultyConfirmation) SelectInput("newDiff:0", newDifficultyName);
    }

    private void MapTemplatesText(string text)
    {
        if (templateIndex < 0) return;
        text = new string(text.Where(ch => !char.IsControl(ch)).ToArray());
        templateValues[templateField] = InsertInput("template:" + templateField, templateValues[templateField], text, templateField == 0 ? 128 : 24);
        templateError = "";
    }

    private void MapTemplatesKey(int key, bool ctrl, bool shift)
    {
        if (key == 27) { CloseMapTemplates(false); return; }
        if (key == 13) { CloseMapTemplates(true); return; }
        if (templateIndex < 0) return;
        if (key == 9)
        {
            templateField = templateFieldOrder[(Array.IndexOf(templateFieldOrder, templateField) + (shift ? 6 : 1)) % 7];
            SelectInput("template:" + templateField, templateValues[templateField]); return;
        }
        InputKey("template:" + templateField, ref templateValues[templateField], key, ctrl, shift,
            templateField == 0 ? 128 : 24, () => RequestPasteField?.Invoke());
        templateError = "";
    }

    private void DrawMapTemplates(ICanvas c)
    {
        hits.Clear(); fields.Clear();
        float w = Math.Min(860, width - 32), h = Math.Min(552, height - 32);
        float x = (width - w) / 2, y = (height - h) / 2, left = x + 248, inner = w - 270;
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(new(x, y, w, h), Panel, 8); c.Stroke(new(x, y, w, h), Accent, 2, 8);
        c.Text(L.Get("template.manage"), x + 22, y + 20, 20, Foreground, w - 44, true);
        c.Line(x + 230, y + 66, x + 230, y + h - 82, Grid);
        templatePage = Math.Clamp(templatePage, 0, Math.Max(0, (templateDraft.Count - 1) / 8));
        for (int i = templatePage * 8; i < Math.Min(templateDraft.Count, (templatePage + 1) * 8); i++)
        {
            int target = i;
            var row = new Rect(x + 22, y + 70 + (i % 8) * 36, 190, 32);
            Button(c, row, templateDraft[i].Name, () => { if (CommitMapTemplate()) SelectMapTemplate(target); },
                i == templateIndex, textPadding: 38);
            DrawTemplateIcon(c, templateDraft[i].Icon, new(row.X + 8, row.Y + 6, 20, 20));
        }
        if (templateDraft.Count > 8)
        {
            Button(c, new(x + 22, y + 368, 88, 30), "‹", () => templatePage--, enabled: templatePage > 0);
            Button(c, new(x + 124, y + 368, 88, 30), "›", () => templatePage++, enabled: (templatePage + 1) * 8 < templateDraft.Count);
        }
        Button(c, new(x + 22, y + h - 144, 190, 32), L.Get("template.new"), () => AddMapTemplate(false));
        Button(c, new(x + 22, y + h - 104, 190, 32), L.Get("template.fromCurrent"), () => AddMapTemplate(true),
            enabled: HasEditorProject && !LibraryVisible);
        if (templateIndex >= 0)
        {
            Field(0, L.Get("template.name"), left, y + 70, inner);
            string[] labels = ["HP", "CS", "AR", "OD", "SV", "STR"];
            for (int i = 0; i < labels.Length; i++)
                Field(templateFieldOrder[i + 1], labels[i], left + (i % 2) * ((inner + 16) / 2), y + 150 + (i / 2) * 78, (inner - 16) / 2);
            c.Text(L.Get("template.icon"), left, y + 374, 13, Muted, inner);
            for (int i = 0; i < templateIconStars.Length; i++)
            {
                var icon = (MapTemplateIcon)i;
                var r = new Rect(left + i * 36, y + 398, 32, 32);
                bool selected = templateDraft[templateIndex].Icon == icon;
                c.Fill(r, selected ? 0x31494Bu : Surface, 4);
                c.Stroke(r, selected ? Accent : Grid, radius: 4);
                DrawTemplateIcon(c, icon, new(r.X + 6, r.Y + 6, 20, 20));
                hits.Add(new(r, () => templateDraft[templateIndex].Icon = icon, true));
            }
            Button(c, new(left, y + h - 110, 160, 32), L.Get("template.delete"), () =>
            {
                templateDraft.RemoveAt(templateIndex);
                if (templateDraft.Count > 0) SelectMapTemplate(Math.Min(templateIndex, templateDraft.Count - 1));
                else { templateIndex = -1; templateError = ""; }
            });
        }
        else c.Text(L.Get("difficulty.noTemplates"), left, y + 90, 14, Muted, inner);
        c.Text(templateError, left, y + h - 76, 12, Error, inner);
        Button(c, new(x + w - 282, y + h - 58, 120, 36), L.Get("mac.cancel"), () => CloseMapTemplates(false));
        Button(c, new(x + w - 142, y + h - 58, 120, 36), L.Get("mac.save"), () => CloseMapTemplates(true), true);

        void Field(int id, string label, float fx, float fy, float fw)
        {
            c.Text(label, fx, fy, 13, Muted, fw);
            var r = new Rect(fx, fy + 24, fw, 34);
            c.Fill(r, Surface, 5); c.Stroke(r, templateField == id ? Accent : Grid, radius: 5);
            DrawInputText(c, new(r.X + 10, r.Y + 8, r.Width - 20, 20), templateValues[id], 14, templateField == id, "template:" + id);
            hits.Add(new(r, () => { templateField = id; FocusInput("template:" + id, templateValues[id], mouseX); }, true));
        }
    }
}
