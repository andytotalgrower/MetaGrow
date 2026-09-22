using System.Collections;
using System.Reflection;
using MetaGrow.Web.Components.Pages;

namespace MetaGrow.Web.Tests;

public class FarmSurveyNavigationTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(typeof(Samples))]
    [InlineData(typeof(Banana))]
    [InlineData(typeof(MultiCrop))]
    public void FarmLink_MatchesIdDespiteDuplicateOrRenamedFarm(Type pageType)
    {
        var page = Activator.CreateInstance(pageType)!;
        pageType.GetProperty("RequestedPropertyId")!.SetValue(page, 42);
        pageType.GetProperty("RequestedProperty")!.SetValue(page, "Old farm name");
        ApplyLinkedFilter(page);
        var surveys = (IList)pageType.GetField("_surveys", PrivateInstance)!.GetValue(page)!;
        AddSurvey(surveys, 1, 42, "Renamed farm");
        AddSurvey(surveys, 2, 77, "Renamed farm");
        AddSurvey(surveys, 3, 88, "Old farm name");

        var result = Results(page);
        Assert.Single(result);
        Assert.Equal(1, result[0].GetType().GetProperty("SurveyId")!.GetValue(result[0]));

        pageType.GetProperty("RequestedPropertyId")!.SetValue(page, 99);
        ApplyLinkedFilter(page);
        Assert.Empty(Results(page));

        pageType.GetProperty("RequestedPropertyId")!.SetValue(page, 77);
        ApplyLinkedFilter(page);
        Assert.Equal(2, Assert.Single(Results(page)).GetType().GetProperty("SurveyId")!.GetValue(Results(page)[0]));
    }

    [Theory]
    [InlineData(typeof(Samples))]
    [InlineData(typeof(Banana))]
    [InlineData(typeof(MultiCrop))]
    public void IdOnlyLink_ExpandsDatesAndClearsUnrelatedFilters(Type pageType)
    {
        var page = Activator.CreateInstance(pageType)!;
        pageType.GetProperty("RequestedPropertyId")!.SetValue(page, 42);
        pageType.GetField("_propertySearch", PrivateInstance)!.SetValue(page, "Another farm");
        foreach (var name in new[] { "_statusId", "_applicationId", "_agronomistId", "_labId", "_testTypeId" })
            pageType.GetField(name, PrivateInstance)?.SetValue(page, 123);
        ApplyLinkedFilter(page);

        Assert.Equal(new DateTime(1990, 1, 1), pageType.GetField("_startDate", PrivateInstance)!.GetValue(page));
        Assert.Equal(DateTime.Today, pageType.GetField("_endDate", PrivateInstance)!.GetValue(page));
        Assert.Null(pageType.GetField("_propertySearch", PrivateInstance)!.GetValue(page));
        foreach (var name in new[] { "_statusId", "_applicationId", "_agronomistId", "_labId", "_testTypeId" })
            Assert.Null(pageType.GetField(name, PrivateInstance)?.GetValue(page));
        if (pageType == typeof(Samples))
            Assert.Equal(new DateTime(1990, 1, 1), pageType.GetField("_appliedStartDate", PrivateInstance)!.GetValue(page));
    }

    [Theory]
    [InlineData(typeof(Samples))]
    [InlineData(typeof(Banana))]
    [InlineData(typeof(MultiCrop))]
    public void LegacyNameOnlyLink_StillFiltersByName(Type pageType)
    {
        var page = Activator.CreateInstance(pageType)!;
        pageType.GetProperty("RequestedProperty")!.SetValue(page, " River ");
        ApplyLinkedFilter(page);
        var surveys = (IList)pageType.GetField("_surveys", PrivateInstance)!.GetValue(page)!;
        AddSurvey(surveys, 1, 42, "River farm");
        AddSurvey(surveys, 2, 77, "Hill farm");
        Assert.Single(Results(page));
    }

    private static void ApplyLinkedFilter(object page) =>
        page.GetType().GetMethod("ApplyLinkedPropertyFilter", PrivateInstance)!.Invoke(page, null);

    private static List<object> Results(object page) =>
        ((IEnumerable)page.GetType().GetProperty("FilteredSurveys", PrivateInstance)!.GetValue(page)!).Cast<object>().ToList();

    private static void AddSurvey(IList surveys, int id, int propertyId, string name)
    {
        var type = surveys.GetType().GetGenericArguments()[0];
        var survey = Activator.CreateInstance(type)!;
        type.GetProperty("SurveyId")!.SetValue(survey, id);
        type.GetProperty("PropertyId")!.SetValue(survey, propertyId);
        type.GetProperty("PropertyName")!.SetValue(survey, name);
        surveys.Add(survey);
    }
}
