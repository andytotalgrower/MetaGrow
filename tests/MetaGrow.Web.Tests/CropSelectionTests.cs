using System.Reflection;
using ApiModels;
using MetaGrow.Web.Components.Pages;
using Microsoft.JSInterop;

namespace MetaGrow.Web.Tests;

public class CropSelectionTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly CropType First = new() { CropTypeId = 1, CropTypeName = "Corn" };
    private static readonly CropType Second = new() { CropTypeId = 2, CropTypeName = "Banana" };

    [Fact]
    public async Task BrowsingSwitchesImmediatelyWithoutConfirmation()
    {
        var (page, js) = Setup();
        await Select(page, First);
        await Select(page, Second);
        Assert.Equal(2, Editor(page).CropTypeId);
        Assert.Equal(0, js.Calls);
        Assert.False((bool)typeof(CropTypes).GetProperty("HasUnsavedChanges", Hidden)!.GetValue(page)!);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task UnsavedChangesRequireDiscardDecision(bool discard, int expected)
    {
        var (page, js) = Setup();
        js.Discard = discard;
        await Select(page, First);
        Editor(page).CropTypeName = "Sweet Corn";
        await Select(page, Second);
        Assert.Equal(expected, Editor(page).CropTypeId);
        Assert.Equal(1, js.Calls);
        if (!discard) Assert.Equal("Sweet Corn", Editor(page).CropTypeName);
        Assert.Equal("Corn", First.CropTypeName);
    }

    [Fact]
    public async Task ClickingCurrentCropDoesNotResetDraft()
    {
        var (page, js) = Setup();
        await Select(page, First);
        Editor(page).ParentCropTypeId = 3;
        await Select(page, First);
        Assert.Equal(3, Editor(page).ParentCropTypeId);
        Assert.Equal(0, js.Calls);
    }

    private static (CropTypes, ConfirmJs) Setup()
    {
        var page = new CropTypes();
        var js = new ConfirmJs();
        typeof(CropTypes).GetProperty("JS", Hidden)!.SetValue(page, js);
        return (page, js);
    }
    private static CropType Editor(CropTypes page) => (CropType)typeof(CropTypes).GetField("_editor", Hidden)!.GetValue(page)!;
    private static Task Select(CropTypes page, CropType crop) => (Task)typeof(CropTypes).GetMethod("RequestEditAsync", Hidden)!.Invoke(page, [crop])!;

    private sealed class ConfirmJs : IJSRuntime
    {
        public bool Discard;
        public int Calls;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Assert.Equal("confirm", identifier);
            Calls++;
            return ValueTask.FromResult((TValue)(object)Discard);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
