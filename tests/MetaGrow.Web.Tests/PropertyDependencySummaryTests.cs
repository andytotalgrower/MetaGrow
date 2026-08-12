using ApiModels;

namespace MetaGrow.Web.Tests;

public class PropertyDependencySummaryTests
{
    [Fact]
    public void ActivePropertyWithoutBlocksOrReferencesCanBeDeleted()
    {
        var summary = new PropertyDependencySummary { IsActive = true };

        Assert.True(summary.MeetsDeletionRule);
        Assert.True(summary.PhysicalDeleteAllowed);
    }

    [Fact]
    public void InactivePropertyWithLinkedHistoryRequiresMerge()
    {
        var summary = new PropertyDependencySummary
        {
            IsActive = false,
            Dependencies =
            [
                new PropertyDependencyItem { TableName = "TgsVisitAudit", RowCount = 12 }
            ]
        };

        Assert.True(summary.MeetsDeletionRule);
        Assert.False(summary.PhysicalDeleteAllowed);
        Assert.Equal(12, summary.LinkedRowCount);
    }

    [Fact]
    public void ActivePropertyWithBlocksCannotBeDeleted()
    {
        var summary = new PropertyDependencySummary
        {
            IsActive = true,
            BananaBlockCount = 1,
            Dependencies =
            [
                new PropertyDependencyItem { TableName = "TgsBlock", RowCount = 1 }
            ]
        };

        Assert.False(summary.MeetsDeletionRule);
        Assert.False(summary.PhysicalDeleteAllowed);
    }
}
