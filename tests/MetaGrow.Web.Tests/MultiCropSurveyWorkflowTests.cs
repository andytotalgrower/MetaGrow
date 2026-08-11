using ApiModels;

namespace MetaGrow.Web.Tests;

public sealed class MultiCropSurveyWorkflowTests
{
    [Theory]
    [InlineData(MultiCropSurveyStatus.NotStarted, MultiCropSurveyWorkflowAction.StartEditing, MultiCropSurveyStatus.InProgress)]
    [InlineData(MultiCropSurveyStatus.InProgress, MultiCropSurveyWorkflowAction.ReturnToNotStarted, MultiCropSurveyStatus.NotStarted)]
    [InlineData(MultiCropSurveyStatus.InProgress, MultiCropSurveyWorkflowAction.SubmitForQa, MultiCropSurveyStatus.AwaitingQa)]
    [InlineData(MultiCropSurveyStatus.AwaitingQa, MultiCropSurveyWorkflowAction.ReturnToEditing, MultiCropSurveyStatus.InProgress)]
    [InlineData(MultiCropSurveyStatus.AwaitingQa, MultiCropSurveyWorkflowAction.CompleteQa, MultiCropSurveyStatus.ReadyToSend)]
    [InlineData(MultiCropSurveyStatus.ReadyToSend, MultiCropSurveyWorkflowAction.ReturnToQa, MultiCropSurveyStatus.AwaitingQa)]
    [InlineData(MultiCropSurveyStatus.ReadyToSend, MultiCropSurveyWorkflowAction.MarkSent, MultiCropSurveyStatus.Complete)]
    [InlineData(MultiCropSurveyStatus.Complete, MultiCropSurveyWorkflowAction.ReopenForSending, MultiCropSurveyStatus.ReadyToSend)]
    public void GetTargetStatus_AllowsEachForwardAndBackwardStep(
        int currentStatus,
        MultiCropSurveyWorkflowAction action,
        int expectedStatus)
    {
        Assert.Equal(expectedStatus, MultiCropSurveyWorkflow.GetTargetStatus(currentStatus, action));
    }

    [Fact]
    public void GetTargetStatus_RejectsSkippingWorkflowStages()
    {
        Assert.Null(MultiCropSurveyWorkflow.GetTargetStatus(
            MultiCropSurveyStatus.InProgress,
            MultiCropSurveyWorkflowAction.MarkSent));
    }

    [Theory]
    [InlineData(14, 14, false)]
    [InlineData(14, 0, false)]
    [InlineData(14, 22, true)]
    public void CanCompleteQa_RequiresAnotherKnownAgronomist(
        int surveyAgronomistId,
        int actingAgronomistId,
        bool expected)
    {
        Assert.Equal(expected, MultiCropSurveyWorkflow.CanCompleteQa(surveyAgronomistId, actingAgronomistId));
    }
}
