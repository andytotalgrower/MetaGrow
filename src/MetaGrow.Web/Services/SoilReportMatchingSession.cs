using System.Text.Json;
using MetaGrow.Shared.Models;

namespace MetaGrow.Web.Services;

/// <summary>Keeps popup edits local until every result has a distinct sample and manual reasons are supplied.</summary>
public sealed class SoilReportMatchingSession(SoilReportMatchingReview review)
{
    private readonly SoilReportMatchingReview _review = JsonSerializer.Deserialize<SoilReportMatchingReview>(JsonSerializer.Serialize(review))!;
    public SoilReportMatchingReview Review => _review;
    public bool CanSave => _review.CanEdit && _review.Rows.Count > 0
        && _review.Rows.All(row => row.Choices.Any(choice => choice.SurveyItemId == row.SurveyItemId)
            && (!row.IsManual || !string.IsNullOrWhiteSpace(row.Reason) && row.Reason.Length <= 500))
        && _review.Rows.Select(row => row.SurveyItemId).Distinct().Count() == _review.Rows.Count;

    public void Select(SoilReportMatchRow row, int surveyItemId)
    {
        row.SurveyItemId = surveyItemId; row.IsManual = true; row.Reason = "";
        row.RequiresReview = surveyItemId <= 0;
    }

    public void RestoreAutomatic(SoilReportMatchRow row)
    {
        // Clearing a saved choice is explicit; the server re-evaluates the automatic match before accepting it.
        row.SurveyItemId = row.AutomaticSurveyItemId; row.IsManual = false; row.Reason = "";
        row.Message = row.AutomaticMessage;
        row.RequiresReview = row.SurveyItemId <= 0;
    }

    public bool IsDuplicate(SoilReportMatchRow row) => row.SurveyItemId > 0
        && _review.Rows.Count(other => other.SurveyItemId == row.SurveyItemId) > 1;

    public SoilReportMatchingChange Change() => new()
    {
        ExpectedVersion = _review.Version, ExpectedInputHash = _review.InputHash,
        Items = _review.Rows.Where(row => row.IsManual).Select(row => new SoilReportManualMatch
            { LabSoilId = row.LabSoilId, SurveyItemId = row.SurveyItemId, Reason = row.Reason }).ToList()
    };
}
