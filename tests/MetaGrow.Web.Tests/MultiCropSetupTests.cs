using System.Reflection;
using System.Security.Claims;
using Metagen.Shared.Services;
using MetaGrow.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using DevExpress.Blazor;
using ApiModels.MetaGrow;
using MetaGrow.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using MetaGrow.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MetaGrow.Web.Tests;

public class MultiCropSetupTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormOnlyDisablesInputsDuringSave(bool busy)
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddDevExpressBlazor();
        collection.AddSingleton<IJSRuntime, StaticJs>();
        collection.AddSingleton<NavigationManager, TestNavigation>();
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<MultiCropSetupForm>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Model"] = new SurveyTypeSetup(), ["Catalogue"] = new MultiCropSetupCatalogue(), ["Busy"] = busy
            }));
            return output.ToHtmlString();
        });
        Assert.Equal(busy, html.Contains("<fieldset disabled"));
        Assert.Contains("Multiple crops", html);
        Assert.Contains(busy ? "Saving…" : "Save changes", System.Net.WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData(true, true, "types")]
    [InlineData(false, false, "types")]
    [InlineData(false, false, "parameters")]
    [InlineData(false, false, "products")]
    [InlineData(false, true, "types")]
    [InlineData(false, true, "parameters")]
    [InlineData(false, true, "products")]
    public async Task SetupPageRendersWhileLoadingOrSaving(bool loading, bool busy, string section)
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddDevExpressBlazor();
        collection.AddSingleton<IJSRuntime, StaticJs>();
        collection.AddSingleton<NavigationManager, TestNavigation>();
        collection.AddSingleton<ITgsApiService>(DispatchProxy.Create<ITgsApiService, UnusedApi>());
        collection.AddSingleton(new ApiTokenService(null!, null!));
        collection.AddSingleton<AuthenticationStateProvider, TestAuthentication>();
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SetupPageForRendering>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Loading"] = loading, ["Busy"] = busy, ["Section"] = section }));
            return output.ToHtmlString();
        });
        Assert.Contains("Multi-crop survey setup", html);
        Assert.Contains("Shared parameters", html);
        Assert.Contains("Spray products", html);
        Assert.Contains(loading ? "Loading survey setup" : "Setup editor", html);
    }

    public class SetupPageForRendering : MultiCropSetup
    {
        private int _renders;
        [Parameter] public bool Loading { get; set; }
        [Parameter] public bool Busy { get; set; }
        [Parameter] public string Section { get; set; } = "types";
        protected override Task OnInitializedAsync()
        {
            typeof(MultiCropSetup).GetField("_busy", Hidden)!.SetValue(this, Busy);
            typeof(MultiCropSetup).GetField("_tab", Hidden)!.SetValue(this, Section);
            typeof(MultiCropSetup).GetField("_loading", Hidden)!.SetValue(this, Loading);
            typeof(MultiCropSetup).GetField("_catalogue", Hidden)!.SetValue(this, Loading ? null : new MultiCropSetupCatalogue());
            return Task.CompletedTask;
        }
        protected override bool ShouldRender()
        {
            if (++_renders > 20) throw new InvalidOperationException("The setup page is stuck in a render loop.");
            return true;
        }
    }
    public class UnusedApi : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Rendering should not call the API.");
    }
    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void SetupAndAdministrationAllowBothRolesWhileCropMaintenanceStaysAdminOnly()
    {
        foreach (var type in new[] { typeof(MultiCropSetup), typeof(Admin) })
            Assert.Equal(new[] { MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager }, type.GetCustomAttributes<AuthorizeAttribute>().Single(a => a.Roles is not null).Roles!.Split(','));
        Assert.Equal(MetaGrowRoles.Admin, typeof(CropTypes).GetCustomAttributes<AuthorizeAttribute>().Single(a => a.Roles is not null).Roles);
    }

    [Fact]
    public async Task EditingMakesAnIndependentDraftAndDetectsUnsavedChanges()
    {
        var page = new MultiCropSetup();
        var source = new SurveyTypeSetup { ApplicationId = 4, ApplicationName = "Citrus", ApplicationCode = "CIT", CropTypeId = 2 };
        await Invoke(page, "Edit", source, "Edit type");
        var draft = Assert.IsType<SurveyTypeSetup>(Field(page, "_draft"));
        Assert.NotSame(source, draft);
        Assert.False(Dirty(page));
        draft.ApplicationName = "Changed";
        Assert.True(Dirty(page));
        Assert.Equal("Citrus", source.ApplicationName);
    }

    [Fact]
    public async Task CloneStartsNewIdentityAndKeepsReportSettings()
    {
        var page = new MultiCropSetup();
        var source = new SurveyTypeSetup { ApplicationId = 4, ApplicationName = "Citrus", ApplicationCode = "CIT", CropTypeId = 2, ComplianceText = "Compliance", ShowGraphDisease = false, ShowGridPest = false };
        await Invoke(page, "Clone", source);
        var clone = Assert.IsType<SurveyTypeSetup>(Field(page, "_draft"));
        Assert.Equal(4, Field(page, "_cloneSource"));
        Assert.Equal(0, clone.ApplicationId);
        Assert.Empty(clone.ApplicationName);
        Assert.Empty(clone.ApplicationCode);
        Assert.Equal(2, clone.CropTypeId);
        Assert.Equal("Compliance", clone.ComplianceText);
        Assert.False(clone.ShowGraphDisease);
        Assert.False(clone.ShowGridPest);
        Assert.Equal("Citrus", source.ApplicationName);
    }

    [Fact]
    public async Task DecliningDiscardKeepsDraftAndSelection()
    {
        var page = new MultiCropSetup();
        var js = new Confirm(false);
        typeof(MultiCropSetup).GetProperty("JS", Hidden)!.SetValue(page, js);
        await Invoke(page, "Edit", new SurveyTypeSetup(), "New type");
        var draft = (SurveyTypeSetup)Field(page, "_draft")!;
        draft.ApplicationName = "Unsaved";
        await Invoke(page, "ChangeSection", 2);
        Assert.Same(draft, Field(page, "_draft"));
        Assert.Equal("types", Field(page, "_tab"));
        Assert.Equal(1, Field(page, "_tabsVersion"));
        Assert.Equal(1, js.Calls);
    }

    [Fact]
    public async Task DecliningDiscardPreventsReorder()
    {
        var page = new MultiCropSetup();
        typeof(MultiCropSetup).GetProperty("JS", Hidden)!.SetValue(page, new Confirm(false));
        await Invoke(page, "Edit", new SurveyGroupSetup(), "Edit group");
        var draft = (SurveyGroupSetup)Field(page, "_draft")!;
        draft.GroupName = "Unsaved";
        // No API is injected: declining must return before attempting a save.
        await Invoke(page, "Move", "groups", 1, -1);
        Assert.Same(draft, Field(page, "_draft"));
    }

    [Fact]
    public void OrderingUsesIdToBreakTiesAndKeepsOtherParentsOut()
    {
        var page = new MultiCropSetup();
        typeof(MultiCropSetup).GetField("_typeId", Hidden)!.SetValue(page, 1);
        typeof(MultiCropSetup).GetField("_groupId", Hidden)!.SetValue(page, 2);
        typeof(MultiCropSetup).GetField("_catalogue", Hidden)!.SetValue(page, new MultiCropSetupCatalogue
        {
            Groups = [new() { ApplicationId = 1, ApplicationGroupId = 3, SortOrder = 5 }, new() { ApplicationId = 1, ApplicationGroupId = 2, SortOrder = 5 }, new() { ApplicationId = 9, ApplicationGroupId = 1 }],
            Parameters = [new() { ApplicationGroupId = 2, ApplicationParameterId = 8, SortOrder = 0 }, new() { ApplicationGroupId = 2, ApplicationParameterId = 7, SortOrder = 0 }, new() { ApplicationGroupId = 9, ApplicationParameterId = 1 }]
        });
        var groups = (IEnumerable<SurveyGroupSetup>)typeof(MultiCropSetup).GetProperty("TypeGroups", Hidden)!.GetValue(page)!;
        var parameters = (IEnumerable<SurveyParameterSetup>)typeof(MultiCropSetup).GetProperty("GroupParameters", Hidden)!.GetValue(page)!;
        Assert.Equal(new[] { 2, 3 }, groups.Select(g => g.ApplicationGroupId));
        Assert.Equal(new[] { 7, 8 }, parameters.Select(p => p.ApplicationParameterId));
    }

    [Fact]
    public async Task AcceptingDiscardClearsCloneState()
    {
        var page = new MultiCropSetup();
        typeof(MultiCropSetup).GetProperty("JS", Hidden)!.SetValue(page, new Confirm(true));
        await Invoke(page, "Clone", new SurveyTypeSetup { ApplicationId = 7 });
        ((SurveyTypeSetup)Field(page, "_draft")!).ApplicationName = "Unsaved";
        await Invoke(page, "ChangeSection", 1);
        Assert.Null(Field(page, "_draft"));
        Assert.Equal(0, Field(page, "_cloneSource"));
        Assert.Equal("parameters", Field(page, "_tab"));
        Assert.Equal(0, Field(page, "_tabsVersion"));
    }

    private static object? Field(MultiCropSetup page, string name) => typeof(MultiCropSetup).GetField(name, Hidden)!.GetValue(page);
    private static bool Dirty(MultiCropSetup page) => (bool)typeof(MultiCropSetup).GetProperty("Dirty", Hidden)!.GetValue(page)!;
    private static Task Invoke(MultiCropSetup page, string name, params object[] args) => (Task)typeof(MultiCropSetup).GetMethod(name, Hidden)!.Invoke(page, args)!;
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/admin/multicrop-setup");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class StaticJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
    private sealed class Confirm(bool answer) : IJSRuntime
    {
        public int Calls;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Assert.Equal("confirm", identifier); Calls++;
            return ValueTask.FromResult((T)(object)answer);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }
}
