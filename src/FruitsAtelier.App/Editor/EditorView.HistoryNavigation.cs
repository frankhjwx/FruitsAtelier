using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void RevealHistoryChange(MapDocument before, HashSet<Guid> unchanged)
    {
        var positions = new List<MapPoint>();
        var currentIds = Document.Fruits.Select(f => f.Id).Concat(Document.Tracks.Select(t => t.Id))
            .Concat(Document.ImportedSliders.Select(s => s.Id)).Concat(Document.BananaShowers.Select(b => b.Id)).ToHashSet();
        AddPositions(Document, before, id => !unchanged.Contains(id));
        AddPositions(before, Document, id => !currentIds.Contains(id));
        if (positions.Count == 0 || positions.Any(Visible)) return;
        var target = positions.OrderBy(p => Math.Abs(p.TimeMs - playhead)).First();
        SeekTo(target.TimeMs);

        bool Visible(MapPoint point)
        {
            var screen = Transform.ToScreen(point);
            return plot.Contains((float)screen.X, (float)screen.Y);
        }

        void AddPositions(MapDocument source, MapDocument other, Func<Guid, bool> include)
        {
            foreach (var fruit in source.Fruits.Where(f => include(f.Id))) positions.Add(new(fruit.TimeMs, fruit.X));
            var otherTracks = other.Tracks.ToDictionary(t => t.Id);
            foreach (var track in source.Tracks.Where(t => include(t.Id)))
            {
                int initialCount = positions.Count;
                if (otherTracks.TryGetValue(track.Id, out var previous))
                {
                    var previousNodes = previous.Nodes.ToDictionary(n => n.Id);
                    foreach (var node in track.Nodes)
                        if (!previousNodes.TryGetValue(node.Id, out var old) || node.TimeMs != old.TimeMs || node.X != old.X
                            || node.HandleIn != old.HandleIn || node.HandleOut != old.HandleOut || node.OutgoingKind != old.OutgoingKind
                            || !SameCurve(node.OutgoingCurve, old.OutgoingCurve))
                            positions.Add(new(node.TimeMs, node.X));
                    var nodes = track.Nodes.Select(n => n.Id).ToHashSet();
                    foreach (var node in previous.Nodes.Where(n => !nodes.Contains(n.Id))) positions.Add(new(node.TimeMs, node.X));
                    if (track.SpanCount != previous.SpanCount && track.Nodes.Count > 1)
                        positions.Add(new(CurveMath.EndTimeMs(track), track.Nodes[^1].X));
                }
                if (positions.Count == initialCount && track.Nodes.Count > 0)
                    positions.Add(new(track.Nodes[0].TimeMs, track.Nodes[0].X));
            }
            foreach (var slider in source.ImportedSliders.Where(s => include(s.Id))) positions.Add(new(slider.TimeMs, slider.X));
            var otherBananas = other.BananaShowers.ToDictionary(b => b.Id);
            foreach (var shower in source.BananaShowers.Where(b => include(b.Id)))
            {
                if (otherBananas.TryGetValue(shower.Id, out var old) && shower.TimeMs == old.TimeMs && shower.EndTimeMs != old.EndTimeMs)
                    positions.Add(new(shower.EndTimeMs, 256));
                else positions.Add(new(shower.TimeMs, 256));
            }
        }
        static bool SameCurve(ControlCurve? a, ControlCurve? b) => a is null ? b is null : b is not null
            && a.Kind == b.Kind && a.ReferenceScale == b.ReferenceScale
            && a.Controls.Select(p => (p.Id, p.Offset)).SequenceEqual(b.Controls.Select(p => (p.Id, p.Offset)));
    }
}
