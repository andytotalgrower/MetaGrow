using MetaGrow.Web.Services;
using ApiModels.Realtime;

namespace MetaGrow.Web.Tests;

public sealed class RealtimeChangeNotifierTests
{
    [Fact]
    public async Task PublishAsync_NotifiesEveryCurrentSubscriber()
    {
        var notifier = new RealtimeChangeNotifier();
        var firstCount = 0;
        var secondCount = 0;
        using var first = notifier.Subscribe(_ =>
        {
            firstCount++;
            return Task.CompletedTask;
        });
        using var second = notifier.Subscribe(_ =>
        {
            secondCount++;
            return Task.CompletedTask;
        });

        await notifier.PublishAsync(CreateMessage());

        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public async Task DisposedSubscription_IsNotNotified()
    {
        var notifier = new RealtimeChangeNotifier();
        var count = 0;
        var subscription = notifier.Subscribe(_ =>
        {
            count++;
            return Task.CompletedTask;
        });
        subscription.Dispose();

        await notifier.PublishAsync(CreateMessage());

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("banana:42", RealtimeSurveyTypes.Banana, true)]
    [InlineData("multicrop:42", RealtimeSurveyTypes.MultiCrop, true)]
    [InlineData("sample:42", RealtimeSurveyTypes.Sample, true)]
    [InlineData("sample:42", RealtimeSurveyTypes.Banana, false)]
    public void IsSurveyChangeFor_FiltersBySurveyType(
        string aggregateId,
        string surveyType,
        bool expected)
    {
        var message = CreateMessage() with { AggregateId = aggregateId };

        Assert.Equal(expected, RealtimeEventFilters.IsSurveyChangeFor(message, surveyType));
    }

    [Fact]
    public void IsSurveyChangeFor_AcceptsCatchUpEventForEverySurveyType()
    {
        var message = CreateMessage() with { EventId = 0, AggregateId = "*" };

        Assert.True(RealtimeEventFilters.IsSurveyChangeFor(message, RealtimeSurveyTypes.Banana));
        Assert.True(RealtimeEventFilters.IsSurveyChangeFor(message, RealtimeSurveyTypes.MultiCrop));
        Assert.True(RealtimeEventFilters.IsSurveyChangeFor(message, RealtimeSurveyTypes.Sample));
    }

    private static RealtimeEventEnvelope CreateMessage() => new(
        1,
        RealtimeEventTypes.SurveyChangedV1,
        1,
        RealtimeAggregateTypes.Survey,
        "multicrop:42",
        RealtimeOperations.Updated,
        "MetaGrow",
        Guid.NewGuid(),
        "{\"surveyType\":\"multicrop\",\"surveyId\":42}",
        DateTime.UtcNow);
}
