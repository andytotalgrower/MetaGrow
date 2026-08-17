using ApiModels;
using MetaGrow.Web.Services;

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
    public void InactivePropertyWithRetainedBackupHistoryCanBeDeleted()
    {
        var summary = new PropertyDependencySummary
        {
            IsActive = false,
            Dependencies =
            [
                new PropertyDependencyItem
                {
                    TableName = "TgsLabSoil_20240610",
                    RowCount = 4,
                    BlocksDeletion = false
                },
                new PropertyDependencyItem
                {
                    TableName = "TgsLabSoil_bak",
                    RowCount = 4,
                    BlocksDeletion = false
                }
            ]
        };

        Assert.True(summary.MeetsDeletionRule);
        Assert.True(summary.PhysicalDeleteAllowed);
        Assert.Equal(8, summary.LinkedRowCount);
        Assert.Equal(0, summary.BlockingLinkedRowCount);
        Assert.Equal(8, summary.RetainedLinkedRowCount);
        Assert.Equal(2, summary.RetainedDependencyCount);
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

    [Fact]
    public void LinkedDataPresentationGroupsLabAndOperationalTablesIntoReadableCategories()
    {
        var summary = new PropertyDependencySummary
        {
            Dependencies =
            [
                new PropertyDependencyItem { TableName = "TgsLabSoil", RowCount = 7 },
                new PropertyDependencyItem { TableName = "TgsLabSoilAudit", RowCount = 2 },
                new PropertyDependencyItem { TableName = "TgsLabTissue", RowCount = 3 },
                new PropertyDependencyItem { TableName = "TgsVisit", RowCount = 1 }
            ]
        };

        var categories = PropertyDependencyPresentation.Summarize(summary);

        Assert.Collection(
            categories,
            item =>
            {
                Assert.Equal("9 soil lab records", item.DisplayText);
                Assert.Equal("/surveys/samples/lab-results?type=soil", item.FinderPath);
            },
            item =>
            {
                Assert.Equal("3 tissue lab records", item.DisplayText);
                Assert.Equal("/surveys/samples/lab-results?type=tissue", item.FinderPath);
            },
            item =>
            {
                Assert.Equal("1 banana survey", item.DisplayText);
                Assert.Equal("/surveys/banana", item.FinderPath);
            });
    }

    [Fact]
    public void LinkedDataPresentationKeepsUnrecognisedTablesVisible()
    {
        var summary = new PropertyDependencySummary
        {
            Dependencies =
            [
                new PropertyDependencyItem { TableName = "TgsFutureFeature", RowCount = 4 }
            ]
        };

        var category = Assert.Single(PropertyDependencyPresentation.Summarize(summary));

        Assert.Equal("4 other linked records", category.DisplayText);
    }

    [Fact]
    public void MultiCropSurveyCountIsKeptSeparateFromItsRelatedDataRows()
    {
        var summary = new PropertyDependencySummary
        {
            Dependencies =
            [
                new PropertyDependencyItem { TableName = "TgsFarmSurvey", RowCount = 2 },
                new PropertyDependencyItem { TableName = "TgsFarmSurveyAudit", RowCount = 5 }
            ]
        };

        var categories = PropertyDependencyPresentation.Summarize(summary);

        Assert.Contains(categories, item => item.DisplayText == "2 multi-crop surveys" && item.FinderPath == "/surveys/multicrop");
        Assert.Contains(categories, item => item.DisplayText == "5 multi-crop survey data rows" && item.FinderPath == "/surveys/multicrop");
    }

    [Fact]
    public void QuickSoilAndSapUseTheirUnifiedResultTabs()
    {
        var summary = new PropertyDependencySummary
        {
            Dependencies =
            [
                new PropertyDependencyItem { TableName = "TgsLabQuickSoil", RowCount = 2 },
                new PropertyDependencyItem { TableName = "TgsLabSap", RowCount = 1 }
            ]
        };

        var categories = PropertyDependencyPresentation.Summarize(summary);

        Assert.Contains(categories, item => item.DisplayText == "2 quick soil lab records" && item.FinderPath?.Contains("type=quick-soil") == true);
        Assert.Contains(categories, item => item.DisplayText == "1 sap lab record" && item.FinderPath?.Contains("type=sap") == true);
    }
}
