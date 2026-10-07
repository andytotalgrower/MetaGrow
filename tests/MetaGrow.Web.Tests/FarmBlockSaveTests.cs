using System.Reflection;
using ApiModels;
using MetaGrow.Web.Components.Pages;
using Metagen.Shared.Services;

namespace MetaGrow.Web.Tests;

public sealed class FarmBlockSaveTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("mcs")]
    [InlineData("banana")]
    public async Task Existing_block_without_crop_can_be_deactivated_and_editor_closes_after_reload(string type)
    {
        var (page, api) = CreatePage(type);
        await Save(page);
        Assert.Equal(1, api.Writes);
        Assert.False(api.Stored.IsActive);
        Assert.Equal(2, api.Reads);
        Assert.Null(Get(page, "_blockEditor"));
        Assert.Empty((IReadOnlyList<Block>)typeof(Farms).GetProperty("FilteredBlocks", Hidden)!.GetValue(page)!);
        Set(page, "_showInactiveBlocks", true);
        Assert.False(Assert.Single((IReadOnlyList<Block>)typeof(Farms).GetProperty("FilteredBlocks", Hidden)!.GetValue(page)!).IsActive);
        Assert.False((bool)Get(page, "_busy")!);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Crop_is_still_required_for_new_or_active_multicrop_blocks(bool isNew, bool active)
    {
        var (page, api) = CreatePage("mcs");
        Set(page, "_isNewBlock", isNew);
        ((Block)Get(page, "_blockEditor")!).IsActive = active;
        await Save(page);
        Assert.Equal(0, api.Writes);
        Assert.Contains("Choose a crop", (string)Get(page, "_message")!);
        Assert.NotNull(Get(page, "_blockEditor"));
    }

    [Theory]
    [InlineData("write")]
    [InlineData("refresh")]
    [InlineData("unchanged")]
    public async Task Failed_or_unconfirmed_save_keeps_editor_and_reports_error(string failure)
    {
        var (page, api) = CreatePage("mcs");
        api.Failure = failure;
        await Save(page);
        Assert.NotNull(Get(page, "_blockEditor"));
        Assert.True((bool)Get(page, "_messageIsError")!);
        Assert.False((bool)Get(page, "_busy")!);
        Assert.DoesNotContain("Block details saved", (string)Get(page, "_message")!);
    }

    private static (Farms, BlockApi) CreatePage(string type)
    {
        var page = new Farms();
        var api = DispatchProxy.Create<ITgsApiService, BlockApi>();
        typeof(Farms).GetProperty("TgsApi", Hidden)!.SetValue(page, api);
        Set(page, "_property", new Property { PropertyId = 10 });
        Set(page, "_activeBlockType", type);
        Set(page, "_blockEditor", new Block { BlockId = 1, PropertyId = 10, BlockName = "B1", IsActive = false, CropTypeId = 0 });
        return (page, (BlockApi)api);
    }

    private static Task Save(Farms page) => (Task)typeof(Farms).GetMethod("SaveBlockAsync", Hidden)!.Invoke(page, null)!;
    private static object? Get(Farms page, string name) => typeof(Farms).GetField(name, Hidden)!.GetValue(page);
    private static void Set(Farms page, string name, object value) => typeof(Farms).GetField(name, Hidden)!.SetValue(page, value);

    public class BlockApi : DispatchProxy
    {
        public Block Stored { get; private set; } = new() { BlockId = 1, PropertyId = 10, BlockName = "B1", IsActive = true };
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public string? Failure { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetMultiCropBlocksForProperty":
                case "GetBananaBlocksForProperty":
                    Reads++;
                    return Task.FromResult<List<Block>?>(Failure == "refresh" && Reads > 1 ? null : [Stored]);
                case "UpdateMultiCropBlock":
                case "UpdateBananaBlock":
                    Writes++;
                    if (Failure == "write") return Task.FromResult<Block?>(null);
                    var edited = (Block)args![0]!;
                    if (Failure != "unchanged") Stored = edited;
                    return Task.FromResult<Block?>(edited);
                case "get_ErrorMessage": return "Test failure";
                default: throw new InvalidOperationException(method.Name);
            }
        }
    }
}
