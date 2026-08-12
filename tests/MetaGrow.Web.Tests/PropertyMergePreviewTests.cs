using ApiModels;

namespace MetaGrow.Web.Tests;

public sealed class PropertyMergePreviewTests
{
    [Fact]
    public void Move_decision_is_resolved_without_a_target_block()
    {
        var preview = Preview(new PropertyMergeBlockPreview
        {
            Action = PropertyMergeBlockAction.Move,
            SourceBlockId = 10
        });

        Assert.True(preview.CanRequestMerge);
    }

    [Fact]
    public void Merge_decision_requires_a_target_block()
    {
        var preview = Preview(new PropertyMergeBlockPreview
        {
            Action = PropertyMergeBlockAction.Merge,
            SourceBlockId = 10
        });

        Assert.False(preview.CanRequestMerge);
        Assert.True(preview.HasUnresolvedBlocks);
    }

    [Fact]
    public void Collision_prevents_a_merge_request()
    {
        var preview = Preview(new PropertyMergeBlockPreview
        {
            Action = PropertyMergeBlockAction.Merge,
            SourceBlockId = 10,
            TargetBlockId = 20,
            Collisions = [new PropertyMergeBlockCollision { IsBlocking = true }]
        });

        Assert.False(preview.CanRequestMerge);
        Assert.True(preview.HasBlockingCollisions);
    }

    private static PropertyMergePreview Preview(PropertyMergeBlockPreview block) => new()
    {
        Blocks = [block]
    };
}
