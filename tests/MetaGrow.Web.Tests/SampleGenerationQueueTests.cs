using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class SampleGenerationQueueTests
{
    [Fact]
    public void Draft_column_choices_survive_component_recreation_and_remain_file_scoped()
    {
        var first = new SampleGenerationQueueEntry();
        var second = new SampleGenerationQueueEntry();
        var review = new SampleImportColumnReviewDto
        {
            FileHeaders = ["Sample", "Date", "Result"],
            MissingColumns = [new() { ColumnId = 10, Name = "Result", IsRequired = true }]
        };
        void Mount(SampleGenerationQueueEntry file)
        {
            var component = new MetaGrow.Web.Components.Shared.SampleImportColumnReview();
            var type = component.GetType();
            type.GetProperty("Review")!.SetValue(component, review);
            type.GetProperty("DraftSelections")!.SetValue(component, file.ColumnSelections);
            type.GetMethod("OnParametersSet", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(component, null);
        }
        Mount(first);
        Assert.Equal(-2, first.ColumnSelections[10]);
        first.ColumnSelections[10] = 2;
        Mount(second);
        Mount(first);
        Assert.Equal(2, first.ColumnSelections[10]);
        Assert.Equal(-2, second.ColumnSelections[10]);
        first.ApplyPreview(Preview("one.csv", 1, "1"));
        Assert.Empty(first.ColumnSelections);
    }

    [Fact]
    public void Different_dates_are_independent_even_with_the_same_block()
    {
        var first = Preview("one.csv", 1, "1");
        var second = Preview("two.csv", 2, "2");
        Assert.Empty(SampleGenerationBatchRules.Validate([first, second]));
    }

    [Fact]
    public void Overlapping_dates_in_any_part_of_a_file_require_manual_combining()
    {
        var first = Preview("one.csv", 1, "1");
        first.SurveyGroups.Add(new() { SurveyDate = new(2026, 9, 2), ResultCount = 1 });
        var errors = SampleGenerationBatchRules.Validate([first, Preview("two.csv", 2, "2")]);
        Assert.Contains(errors, error => error.Contains("Combine the CSVs manually") && error.Contains("one.csv") && error.Contains("two.csv"));
    }

    [Fact]
    public void Identical_files_are_rejected_even_if_renamed()
    {
        var first = Preview("original.csv", 1, "1");
        var second = Preview("renamed.csv", 2, "2");
        second.FileHash = first.FileHash;
        Assert.Contains(SampleGenerationBatchRules.Validate([first, second]), error => error.Contains("Identical CSV"));
    }

    [Fact]
    public void References_are_compared_across_files_with_numeric_normalization()
    {
        Assert.Contains(SampleGenerationBatchRules.Validate([Preview("one.csv", 1, "00012"), Preview("two.csv", 2, "12")]),
            error => error.Contains("Sample reference 12"));
    }

    [Fact]
    public void Different_labs_can_use_the_same_reference_on_different_dates()
    {
        var second = Preview("two.csv", 2, "12");
        second.LabId = 9;
        Assert.Empty(SampleGenerationBatchRules.Validate([Preview("one.csv", 1, "12"), second]));
    }

    [Fact]
    public void Batch_limit_and_shared_destination_are_checked()
    {
        var many = Enumerable.Range(1, 11).Select(day => Preview($"{day}.csv", day, day.ToString())).ToList();
        Assert.Contains(SampleGenerationBatchRules.Validate(many), error => error.Contains("between 1 and 10"));
        var otherFarm = Preview("two.csv", 2, "2");
        otherFarm.PropertyId = 2;
        Assert.Contains(SampleGenerationBatchRules.Validate([Preview("one.csv", 1, "1"), otherFarm]), error => error.Contains("same farm"));
        Assert.NotEmpty(SampleGenerationBatchRules.Validate([]));
    }

    [Fact]
    public void Per_file_reviews_survive_switching_adding_removing_and_refreshing_other_files()
    {
        var first = new SampleGenerationQueueEntry();
        first.ApplyPreview(Preview("one.csv", 1, "1"));
        first.Mappings[0].BlockKey = "mcs:42";
        first.PropertyOverrideAccepted = true;
        var second = new SampleGenerationQueueEntry();
        second.ApplyPreview(Preview("two.csv", 2, "2"));
        var queue = new List<SampleGenerationQueueEntry> { first, second };
        second.ApplyPreview(Preview("two.csv", 2, "2"));
        queue.Remove(second);
        Assert.Equal("mcs:42", first.Mappings[0].BlockKey);
        Assert.True(first.PropertyOverrideAccepted);
        Assert.Single(queue);
        first.ApplyPreview(Preview("one.csv", 1, "1"));
        Assert.Equal("mcs:42", first.Mappings[0].BlockKey);
        Assert.False(first.PropertyOverrideAccepted); // a refreshed farm check must be acknowledged again
        Assert.Equal(42, first.ToRequest().Mappings[0].BlockId);
    }

    [Fact]
    public void Changed_row_identity_does_not_inherit_an_old_block_choice()
    {
        var file = new SampleGenerationQueueEntry();
        file.ApplyPreview(Preview("one.csv", 1, "1"));
        file.Mappings[0].BlockKey = "mcs:42";
        file.ApplyPreview(Preview("one.csv", 1, "different-sample"));
        Assert.Equal("mcs:1", file.Mappings[0].BlockKey);
    }

    [Fact]
    public void Unknown_outcomes_block_retry_and_imported_files_are_never_ready_again()
    {
        var file = new SampleGenerationQueueEntry();
        file.ApplyPreview(Preview("one.csv", 1, "1"));
        Assert.True(file.IsReady);
        file.Error = "Connection interrupted";
        file.OutcomeUnknown = true;
        Assert.False(file.IsReady);
        Assert.Equal("Outcome unknown", file.Status);
        file.OutcomeUnknown = false;
        Assert.True(file.IsReady); // confirmed rollback can be retried
        file.Receipt = new();
        Assert.False(file.IsReady);
        Assert.Equal("Imported", file.Status);
    }

    [Fact]
    public void File_scoped_control_ids_do_not_collide_for_the_same_csv_row()
    {
        var first = new SampleGenerationQueueEntry();
        var second = new SampleGenerationQueueEntry();
        first.ApplyPreview(Preview("one.csv", 1, "1"));
        second.ApplyPreview(Preview("two.csv", 2, "2"));
        Assert.NotEqual(first.Mappings[0].BlockControlId, second.Mappings[0].BlockControlId);
    }

    private static SampleSoilSurveyGenerationPreviewDto Preview(string name, int day, string reference) => new()
    {
        FileName = name, FileHash = name, PreviewToken = name, PropertyId = 1, PropertyName = "Farm", AgronomistId = 1, LabId = 3,
        LabTestTypes = [new LabTestType { LabTestTypeId = 42, LabId = 3, TestTypeId = 10 }],
        SurveyGroups = [new() { SurveyDate = new(2026, 9, day), ResultCount = 1 }],
        Rows = [new() { RowNumber = 2, OrderNo = reference, MappingKey = "row:2", RawBlockName = "North", DateReceived = new(2026, 9, day) }],
        Mappings = [new() { MappingKey = "row:2", RawBlockName = "North", SuggestedBlockId = 1, SuggestedBlockType = "mcs", SuggestedLabTestTypeId = 42, ResultCount = 1 }]
    };
}
