using ApiModels.MetaGrow;
using MetaGrow.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace MetaGrow.Api.Tests;

public sealed class RoleContractTests
{
    [Fact]
    public void Initial_registration_roles_are_exactly_the_agreed_roles()
    {
        Assert.Equal(["Admin", "Agriculture Manager", "Agronomist", "Accountant"], MetaGrowRoles.All);
    }

    [Theory]
    [InlineData(nameof(ReportSharesController.GetForSurvey))]
    [InlineData(nameof(ReportSharesController.Create))]
    [InlineData(nameof(ReportSharesController.Revoke))]
    public void Core_survey_roles_can_manage_report_shares(string methodName)
    {
        var method = typeof(ReportSharesController).GetMethod(methodName)!;
        var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal([MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager, MetaGrowRoles.Agronomist],
            authorization.Roles!.Split(',', StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void Agronomists_can_submit_property_deletion_requests()
    {
        AssertRoles(nameof(PropertyDeletionsController.Create), [MetaGrowRoles.Agronomist]);
    }

    [Theory]
    [InlineData(nameof(PropertyDeletionsController.DeleteImmediately))]
    [InlineData(nameof(PropertyDeletionsController.Approve))]
    [InlineData(nameof(PropertyDeletionsController.Reject))]
    public void Only_managers_and_administrators_can_execute_property_deletion(string methodName)
    {
        AssertRoles(methodName, [MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager]);
    }

    [Fact]
    public void Core_survey_roles_can_view_the_relevant_deletion_queue()
    {
        AssertRoles(nameof(PropertyDeletionsController.GetPending),
            [MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager, MetaGrowRoles.Agronomist]);
    }

    [Fact]
    public void Agronomists_can_submit_property_merge_requests()
    {
        AssertRoles<PropertyMergesController>(nameof(PropertyMergesController.Create), [MetaGrowRoles.Agronomist]);
    }

    [Theory]
    [InlineData(nameof(PropertyMergesController.ExecuteImmediately))]
    [InlineData(nameof(PropertyMergesController.Approve))]
    [InlineData(nameof(PropertyMergesController.Reject))]
    public void Only_managers_and_administrators_can_execute_or_reject_property_merges(string methodName)
    {
        AssertRoles<PropertyMergesController>(methodName,
            [MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager]);
    }

    [Fact]
    public void Core_survey_roles_can_view_the_relevant_merge_queue()
    {
        AssertRoles<PropertyMergesController>(nameof(PropertyMergesController.GetPending),
            [MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager, MetaGrowRoles.Agronomist]);
    }

    [Theory]
    [InlineData(nameof(SampleSurveyDeletionsController.GetPending))]
    [InlineData(nameof(SampleSurveyDeletionsController.Create))]
    [InlineData(nameof(SampleSurveyDeletionsController.Cancel))]
    public void All_sample_workflow_roles_can_view_or_request_deletion(string methodName)
    {
        AssertRoles<SampleSurveyDeletionsController>(methodName,
            [MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager, MetaGrowRoles.Agronomist, MetaGrowRoles.Accountant]);
    }

    [Theory]
    [InlineData(nameof(SampleSurveyDeletionsController.Approve))]
    [InlineData(nameof(SampleSurveyDeletionsController.Reject))]
    [InlineData(nameof(SampleSurveyDeletionsController.GetExecutionGrant))]
    public void Only_sample_deletion_reviewers_can_review_or_receive_execution_grants(string methodName)
    {
        AssertRoles<SampleSurveyDeletionsController>(methodName,
            [MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager, MetaGrowRoles.Accountant]);
    }

    private static void AssertRoles(string methodName, string[] expected)
    {
        var method = typeof(PropertyDeletionsController).GetMethod(methodName)!;
        var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(expected, authorization.Roles!.Split(',', StringSplitOptions.TrimEntries));
    }

    private static void AssertRoles<TController>(string methodName, string[] expected)
    {
        var method = typeof(TController).GetMethod(methodName)!;
        var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(expected, authorization.Roles!.Split(',', StringSplitOptions.TrimEntries));
    }
}
