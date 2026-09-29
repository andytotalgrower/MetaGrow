using ApiModels;
using System.Text.Json;

namespace MetaGrow.Web.Services;

/// <summary>Uses the existing Damerau name matcher, including spacing and transposition handling.</summary>
public sealed record CropNameReview(List<CropType> Candidates, bool HasExactDuplicate, string Key)
{
    public static CropNameReview Check(string name, int parentId, IEnumerable<CropType> crops)
    {
        var candidates = crops.Where(c => RecordNameMatching.SameName(name, c.CropTypeName) ||
            RecordNameMatching.LikelyDuplicate(name, c.CropTypeName))
            .OrderByDescending(c => RecordNameMatching.SameName(name, c.CropTypeName))
            .ThenBy(c => c.CropTypeName, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.CropTypeId).ToList();
        // Bind approval to the name, parent and exact set of reviewed candidates.
        var key = JsonSerializer.Serialize(new { name, parentId, candidates });
        return new(candidates, candidates.Any(c => RecordNameMatching.SameName(name, c.CropTypeName)), key);
    }
}
