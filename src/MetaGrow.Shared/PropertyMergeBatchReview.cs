using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiModels;

namespace MetaGrow.Shared;

public sealed class ReviewedPropertyMergeRequest
{
    public Guid RequestId { get; set; }
    [Required] public PropertyMergePreviewRequest Plan { get; set; } = new();
    [Required] public string PreviewHash { get; set; } = string.Empty;
}

public static class PropertyMergeBatchReview
{
    private static readonly string[] Fields =
    [
        "PropertyName", "BillingEntityId", "BillingAccountCode", "Address1", "Address2", "Suburb",
        "State", "PostCode", "Email", "Phone", "MobilePhone", "ClientGuid", "ThirdPartyId", "NaGrowerId", "Comments"
    ];

    public static PropertyMergePreviewRequest InitialPlan(int sourceId, int targetId) => new()
    {
        SourcePropertyId = sourceId,
        TargetPropertyId = targetId,
        // Explicit choices also keep empty destination fields instead of silently filling them.
        FieldChoices = Fields.Select(name => new PropertyMergeFieldChoice
        {
            FieldName = name, ValueSource = PropertyMergeFieldValue.Target
        }).ToList()
    };

    public static PropertyMergePreviewRequest Plan(PropertyMergePreview preview)
    {
        var plan = InitialPlan(preview.Source.PropertyId, preview.Target.PropertyId);
        foreach (var field in preview.FieldDifferences.Where(field => field.FieldName != "PropertyName"))
        {
            var choice = plan.FieldChoices.FirstOrDefault(choice => choice.FieldName == field.FieldName);
            if (choice is not null) choice.ValueSource = field.ValueSource;
        }
        plan.BlockDecisions = preview.Blocks.Select(block => new PropertyMergeBlockDecision
        {
            BlockType = block.BlockType, SourceBlockId = block.SourceBlockId,
            Action = block.Action, TargetBlockId = block.Action == PropertyMergeBlockAction.Merge ? block.TargetBlockId : null
        }).ToList();
        return plan;
    }

    public static string Fingerprint(PropertyMergePreview preview)
    {
        var snapshot = new
        {
            Source = new
            {
                preview.Source.PropertyId, preview.Source.PropertyName, preview.Source.IsActive,
                preview.Source.IncludedRowCount, preview.Source.ExcludedRowCount,
                Dependencies = preview.Source.Dependencies.OrderBy(item => item.TableName).ToArray()
            },
            // Destination totals grow after each merge; its identity and chosen values must stay fixed.
            Target = new { preview.Target.PropertyId, preview.Target.PropertyName, preview.Target.IsActive },
            Fields = preview.FieldDifferences.OrderBy(field => field.FieldName).Select(field => new
            {
                field.FieldName, field.SourceValue, field.TargetValue, field.ValueSource
            }).ToArray(),
            Blocks = preview.Blocks.OrderBy(block => block.BlockType).ThenBy(block => block.SourceBlockId).Select(block => new
            {
                block.BlockType, block.SourceBlockId, block.SourceBlockName, block.SourceIsActive,
                block.Action, block.TargetBlockId, block.TargetBlockName,
                // New strong matches need review even when the old plan said to move the block.
                Matches = block.Candidates.Where(candidate => candidate.MatchScore >= 0.88)
                    .OrderBy(candidate => candidate.BlockId).Select(candidate => new
                    {
                        candidate.BlockId, candidate.BlockName, candidate.IsActive, candidate.MatchScore
                    }).ToArray()
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot))));
    }
}
