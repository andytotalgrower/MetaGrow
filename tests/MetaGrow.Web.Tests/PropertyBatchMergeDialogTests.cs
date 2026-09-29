using System.Reflection;
using DevExpress.Blazor;
using MetaGrow.Web.Components.Shared;
using MetaGrow.Web.Services;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace MetaGrow.Web.Tests;

public class PropertyBatchMergeDialogTests
{
    [Fact]
    public async Task Popup_renders_fixed_destination_and_distinct_suggestions()
    {
        var html = await RenderAsync();
        Assert.Contains("Farm to keep", html);
        Assert.Contains("Greensill Qunaba", html);
        Assert.Contains("Showing 1 of 1 farms", html);
        Assert.Contains("Review selected farms (0)", html);
        Assert.Contains("Search all farms by name or ID", html);
        Assert.DoesNotContain("batch-outcome", html);
    }

    [Fact]
    public async Task Completed_batch_confirms_count_and_destination()
    {
        var html = await RenderAsync(BatchMergeState.Completed);
        Assert.Contains("Merge completed successfully", html);
        Assert.Contains("2 farms were merged into <strong>Greensill Qunaba</strong>", html);
        Assert.Contains("You can now close this window.", html);
        Assert.DoesNotContain("merges into <strong>Greensill Qunaba</strong> are confirmed complete", html);
    }

    [Theory]
    [InlineData(BatchMergeState.NeedsReview, "Merge stopped — attention required", "Review stopped merge")]
    [InlineData(BatchMergeState.VerifyResult, "Merge stopped — result needs checking", "Check result")]
    [InlineData(BatchMergeState.Ready, "Ready to continue merging", "Use the merge button below to continue")]
    public async Task Incomplete_batch_reports_confirmed_count_and_next_action(BatchMergeState state, string title, string action)
    {
        var html = System.Net.WebUtility.HtmlDecode(await RenderAsync(state));
        Assert.Contains(title, html);
        Assert.Contains("<strong>1 of 2</strong> merges", html);
        Assert.Contains("1 farm still needs attention", html);
        Assert.Contains("Completed merges have been kept.", html);
        Assert.Contains(action, html);
        Assert.DoesNotContain("Merge completed successfully", html);
        if (state != BatchMergeState.Ready)
            Assert.Contains("<strong>Variant two:</strong> Check this farm before continuing.", html);
    }

    [Fact]
    public async Task Running_batch_does_not_show_final_outcome()
    {
        var html = await RenderAsync(BatchMergeState.Completed, busy: true);
        Assert.DoesNotContain("batch-outcome", html);
        Assert.DoesNotContain("Merge completed successfully", html);
    }

    private static async Task<string> RenderAsync(BatchMergeState? finalState = null, bool busy = false)
    {
        var collection = new ServiceCollection().AddLogging();
        collection.AddDevExpressBlazor();
        collection.AddSingleton<IJSRuntime, StaticJs>();
        collection.AddSingleton<NavigationManager, TestNavigation>();
        collection.AddSingleton<ITgsApiService>(DispatchProxy.Create<ITgsApiService, MultiCropSetupTests.UnusedApi>());
        collection.AddSingleton(new PropertyMergeApiClient(null!, null!, null!, null!));
        await using var services = collection.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<OpenDialog>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["IsReviewer"] = true,
                ["FinalState"] = finalState,
                ["Busy"] = busy,
                ["Properties"] = new ApiModels.Property[]
                {
                    new() { PropertyId = 10, PropertyName = "Greensill Qunaba" },
                    new() { PropertyId = 1, PropertyName = "Greenhills Qunaba" },
                    new() { PropertyId = 1, PropertyName = "Greenhills Qunaba" }
                }
            }));
            return rendered.ToHtmlString();
        });
    }

    public class OpenDialog : PropertyBatchMergeDialog
    {
        [Parameter] public BatchMergeState? FinalState { get; set; }
        [Parameter] public bool Busy { get; set; }

        // DxPopup mounts its body through JavaScript. Render the same fragment directly here.
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddContent(0, BatchContent);

        protected override void OnInitialized()
        {
            Set("_target", new ApiModels.Property { PropertyId = 10, PropertyName = "Greensill Qunaba" });
            var session = new PropertyMergeBatchSession(10, null!);
            if (FinalState is { } state)
            {
                session.Select(1, "Variant one", true);
                session.Select(2, "Variant two", true);
                typeof(BatchMergeItem).GetProperty(nameof(BatchMergeItem.State))!.SetValue(session.Items[0], BatchMergeState.Completed);
                typeof(BatchMergeItem).GetProperty(nameof(BatchMergeItem.State))!.SetValue(session.Items[1], state);
                typeof(BatchMergeItem).GetProperty(nameof(BatchMergeItem.Message))!.SetValue(session.Items[1], "Check this farm before continuing.");
            }
            Set("_session", session);
            Set("_hasRun", FinalState.HasValue);
            Set("_busy", Busy);
            Set("_suggested", new HashSet<int> { 1, 10 });
            Set("_visible", true);
        }
        private void Set(string field, object value) => typeof(PropertyBatchMergeDialog)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, value);
    }

    private sealed class StaticJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/farms/duplicates");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
