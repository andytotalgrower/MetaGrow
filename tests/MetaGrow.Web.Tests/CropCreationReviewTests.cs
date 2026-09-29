using System.Reflection;
using ApiModels;
using Metagen.Shared.Services;
using MetaGrow.Web.Components.Pages;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Web.Tests;

public class CropCreationReviewTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("sweetcorn", false)]
    [InlineData("Sweet Corn", true)]
    public async Task SaveRequiresReviewWithoutCallingMutation(string name, bool exact)
    {
        var (page, api) = Setup(name);
        await Invoke(page, "SaveAsync");
        Assert.Single((List<CropType>)Field(page, "_similarCrops")!);
        Assert.Equal(exact, Field(page, "_exactDuplicate"));
        Assert.Equal(0, api.Writes);
        if (exact)
        {
            await Invoke(page, "ConfirmCreateAsync");
            Assert.Equal(0, api.Writes);
        }
    }

    [Fact]
    public async Task ConfirmationRechecksListAndStopsForNewMatches()
    {
        var (page, api) = Setup("sweetcorn");
        await Invoke(page, "SaveAsync");
        api.Crops.Add(new() { CropTypeId = 2, CropTypeName = "Sweet Corns" });
        await Invoke(page, "ConfirmCreateAsync");
        Assert.Equal(2, ((List<CropType>)Field(page, "_similarCrops")!).Count);
        Assert.Equal(0, api.Writes);
        Assert.Equal(2, api.Reads);
    }

    [Fact]
    public async Task FailedDuplicateLookupDoesNotAllowCreation()
    {
        var (page, api) = Setup("Carrot");
        api.FailRead = true;
        await Invoke(page, "SaveAsync");
        Assert.Equal(0, api.Writes);
        Assert.Contains("could not be checked", (string)Field(page, "_message")!);
    }

    private static (CropTypes, CropApiProxy) Setup(string name)
    {
        var service = DispatchProxy.Create<ITgsApiService, CropApiProxy>();
        var proxy = (CropApiProxy)service;
        var page = new CropTypes();
        typeof(CropTypes).GetProperty("TgsApi", Hidden)!.SetValue(page, service);
        typeof(CropTypes).GetProperty("Logger", Hidden)!.SetValue(page, NullLogger<CropTypes>.Instance);
        typeof(CropTypes).GetField("_editor", Hidden)!.SetValue(page, new CropType { CropTypeName = name });
        return (page, proxy);
    }

    private static object? Field(CropTypes page, string name) => typeof(CropTypes).GetField(name, Hidden)!.GetValue(page);
    private static Task Invoke(CropTypes page, string method) => (Task)typeof(CropTypes).GetMethod(method, Hidden)!.Invoke(page, null)!;

    public class CropApiProxy : DispatchProxy
    {
        public List<CropType> Crops = [new() { CropTypeId = 1, CropTypeName = "Sweet Corn" }];
        public int Writes, Reads;
        public bool FailRead;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "GetCrops") { Reads++; return Task.FromResult<List<CropType>?>(FailRead ? null : Crops); }
            if (targetMethod.Name == "get_ErrorMessage") return null;
            if (targetMethod.Name == "SaveCropType") { Writes++; throw new InvalidOperationException("Unexpected save before review."); }
            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
