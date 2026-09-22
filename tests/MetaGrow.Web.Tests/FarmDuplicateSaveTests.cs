using System.Reflection;
using ApiModels;
using MetaGrow.Web.Components.Pages;

namespace MetaGrow.Web.Tests;

public class FarmDuplicateSaveTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_RejectsExistingNameIncludingInactiveFarm(bool active)
    {
        var (page, api) = CreatePage(" river  farm ");
        api.Properties = [new() { PropertyId = 9, PropertyName = "River Farm", IsActive = active }];
        await Save(page);
        Assert.Equal(0, api.Writes);
        Assert.Contains("already exists", (string)typeof(Farms).GetField("_message", Hidden)!.GetValue(page)!);
        Assert.False((bool)typeof(Farms).GetField("_busy", Hidden)!.GetValue(page)!);
    }

    [Fact]
    public async Task Create_DoesNotWriteWhenFreshLookupFails()
    {
        var (page, api) = CreatePage("New Farm");
        api.Properties = null;
        await Save(page);
        Assert.Equal(0, api.Writes);
        Assert.Contains("Could not check", (string)typeof(Farms).GetField("_message", Hidden)!.GetValue(page)!);
    }

    [Fact]
    public async Task Create_SimilarNameRequiresConfirmationBeforeWrite()
    {
        var (page, api) = CreatePage("Rvier Farm");
        api.Properties = [new() { PropertyId = 9, PropertyName = "River Farm" }];
        await Save(page); // No confirmation component supplied: cannot approve the warning.
        Assert.Equal(0, api.Writes);
    }

    [Fact]
    public async Task Create_DistinctNameCanBeSaved()
    {
        var (page, api) = CreatePage("Hill Farm");
        api.Properties = [new() { PropertyId = 9, PropertyName = "River Farm" }];
        await Save(page);
        Assert.Equal(1, api.Writes);
    }

    private static (Farms, FarmApiProxy) CreatePage(string name)
    {
        var page = new Farms();
        var apiProperty = typeof(Farms).GetProperty("TgsApi", Hidden)!;
        var proxy = DispatchProxy.Create(apiProperty.PropertyType, typeof(FarmApiProxy));
        apiProperty.SetValue(page, proxy);
        typeof(Farms).GetField("_property", Hidden)!.SetValue(page, new Property { PropertyName = name });
        typeof(Farms).GetField("_isNewProperty", Hidden)!.SetValue(page, true);
        return (page, (FarmApiProxy)proxy);
    }

    private static Task Save(Farms page) => (Task)typeof(Farms).GetMethod("SavePropertyAsync", Hidden)!.Invoke(page, null)!;

    public class FarmApiProxy : DispatchProxy
    {
        public List<Property>? Properties { get; set; } = [];
        public int Writes { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "GetProperties") return Task.FromResult(Properties);
            if (method.Name == "get_ErrorMessage") return "Lookup failed";
            if (method.Name == "CreatePropertyForMaintenance")
            {
                Writes++;
                var property = (Property)args![0]!;
                property.PropertyId = 10;
                return Task.FromResult<Property?>(property);
            }
            throw new InvalidOperationException($"Unexpected API call: {method.Name}");
        }
    }
}
