using ApiModels;

namespace MetaGrow.Web.Tests;

public sealed class SampleSurveyWorkflowTests
{
    [Theory]
    [InlineData(MultiCropSurveyStatus.InProgress, SampleSurveyWorkflowAction.SubmitForQa, MultiCropSurveyStatus.AwaitingQa)]
    [InlineData(MultiCropSurveyStatus.AwaitingQa, SampleSurveyWorkflowAction.CompleteQa, MultiCropSurveyStatus.ReadyToSend)]
    [InlineData(MultiCropSurveyStatus.ReadyToSend, SampleSurveyWorkflowAction.MarkSent, MultiCropSurveyStatus.Complete)]
    [InlineData(MultiCropSurveyStatus.Complete, SampleSurveyWorkflowAction.ReopenForSending, MultiCropSurveyStatus.ReadyToSend)]
    public void GetTargetStatus_AllowsOnlyTheStructuredReportPath(
        int current,
        SampleSurveyWorkflowAction action,
        int expected) =>
        Assert.Equal(expected, SampleSurveyWorkflow.GetTargetStatus(current, action));

    [Fact]
    public void GetTargetStatus_DoesNotTreatLabReceiptAsAReportStatusChange() =>
        Assert.Null(SampleSurveyWorkflow.GetTargetStatus(
            MultiCropSurveyStatus.InProgress,
            SampleSurveyWorkflowAction.MarkReceivedByLab));

    [Theory]
    [InlineData(2, 0, 0, 10, "In transit")]
    [InlineData(0, 2, 0, 10, "At lab")]
    [InlineData(0, 0, 2, 10, "Ready for agronomist")]
    [InlineData(0, 0, 2, 20, "Awaiting QA")]
    public void LaneWorkflowName_PrioritisesLabProgressBeforeReportStatus(
        int inTransit,
        int atLab,
        int complete,
        int reportStatus,
        string expected)
    {
        var lane = new SampleSurveyLaneDto
        {
            SampleCount = inTransit + atLab + complete,
            InTransitCount = inTransit,
            AtLabCount = atLab,
            CompleteCount = complete,
            ReportStatusId = reportStatus
        };

        Assert.Equal(expected, lane.WorkflowName);
    }
}
