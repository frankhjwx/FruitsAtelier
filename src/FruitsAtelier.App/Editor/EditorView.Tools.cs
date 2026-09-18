using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool nextFruitNewCombo;
    private bool sliderModeFlyoutOpen;
    private bool draftStraight;
    public bool NextFruitNewCombo => nextFruitNewCombo;
    private readonly List<Rect> toolButtons = [];
    public IReadOnlyList<Rect> ToolButtonBounds => toolButtons;

    private void DrawToolPalette(ICanvas c)
    {
        toolButtons.Clear();
        float size = Math.Min(96, plot.Height / 4);
        float top = plot.Y + (plot.Height - size * 4) / 2;
        for (int i = 0; i < 4; i++)
        {
            var mode = (Tool)i;
            string name = i == 2 ? "fslider" : mode.ToString().ToLowerInvariant();
            var bounds = new Rect((108 - size) / 2, top + i * size, size, size);
            toolButtons.Add(bounds);
            bool active = mode == tool;
            c.Image(Path.Combine(AppContext.BaseDirectory, "assets", "icons", "tools", name + ".png"), bounds, opacity: active ? 1 : .45f);
            string label = L.Get(i switch { 0 => "tools.select", 1 => "tools.fruit", 2 => "tools.fslider", _ => "tools.banana" });
            float fontSize = size * 52 / 512;
            c.Text(label, bounds.X + (size - c.MeasureText(label, fontSize, true)) / 2, bounds.Y + size * .76f,
                fontSize, active ? Foreground : 0x747B85, size, true);
            hits.Add(new(bounds, () =>
            {
                if (mode == Tool.Slider && draftTrack == Guid.Empty) Select(Guid.Empty);
                ChangeTool(mode);
            }, true));
        }
        var slider = toolButtons[2];
        var hoverArea = new Rect(slider.Right, slider.Y, 144, Math.Max(66, slider.Height));
        var flyout = new Rect(slider.Right, slider.Y, 144, 66);
        sliderModeFlyoutOpen = slider.Contains(mouseX, mouseY) || sliderModeFlyoutOpen && hoverArea.Contains(mouseX, mouseY);
        if (sliderModeFlyoutOpen)
        {
            c.Fill(flyout, Surface, 4);
            Button(c, new(flyout.X, flyout.Y, 142, 32), L.Get("ui.osuLegacyMode"), () => SetSliderEditingMode(SliderEditingMode.OsuLegacy), LegacyMode);
            Button(c, new(flyout.X, flyout.Y + 34, 142, 32), L.Get("ui.penToolMode"), () => SetSliderEditingMode(SliderEditingMode.PenTool), !LegacyMode);
        }
    }

    private bool gridSnap;
    private int gridSize = 4;
    private double SnapX(double x) => EffectiveGridSnap ? Math.Round(x / gridSize, MidpointRounding.AwayFromZero) * gridSize : x;

    private MapPoint? PlacementGhostPoint()
    {
        if (tool is not (Tool.Fruit or Tool.Slider) || !plot.Contains(mouseX, mouseY) || drag != DragKind.None
            || menu >= 0 || contextItems.Count > 0 || editField >= 0 || tool == Tool.Slider && draftTrack == Guid.Empty && SelectedTrack is not null) return null;
        if (tool == Tool.Slider && SelectedTrack is { } draft && draft.Id == draftTrack
            && (LegacyMode && legacyPreviewValid || draft.Nodes.Any(n => Near(Point(n), mouseX, mouseY, 8)))) return null;
        var point = PlacementPoint(mouseX, mouseY);
        if (tool == Tool.Fruit && ObjectsInTimeRange(point.TimeMs - 1, point.TimeMs + 1)
            .Any(item => item.Kind == CatchObjectKind.Fruit && Math.Abs(item.X - point.X) < .01)) return null;
        return point;
    }

    private void DrawPlacementGhost(ICanvas c)
    {
        if (PlacementGhostPoint() is not { } point) return;
        var p = Screen(point);
        float diameter = CatchSize.FruitDiameter(Document.CircleSize) * Playfield.Width / 512;
        if (skin is null || !skin.Draw(c, CatchSkinObject.Fruit, 0, p.X, p.Y, diameter, opacity: .6f))
            c.Circle(p.X, p.Y, diameter / 2, Foreground, opacity: .6f);
        c.Line(Playfield.X, p.Y, Playfield.Right, p.Y, Gold, opacity: .4f);
        if (tool == Tool.Fruit && nextFruitNewCombo)
            c.Text(L.Get("tools.newCombo"), p.X + diameter / 2 + 6, p.Y - 8, 11, Gold, 120);
    }

    private void PlaceFruit(float x, float y)
    {
        var point = PlacementPoint(x, y);
        var fruit = new Fruit { TimeMs = point.TimeMs, X = point.X };
        if (Edit(L.Get("editor.command.addFruit"), () =>
        {
            Document.Fruits.Add(fruit);
            ApplyPlacementFlags(fruit.Id);
            Document.DurationMs = Math.Max(Document.DurationMs, fruit.TimeMs);
        })) { Select(fruit.Id); nextFruitNewCombo = false; }
    }

    private void RightClickCanvas(float x, float y)
    {
        if (!plot.Contains(x, y)) return;
        if (draftBanana != Guid.Empty) { FinishBanana(x, y); return; }
        if (draftTrack != Guid.Empty)
        {
            var track = SelectedTrack!;
            if (LegacyMode && legacyDraft is { } vertices)
            {
                int index = vertices.FindLastIndex(v => Near(v.Point, x, y, 8));
                if (index >= 0)
                {
                    vertices.RemoveAt(index);
                    if (vertices.Count == 0) { CancelInteraction(); return; }
                    vertices[0] = vertices[0] with { Type = vertices[0].Type ?? SliderCurveType.Bezier };
                    if (vertices.Count > 1) { SliderControlEditing.Apply(track, vertices, allowArcFallback: true); legacyPreviewValid = true; }
                    else { track.Nodes.Clear(); track.Nodes.Add(new Anchor { Id = vertices[0].Id, TimeMs = vertices[0].Point.TimeMs, X = vertices[0].Point.X }); legacyPreviewValid = false; }
                    return;
                }
                if (!UpdateLegacyPreview(x, y)) return;
            }
            else
            {
                var hit = track.Nodes.LastOrDefault(n => Near(Point(n), x, y, 8));
                if (hit is not null)
                {
                    if (track.Nodes.Count > 2) CurvePointEditing.RemoveMany(track, [hit.Id]);
                    else
                    {
                        track.Nodes.Remove(hit);
                        if (track.Nodes.Count == 0) { CancelInteraction(); return; }
                        track.Nodes[0].HandleIn = track.Nodes[0].HandleOut = default;
                    }
                    Select(track.Nodes[^1].Id, track.Id);
                    return;
                }
                var point = MapAt(x, y, true);
                if (point.TimeMs >= track.Nodes[^1].TimeMs + CurveMath.MinimumAnchorSpacingMs)
                { AddCurveAnchor(x, y); drag = DragKind.None; }
            }
            FinishCurve();
            return;
        }
        if (showTargets && tool is Tool.Select or Tool.Slider && SelectedTrack is { } selected && objectSelection.Count <= 1)
        {
            var vertices = LegacyMode ? SliderControlEditing.Vertices(selected) : selected.Nodes.Select(n => new SliderVertex(n.Id, Point(n), null)).ToList();
            var hit = vertices.OrderBy(v => PointerDistance(v.Point, x, y)).ThenBy(v => v.Type is null ? 1 : 0).FirstOrDefault(v => Near(v.Point, x, y, 8));
            if (hit is not null)
            {
                SelectAnchors(selected, [hit.Id]);
                if (IsStraightPoint(selected, hit.Id)) SetPointStraight(selected, hit.Id, false);
                else if (LegacyMode) DeleteLegacyPoints(); else DeleteSelectedAnchors();
                return;
            }
        }
        var source = HitCatchObject(x, y)?.SourceId ?? HitSliderLocation(x, y)?.Id ?? HitBananaRectangle(x, y)?.Id;
        if (source is { } id && (tool != Tool.Fruit || !AudioPlaying))
        { SelectObjects([id]); DeleteSelectedObjects(); return; }
        if (tool == Tool.Fruit) { nextFruitNewCombo = !nextFruitNewCombo; StatusMessage = L.Get(nextFruitNewCombo ? "tools.newComboOn" : "tools.newComboOff"); }
    }

    private bool IsStraightPoint(CurveTrack track, Guid id)
    {
        if (!LegacyMode) return !CurvePointEditing.IsCurved(track, id);
        var vertices = SliderControlEditing.Vertices(track);
        int index = vertices.FindIndex(v => v.Id == id);
        if (index > 0 && index < vertices.Count - 1) return vertices[index].Type is not null;
        return !CurvePointEditing.IsCurved(track, id);
    }

    private void SetPointStraight(CurveTrack track, Guid id, bool straight)
    {
        if (IsStraightPoint(track, id) == straight) return;
        if (Edit(L.Get("editor.command.convertPoint"), () =>
        {
            var vertices = LegacyMode ? SliderControlEditing.Vertices(track) : [];
            int index = vertices.FindIndex(v => v.Id == id);
            if (LegacyMode && index > 0 && index < vertices.Count - 1)
                SliderControlEditing.ToggleBoundary(track, id);
            else CurvePointEditing.SetCurved(track, id, !straight);
        })) SelectAnchors(track, [id]);
    }

    private bool StraightenHitPoint(float x, float y)
    {
        if (!showTargets || tool is not (Tool.Select or Tool.Slider) || draftTrack != Guid.Empty
            || objectSelection.Count > 1 || SelectedTrack is not { } track) return false;
        var vertices = LegacyMode ? SliderControlEditing.Vertices(track) : track.Nodes.Select(n => new SliderVertex(n.Id, Point(n), null)).ToList();
        var hit = vertices.OrderBy(v => PointerDistance(v.Point, x, y)).ThenBy(v => v.Type is null ? 1 : 0).FirstOrDefault(v => Near(v.Point, x, y, 8));
        if (hit is null) return false;
        SelectAnchors(track, [hit.Id]);
        SetPointStraight(track, hit.Id, true);
        return true;
    }

    private void TogglePointCurve()
    {
        if (SelectedTrack is not { } track || anchorSelection.Count != 1 || draftTrack != Guid.Empty) return;
        SetPointStraight(track, selection, !IsStraightPoint(track, selection));
    }
}
