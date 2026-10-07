using System.Reflection;
using ApiModels;
using MetaGrow.Web.Components.Pages;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MetaGrow.Web.Tests;

public sealed class SampleSurveyDateSearchTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task Rapid_typing_searches_only_the_last_complete_range_and_blur_does_not_repeat_it()
    {
        var (page, api) = CreatePage();
        using (page)
        {
            var first = Input(page, "01/09/2026");
            Assert.Empty(api.Calls);
            var second = Input(page, "02/09/2026");
            await Task.WhenAll(first, second);
            Assert.Equal((new DateTime(2026, 9, 2), new DateTime(2026, 10, 7)), Assert.Single(api.Calls));
            Set(page, "_startDate", new DateTime(2026, 9, 2));
            await Call(page, "DateChangedAsync", true);
            Assert.Single(api.Calls);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("01/09/202")]
    [InlineData("31/09/2026")]
    [InlineData("10/08/2026", "09/08/2026")]
    public async Task Incomplete_invalid_or_reversed_dates_cancel_pending_search(string start, string end = "07/10/2026")
    {
        var (page, api) = CreatePage();
        using (page)
        {
            var pending = Input(page, "01/09/2026");
            Set(page, "_endDateInput", end);
            await Input(page, start);
            await pending;
            Assert.Empty(api.Calls);
            Assert.NotNull(Get(page, "_dateValidationMessage"));
        }
    }

    [Fact]
    public async Task Calendar_selection_searches_and_disposal_cancels_pending_search()
    {
        var (page, api) = CreatePage();
        Set(page, "_startDate", new DateTime(2026, 9, 5));
        await Call(page, "DateChangedAsync", true);
        Assert.Equal(new DateTime(2026, 9, 5), Assert.Single(api.Calls).Start);
        var pending = Input(page, "06/09/2026");
        page.Dispose();
        await pending;
        Assert.Single(api.Calls);
    }

    [Fact]
    public async Task Returning_to_loaded_dates_restores_applied_range_without_another_query()
    {
        var (page, api) = CreatePage();
        using (page)
        {
            await Input(page, "01/09/2026");
            // A superseded request may have applied a different range before it completed.
            Set(page, "_appliedStartDate", new DateTime(2026, 9, 2));
            await Input(page, "01/09/2026");
            Assert.Single(api.Calls);
            Assert.Equal(new DateTime(2026, 9, 1), Get(page, "_appliedStartDate"));
        }
    }

    [Fact]
    public async Task Superseded_response_cannot_replace_results_even_when_the_new_date_is_incomplete()
    {
        var (page, api) = CreatePage();
        using (page)
        {
            var oldResults = new List<SampleSurveySummaryDto>();
            Set(page, "_surveys", oldResults);
            api.Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Input(page, "01/09/2026");
            await api.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Input(page, "02/09/202");
            api.Response.SetResult([new() { SurveyId = 123, NutritionSampleCount = 1 }]);
            await pending;
            Assert.Same(oldResults, Get(page, "_surveys"));
        }
    }

    private static (Samples Page, SearchApi Api) CreatePage()
    {
        var page = new Samples();
        var api = DispatchProxy.Create<ITgsApiService, SearchApi>();
        typeof(Samples).GetProperty("TgsApi", Hidden)!.SetValue(page, api);
        typeof(Samples).GetProperty("JS", Hidden)!.SetValue(page, new NoopJs());
        Set(page, "_startDate", new DateTime(2026, 7, 7));
        Set(page, "_endDate", new DateTime(2026, 10, 7));
        return (page, (SearchApi)api);
    }

    private static Task Input(Samples page, string value) => Call(page, "DateInputAsync", new ChangeEventArgs { Value = value }, true);
    private static Task Call(Samples page, string method, params object[] args) => (Task)typeof(Samples).GetMethod(method, Hidden)!.Invoke(page, args)!;
    private static void Set(Samples page, string field, object value) => typeof(Samples).GetField(field, Hidden)!.SetValue(page, value);
    private static object? Get(Samples page, string field) => typeof(Samples).GetField(field, Hidden)!.GetValue(page);

    public class SearchApi : DispatchProxy
    {
        public List<(DateTime Start, DateTime End)> Calls { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<List<SampleSurveySummaryDto>?>? Response { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "GetSampleSurveySummaries") throw new NotSupportedException(method.Name);
            Calls.Add(((DateTime)args![0]!, (DateTime)args[1]!));
            Started.TrySetResult();
            return Response?.Task ?? Task.FromResult<List<SampleSurveySummaryDto>?>([]);
        }
    }

    private sealed class NoopJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
