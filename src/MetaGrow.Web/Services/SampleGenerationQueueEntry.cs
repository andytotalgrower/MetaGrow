using ApiModels.MetaGrow;

namespace MetaGrow.Web.Services;

public sealed class SampleGenerationQueueEntry
{
    public Guid Id { get; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public SampleSoilSurveyGenerationPreviewDto? Preview { get; private set; }
    public SampleSoilSurveyGenerationReceiptDto? Receipt { get; set; }
    public List<SampleGenerationMappingRow> Mappings { get; private set; } = [];
    public Dictionary<int, int> ColumnSelections { get; } = [];
    public bool PropertyOverrideAccepted { get; set; }
    public bool Reading { get; set; }
    public bool Importing { get; set; }
    public bool OutcomeUnknown { get; set; }
    public string? Error { get; set; }
    public bool HasDuplicateBlocks => Mappings.Where(row => !string.IsNullOrWhiteSpace(row.BlockKey))
        .GroupBy(row => row.BlockKey.Split(':').Last()).Any(group => group.Count() > 1);
    public bool IsReady => Receipt is null && !Reading && !Importing && !OutcomeUnknown && Preview?.CanGenerate == true &&
        (!Preview.PropertyNameOverrideRequired || PropertyOverrideAccepted) && !HasDuplicateBlocks && Mappings.Count > 0 &&
        Mappings.All(row => !string.IsNullOrWhiteSpace(row.BlockKey) && Preview.LabTestTypes.Any(test => test.LabTestTypeId == row.LabTestTypeId));
    public string Status => Reading ? "Reading" : Importing ? "Importing" : Receipt is not null ? "Imported" :
        OutcomeUnknown ? "Outcome unknown" : Error is not null ? "Failed" : IsReady ? "Ready" : "Needs review";

    public void ApplyPreview(SampleSoilSurveyGenerationPreviewDto preview)
    {
        var previous = Mappings.ToDictionary(row => row.MappingKey);
        Preview = preview;
        FileName = preview.FileName;
        Error = null;
        ColumnSelections.Clear();
        PropertyOverrideAccepted = false;
        Mappings = preview.Mappings.Select((mapping, index) =>
        {
            var result = preview.Rows.FirstOrDefault(row => row.MappingKey == mapping.MappingKey);
            previous.TryGetValue(mapping.MappingKey, out var old);
            var preserve = old is not null && old.RawBlockName == mapping.RawBlockName && old.SampleReference == result?.OrderNo;
            return new SampleGenerationMappingRow
            {
                MappingKey = mapping.MappingKey, RawBlockName = mapping.RawBlockName, ResultCount = mapping.ResultCount,
                SampleReference = result?.OrderNo ?? string.Empty, RowLabel = $"CSV row {result?.RowNumber}",
                BlockControlId = $"generation-{Id:N}-block-{index}", TestControlId = $"generation-{Id:N}-test-{index}",
                BlockKey = preserve ? old!.BlockKey : mapping.SuggestedBlockId > 0
                    ? $"{(mapping.SuggestedBlockType.StartsWith("ban", StringComparison.OrdinalIgnoreCase) ? "banana" : "mcs")}:{mapping.SuggestedBlockId}" : string.Empty,
                BlockWasSuggested = !preserve && mapping.SuggestedBlockId > 0,
                LabTestTypeId = preserve && preview.LabTestTypes.Any(test => test.LabTestTypeId == old!.LabTestTypeId)
                    ? old!.LabTestTypeId : mapping.SuggestedLabTestTypeId
            };
        }).ToList();
    }

    public SampleSoilSurveyGenerationCommitRequest ToRequest() => new()
    {
        PreviewToken = Preview?.PreviewToken ?? string.Empty, ConfirmPropertyNameMismatch = PropertyOverrideAccepted,
        Mappings = Mappings.Select(row => new SampleSoilSurveyGenerationMappingSelectionDto
        {
            MappingKey = row.MappingKey, BlockType = row.BlockKey.Split(':')[0],
            BlockId = int.Parse(row.BlockKey.Split(':')[1]), LabTestTypeId = row.LabTestTypeId
        }).ToList()
    };
}

public sealed class SampleGenerationMappingRow
{
    public string RowLabel { get; init; } = string.Empty;
    public string SampleReference { get; init; } = string.Empty;
    public string BlockControlId { get; init; } = string.Empty;
    public string TestControlId { get; init; } = string.Empty;
    public string MappingKey { get; init; } = string.Empty;
    public string RawBlockName { get; init; } = string.Empty;
    public int ResultCount { get; init; }
    public string BlockKey { get; set; } = string.Empty;
    public bool BlockWasSuggested { get; init; }
    public int LabTestTypeId { get; set; }
}
