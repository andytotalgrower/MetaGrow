using MetaGrow.Shared.Models;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class SoilReportMatchingSessionTests
{
    [Fact]
    public void Manual_selections_need_reasons_and_cannot_reuse_a_sample_or_mutate_the_loaded_review()
    {
        var review = Review(); var session = new SoilReportMatchingSession(review);
        Assert.True(session.CanSave);
        var row = session.Review.Rows[0];
        session.Select(row, 8); row.Reason = "Checked block map";
        Assert.True(session.IsDuplicate(row)); Assert.False(session.CanSave);
        session.Select(row, 7); Assert.False(session.CanSave);
        row.Reason = "Checked block map"; Assert.True(session.CanSave);
        var change = session.Change(); Assert.Single(change.Items); Assert.Equal(7, change.Items[0].SurveyItemId);
        Assert.False(review.Rows[0].IsManual);
        session.RestoreAutomatic(row); Assert.True(session.CanSave); Assert.Empty(session.Change().Items);
        Assert.Equal("Matched the mapped property block.", row.Message);
    }

    [Fact]
    public void Unresolved_stale_and_locked_matches_cannot_be_saved_without_a_valid_choice()
    {
        var review = Review(); review.Rows[0].SurveyItemId = 0; review.Rows[0].RequiresReview = true;
        var session = new SoilReportMatchingSession(review); Assert.False(session.CanSave);
        session.RestoreAutomatic(session.Review.Rows[0]); Assert.True(session.CanSave);
        session.Review.CanEdit = false; Assert.False(session.CanSave);
    }

    private static SoilReportMatchingReview Review() => new()
    {
        CanEdit = true, Version = 3, InputHash = "current-identifiers",
        Rows = Enumerable.Range(0, 2).Select(index => new SoilReportMatchRow { LabSoilId = 100 + index, SurveyItemId = 7 + index,
            AutomaticSurveyItemId = 7 + index, AutomaticMessage = "Matched the mapped property block.",
            Choices = [new(7, "North"), new(8, "South")] }).ToList()
    };
}
