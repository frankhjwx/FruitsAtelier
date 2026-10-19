using System.Text.Json.Serialization;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.Core;

public sealed record CollaborationReference(Guid Id, Guid DifficultyId, double StartMs, double EndMs);

public sealed partial class MapDocument
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CollaborationReference>? CollaborationReferences { get; set; }

    private bool CollaborationReferencesEqual(MapDocument other)
    {
        if (ReferenceEquals(CollaborationReferences, other.CollaborationReferences)) return true;
        int count = CollaborationReferences?.Count ?? 0;
        if (count != (other.CollaborationReferences?.Count ?? 0)) return false;
        for (int i = 0; i < count; i++) if (CollaborationReferences![i] != other.CollaborationReferences![i]) return false;
        return true;
    }

    internal void ValidateCollaborationReferences()
    {
        if (CollaborationReferences is not { } references) return;
        var ordered = references.OrderBy(r => r?.StartMs).ToArray();
        if (ordered.Length > 256 || ordered.Any(r => r is null || r.Id == Guid.Empty || r.DifficultyId == Guid.Empty
            || !double.IsFinite(r.StartMs) || !double.IsFinite(r.EndMs) || r.StartMs != Math.Truncate(r.StartMs)
            || r.EndMs != Math.Truncate(r.EndMs) || r.StartMs < 0 || r.EndMs > int.MaxValue || r.StartMs >= r.EndMs)
            || ordered.Select(r => r.Id).Distinct().Count() != ordered.Length)
            throw new InvalidDataException(L.Get("collab.invalidRange"));
        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i].StartMs < ordered[i - 1].EndMs)
                throw new InvalidDataException(L.Get("collab.overlap"));
    }
}
