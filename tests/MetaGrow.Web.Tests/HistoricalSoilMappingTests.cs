using System.Collections;
using System.Reflection;
using ApiModels;
using Metagen.Shared.Services;
using MetaGrow.Web.Components.Pages;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Web.Tests;

public class HistoricalSoilMappingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenMapping_CountsMatchingRowsAcrossAllDates(bool allDates)
    {
        var (page, api) = CreatePage();
        var visible = Group("Peter Formosa", "North", "Banana");
        api.Groups = [visible, Group(" peter formosa ", " north ", "BANANA"),
            Group("Peter Formosa", "South", "Banana"), Group("Other farm", "North", "Banana")];
        Set(page, "_groups", new List<HistoricalSoilResultGroupDto> { visible });
        Set(page, "_appliedAllDates", allDates);

        await Open(page, visible);

        Assert.Equal(1, api.Reads);
        var row = Assert.Single(((IEnumerable)Get(page, "_mappingRows")).Cast<object>());
        Assert.Equal(2, row.GetType().GetProperty("ExpectedRowCount")!.GetValue(row));
        Assert.Equal(allDates, Get(page, "_appliedAllDates"));
        Assert.Single((List<HistoricalSoilResultGroupDto>)Get(page, "_groups"));
        Assert.True((bool)Get(page, "_mappingRowsLoaded"));
        Assert.False((bool)Get(page, "_loadingMappingRows"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenMapping_LookupFailurePreventsSavingAndShowsError(bool throws)
    {
        var (page, api) = CreatePage();
        api.Groups = null;
        api.Throws = throws;
        await Open(page, Group("Peter Formosa", "North", "Banana"));

        Assert.False((bool)typeof(HistoricalSoilResults).GetProperty("CanSaveMappings", Hidden)!.GetValue(page)!);
        Assert.False((bool)Get(page, "_loadingMappingRows"));
        Assert.False((bool)Get(page, "_mappingRowsLoaded"));
        Assert.False(string.IsNullOrWhiteSpace((string)Get(page, "_dialogError")));
    }

    private static HistoricalSoilResultGroupDto Group(string customer, string field, string crop) => new()
    {
        RawCustomerName = customer,
        Results = [new() { RawCustomerName = customer, RawFieldReference = field, RawCropName = crop }]
    };

    private static (HistoricalSoilResults, MappingApiProxy) CreatePage()
    {
        var page = new HistoricalSoilResults();
        var api = DispatchProxy.Create<ITgsApiService, MappingApiProxy>();
        typeof(HistoricalSoilResults).GetProperty("TgsApi", Hidden)!.SetValue(page, api);
        typeof(HistoricalSoilResults).GetProperty("Similarity", Hidden)!.SetValue(page, new StringSimilarityService());
        typeof(HistoricalSoilResults).GetProperty("Logger", Hidden)!.SetValue(page, NullLogger<HistoricalSoilResults>.Instance);
        return (page, (MappingApiProxy)api);
    }

    private static Task Open(HistoricalSoilResults page, HistoricalSoilResultGroupDto group) =>
        (Task)typeof(HistoricalSoilResults).GetMethod("OpenMappingAsync", Hidden)!.Invoke(page, [group])!;
    private static object Get(HistoricalSoilResults page, string field) =>
        typeof(HistoricalSoilResults).GetField(field, Hidden)!.GetValue(page)!;
    private static void Set(HistoricalSoilResults page, string field, object value) =>
        typeof(HistoricalSoilResults).GetField(field, Hidden)!.SetValue(page, value);

    public class MappingApiProxy : DispatchProxy
    {
        public List<HistoricalSoilResultGroupDto>? Groups { get; set; }
        public int Reads { get; private set; }
        public bool Throws { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_ErrorMessage") return "Lookup failed";
            if (method.Name == "GetUnlinkedSampleSoilResultGroups")
            {
                Reads++;
                Assert.All(args!, value => Assert.Null(value));
                if (Throws) throw new HttpRequestException("Unavailable");
                return Task.FromResult(Groups);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
