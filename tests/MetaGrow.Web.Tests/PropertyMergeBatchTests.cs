using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Shared;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public class PropertyMergeBatchTests
{
    [Fact]
    public void Selection_excludes_destination_and_deduplicates_ids()
    {
        var session = new PropertyMergeBatchSession(10, new Gateway());
        session.Select(10, "Main", true);
        session.Select(1, "Variant", true);
        session.Select(1, "Variant again", true);
        Assert.Equal(1, Assert.Single(session.Items).SourceId);
    }

    [Fact]
    public void Initial_plan_keeps_destination_fields_including_empty_values()
    {
        var plan = PropertyMergeBatchReview.InitialPlan(1, 10);
        Assert.Equal(15, plan.FieldChoices.Count);
        Assert.All(plan.FieldChoices, field => Assert.Equal(PropertyMergeFieldValue.Target, field.ValueSource));
    }

    [Fact]
    public async Task Multiple_merges_keep_the_same_destination_and_skip_completed_items()
    {
        var gateway = new Gateway();
        var session = await Ready(gateway);
        await session.RunAsync(Changed);
        await session.RunAsync(Changed);
        Assert.Equal(new[] { 1, 2 }, gateway.Executions.Select(request => request.Plan.SourcePropertyId));
        Assert.All(gateway.Executions, request => Assert.Equal(10, request.Plan.TargetPropertyId));
        Assert.All(session.Items, item => Assert.Equal(BatchMergeState.Completed, item.State));
        session.Select(1, "Variant", false);
        Assert.Equal(2, session.Items.Count);
    }

    [Fact]
    public async Task New_matching_block_after_first_merge_pauses_second_for_review()
    {
        var gateway = new Gateway();
        var session = await Ready(gateway);
        gateway.PreviewFactory = plan =>
        {
            var preview = Preview(plan);
            if (gateway.Executions.Count > 0) preview.Blocks[0].Candidates.Add(new()
            {
                BlockId = 500, BlockName = "Block A", MatchScore = 1, IsActive = true
            });
            return preview;
        };
        await session.RunAsync(Changed);
        Assert.Single(gateway.Executions);
        Assert.Equal(BatchMergeState.Completed, session.Items[0].State);
        Assert.Equal(BatchMergeState.NeedsReview, session.Items[1].State);
        Assert.False(session.CanRun);
    }

    [Fact]
    public async Task Changed_field_values_require_review_before_any_write()
    {
        var gateway = new Gateway();
        var session = await Ready(gateway);
        gateway.PreviewFactory = plan =>
        {
            var preview = Preview(plan);
            preview.FieldDifferences[0].TargetValue = "Changed name";
            return preview;
        };
        await session.RunAsync(Changed);
        Assert.Empty(gateway.Executions);
        Assert.Equal(BatchMergeState.NeedsReview, session.Items[0].State);
    }

    [Fact]
    public async Task Collision_on_later_farm_keeps_earlier_completed_merge()
    {
        var gateway = new Gateway();
        var session = await Ready(gateway);
        gateway.PreviewFactory = plan =>
        {
            var preview = Preview(plan);
            if (plan.SourcePropertyId == 2) preview.PropertyCollisions.Add(new() { Message = "Duplicate key" });
            return preview;
        };
        await session.RunAsync(Changed);
        Assert.Single(gateway.Executions);
        Assert.Equal(BatchMergeState.Completed, session.Items[0].State);
        Assert.Equal("Duplicate key", session.Items[1].Message);
    }

    [Fact]
    public async Task Lost_response_requires_verification_and_does_not_resubmit_completed_merge()
    {
        var gateway = new Gateway { LoseResponse = true };
        var session = await Ready(gateway);
        await session.RunAsync(Changed);
        Assert.Equal(BatchMergeState.VerifyResult, session.Items[0].State);
        await session.PrepareAsync(session.Items[0]);
        await session.RunAsync(Changed);
        Assert.Single(gateway.Executions);
        gateway.Status = [Receipt(gateway.Executions[0])];
        await session.VerifyAsync(session.Items[0]);
        Assert.Equal(BatchMergeState.Completed, session.Items[0].State);
        gateway.LoseResponse = false;
        await session.RunAsync(Changed);
        Assert.Equal(new[] { 1, 2 }, gateway.Executions.Select(request => request.Plan.SourcePropertyId));
    }

    [Fact]
    public async Task In_progress_execution_cannot_be_retried_or_removed()
    {
        var gateway = new Gateway { LoseResponse = true };
        var session = await Ready(gateway);
        await session.RunAsync(Changed);
        var receipt = Receipt(gateway.Executions[0]);
        receipt.Status = MetaGrowPropertyMergeStatus.Processing;
        gateway.Status = [receipt];
        await session.VerifyAsync(session.Items[0]);
        session.Select(1, "Variant", false);
        Assert.Equal(BatchMergeState.VerifyResult, session.Items[0].State);
        Assert.False(session.CanRun);
    }

    [Fact]
    public async Task Missing_execution_receipt_preserves_request_id_for_safe_retry()
    {
        var gateway = new Gateway { LoseResponse = true };
        var session = await Ready(gateway);
        var id = session.Items[0].RequestId;
        await session.RunAsync(Changed);
        await session.VerifyAsync(session.Items[0]);
        Assert.Equal(id, session.Items[0].RequestId);
        Assert.Equal(BatchMergeState.NeedsReview, session.Items[0].State);
    }

    [Fact]
    public async Task Wrong_destination_or_replacing_its_name_is_rejected()
    {
        var session = await Ready(new Gateway());
        var preview = Preview(PropertyMergeBatchReview.InitialPlan(1, 99));
        Assert.Throws<InvalidOperationException>(() => session.AcceptReview(session.Items[0], preview));
        preview.Target.PropertyId = 10;
        preview.FieldDifferences[0].ValueSource = PropertyMergeFieldValue.Source;
        Assert.Throws<InvalidOperationException>(() => session.AcceptReview(session.Items[0], preview));
    }

    [Fact]
    public async Task Closing_page_stops_before_the_next_merge()
    {
        var gateway = new Gateway();
        var session = await Ready(gateway);
        await session.RunAsync(() =>
        {
            if (gateway.Executions.Count == 1) session.Stop();
            return Task.CompletedTask;
        });
        Assert.Single(gateway.Executions);
        Assert.Equal(BatchMergeState.Ready, session.Items[1].State);
    }

    [Fact]
    public void Destination_totals_can_grow_without_invalidating_unaffected_plan()
    {
        var preview = Preview(PropertyMergeBatchReview.InitialPlan(1, 10));
        var hash = PropertyMergeBatchReview.Fingerprint(preview);
        preview.Target.IncludedRowCount += 123;
        preview.Target.MultiCropBlockCount += 1;
        Assert.Equal(hash, PropertyMergeBatchReview.Fingerprint(preview));
    }

    private static async Task<PropertyMergeBatchSession> Ready(Gateway gateway)
    {
        var session = new PropertyMergeBatchSession(10, gateway);
        session.Select(1, "Variant one", true);
        session.Select(2, "Variant two", true);
        foreach (var item in session.Items) await session.PrepareAsync(item);
        Assert.True(session.CanRun);
        return session;
    }

    private static Task Changed() => Task.CompletedTask;
    private static PropertyMergePreview Preview(PropertyMergePreviewRequest plan) => new()
    {
        Source = new() { PropertyId = plan.SourcePropertyId, PropertyName = "Variant", IsActive = true },
        Target = new() { PropertyId = plan.TargetPropertyId, PropertyName = "Greensill Qunaba", IsActive = true },
        FieldDifferences = [new() { FieldName = "PropertyName", SourceValue = "Variant", TargetValue = "Greensill Qunaba" }],
        Blocks = [new() { BlockType = "multicrop", SourceBlockId = plan.SourcePropertyId * 100, SourceBlockName = "Block A", Action = PropertyMergeBlockAction.Move }]
    };

    private static MetaGrowPropertyMergeRequestDto Receipt(ReviewedPropertyMergeRequest request) => new()
    {
        Id = request.RequestId, SourcePropertyId = request.Plan.SourcePropertyId, TargetPropertyId = request.Plan.TargetPropertyId,
        Status = MetaGrowPropertyMergeStatus.Completed
    };

    private sealed class Gateway : IPropertyMergeBatchGateway
    {
        public Func<PropertyMergePreviewRequest, PropertyMergePreview> PreviewFactory { get; set; } = Preview;
        public List<ReviewedPropertyMergeRequest> Executions { get; } = [];
        public bool LoseResponse { get; set; }
        public MetaGrowPropertyMergeRequestDto[] Status { get; set; } = [];
        public Task<(PropertyMergePreview?, string?)> PreviewAsync(PropertyMergePreviewRequest plan) => Task.FromResult<(PropertyMergePreview?, string?)>((PreviewFactory(plan), null));
        public Task<(MetaGrowPropertyMergeRequestDto?, string?)> ExecuteAsync(ReviewedPropertyMergeRequest request)
        {
            Executions.Add(request);
            return Task.FromResult<(MetaGrowPropertyMergeRequestDto?, string?)>(LoseResponse ? (null, "Connection lost") : (Receipt(request), null));
        }
        public Task<(MetaGrowPropertyMergeRequestDto[]?, string?)> StatusAsync(Guid requestId) => Task.FromResult<(MetaGrowPropertyMergeRequestDto[]?, string?)>((Status, null));
    }
}
