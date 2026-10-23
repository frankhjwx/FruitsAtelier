using System.Text.Json.Serialization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

// Guid.Empty leaves a section unassigned; its interval still participates in validation.
public sealed record CollaborationReference(Guid Id, Guid DifficultyId, double StartMs, double EndMs);
public sealed record CollaborationEditRange(double StartMs, double EndMs, bool IncludeEnd = false);

public sealed partial class MapDocument
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CollaborationReference>? CollaborationReferences { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CollaborationEditRange>? CollaborationEditableRanges { get; set; }

    private bool CollaborationReferencesEqual(MapDocument other)
    {
        if ((CollaborationEditableRanges is null) != (other.CollaborationEditableRanges is null)
            || !(CollaborationEditableRanges ?? []).SequenceEqual(other.CollaborationEditableRanges ?? [])) return false;
        if (ReferenceEquals(CollaborationReferences, other.CollaborationReferences)) return true;
        int count = CollaborationReferences?.Count ?? 0;
        if (count != (other.CollaborationReferences?.Count ?? 0)) return false;
        for (int i = 0; i < count; i++) if (CollaborationReferences![i] != other.CollaborationReferences![i]) return false;
        return true;
    }

    internal void ValidateCollaborationReferences()
    {
        if (CollaborationEditableRanges is { } ranges && (ranges.Count > 256 || ranges.Any(r => r is null
            || !double.IsFinite(r.StartMs) || !double.IsFinite(r.EndMs) || r.StartMs < 0 || r.EndMs > int.MaxValue || r.EndMs <= r.StartMs)))
            throw new InvalidDataException(L.Get("collab.invalidRange"));
        if (CollaborationEditableRanges is { } orderedRanges)
            for (int i = 1; i < orderedRanges.Count; i++)
                if (orderedRanges[i].StartMs < orderedRanges[i - 1].EndMs) throw new InvalidDataException(L.Get("collab.overlap"));
        if (CollaborationReferences is not { } references) return;
        var ordered = references.OrderBy(r => r?.StartMs).ToArray();
        if (ordered.Length > 256 || ordered.Any(r => r is null || r.Id == Guid.Empty
            || !double.IsFinite(r.StartMs) || !double.IsFinite(r.EndMs) || r.StartMs != Math.Truncate(r.StartMs)
            || r.EndMs != Math.Truncate(r.EndMs) || r.StartMs < 0 || r.EndMs > int.MaxValue || r.StartMs >= r.EndMs)
            || ordered.Select(r => r.Id).Distinct().Count() != ordered.Length)
            throw new InvalidDataException(L.Get("collab.invalidRange"));
        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i].StartMs < ordered[i - 1].EndMs)
                throw new InvalidDataException(L.Get("collab.overlap"));
    }
}
