using System.Reflection;
using ApiModels;
using MetaGrow.Web.Components.Shared;

namespace MetaGrow.Web.Tests;

public class BlockMergeDialogTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Execution_requires_clean_preview_and_explicit_confirmation()
    {
        var dialog = new BlockMergeReviewDialog();
        var preview = Preview();
        Set(dialog, "_preview", preview);
        Assert.False(CanExecute(dialog));
        Set(dialog, "_confirmed", true);
        Assert.True(CanExecute(dialog));
        preview.Issues.Add(new() { Code = "SHARED_SURVEY", Message = "Overlapping survey values" });
        Assert.False(CanExecute(dialog));
    }

    [Fact]
    public void Inactive_target_requires_additional_acknowledgement()
    {
        var dialog = new BlockMergeReviewDialog();
        var preview = Preview();
        preview.Target!.IsActive = false;
        Set(dialog, "_preview", preview);
        Set(dialog, "_confirmed", true);
        Assert.False(CanExecute(dialog));
        Set(dialog, "_inactiveAcknowledged", true);
        Assert.True(CanExecute(dialog));
    }

    [Fact]
    public void Pending_or_running_merge_disables_another_submission()
    {
        var dialog = new BlockMergeReviewDialog();
        Set(dialog, "_preview", Preview());
        Set(dialog, "_confirmed", true);
        Set(dialog, "_working", true);
        Assert.False(CanExecute(dialog));
        Set(dialog, "_working", false);
        Set(dialog, "_pending", new MultiCropBlockMergeExecution { RequestId = Guid.NewGuid() });
        Assert.False(CanExecute(dialog));
    }

    [Fact]
    public async Task Unrelated_status_receipt_cannot_clear_pending_request()
    {
        var dialog = new BlockMergeReviewDialog();
        var pending = new MultiCropBlockMergeExecution { RequestId = Guid.NewGuid() };
        Set(dialog, "_pending", pending);
        await (Task)typeof(BlockMergeReviewDialog).GetMethod("HandleStatus", Hidden)!.Invoke(dialog, [new MultiCropBlockMergeStatus { RequestId = Guid.NewGuid(), Status = "Completed" }])!;
        Assert.Same(pending, typeof(BlockMergeReviewDialog).GetField("_pending", Hidden)!.GetValue(dialog));
    }

    private static MultiCropBlockMergePreview Preview() => new()
    {
        Source = new() { BlockId = 1, PropertyId = 10, BlockName = "Old spelling", CropTypeId = 7 },
        Target = new() { BlockId = 2, PropertyId = 10, BlockName = "Kept name", CropTypeId = 7, IsActive = true },
        Fingerprint = new('A', 64)
    };
    private static bool CanExecute(BlockMergeReviewDialog dialog) => (bool)typeof(BlockMergeReviewDialog).GetProperty("CanExecute", Hidden)!.GetValue(dialog)!;
    private static void Set(BlockMergeReviewDialog dialog, string field, object value) => typeof(BlockMergeReviewDialog).GetField(field, Hidden)!.SetValue(dialog, value);
}
