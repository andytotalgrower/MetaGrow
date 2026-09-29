using System.Collections;
using System.Reflection;
using MetaGrow.Web.Components.Pages;

namespace MetaGrow.Web.Tests;

public sealed class SampleImportBlockSelectionTests
{
    [Theory]
    [InlineData(typeof(SampleImportPlaceholder), "mcs:1", "mcs:1", true)]
    [InlineData(typeof(SampleImportPlaceholder), "mcs:1", "banana:1", true)]
    [InlineData(typeof(SampleImportPlaceholder), "mcs:1", "mcs:2", false)]
    [InlineData(typeof(SampleImportPlaceholder), "", "", false)]
    [InlineData(typeof(SampleSoilSurveyGenerator), "mcs:1", "mcs:1", true)]
    [InlineData(typeof(SampleSoilSurveyGenerator), "mcs:1", "banana:1", true)]
    [InlineData(typeof(SampleSoilSurveyGenerator), "mcs:1", "mcs:2", false)]
    [InlineData(typeof(SampleSoilSurveyGenerator), "", "", false)]
    public void Duplicate_block_validation_matches_production(Type pageType, string first, string second, bool expected)
    {
        var page = Activator.CreateInstance(pageType)!;
        var field = pageType.GetField("_mappingRows", BindingFlags.NonPublic | BindingFlags.Instance);
        var mappings = pageType.GetProperty("_mappingRows", BindingFlags.NonPublic | BindingFlags.Instance);
        var rows = (IList)(field?.GetValue(page) ?? mappings!.GetValue(page))!;
        var rowType = (field?.FieldType ?? mappings!.PropertyType).GetGenericArguments()[0];
        foreach (var key in new[] { first, second })
        {
            var row = Activator.CreateInstance(rowType)!;
            rowType.GetProperty("BlockKey")!.SetValue(row, key);
            rows.Add(row);
        }
        var property = pageType.GetProperty("HasDuplicateBlocks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(expected, property.GetValue(page));
    }
}
