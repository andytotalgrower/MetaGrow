using ApiModels.Realtime;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace MetaGrow.Web.Services;

public sealed class RealtimeHubListener(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    IRealtimeChangeNotifier notifier,
    ILogger<RealtimeHubListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TgsApiConnection connectionSettings;
            try
            {
                connectionSettings = ResolveConnectionSettings();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The TgsApi realtime connection is not configured correctly; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                continue;
            }

            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(connectionSettings.BaseUri, RealtimeRoutes.Hub), options =>
                {
                    options.Headers["Authorization"] = connectionSettings.Authorization.ToString();
                })
                .WithAutomaticReconnect()
                .Build();

            using var subscription = connection.On<RealtimeEventEnvelope>(
                RealtimeMethods.EventPublished,
                notifier.PublishAsync);

            connection.Reconnected += async _ =>
            {
                await connection.InvokeAsync(RealtimeMethods.SubscribeToSurveys, stoppingToken);
                await notifier.PublishAsync(CreateCatchUpEvent());
            };
            connection.Closed += _ =>
            {
                closed.TrySetResult();
                return Task.CompletedTask;
            };

            try
            {
                await connection.StartAsync(stoppingToken);
                await connection.InvokeAsync(RealtimeMethods.SubscribeToSurveys, stoppingToken);
                await notifier.PublishAsync(CreateCatchUpEvent());
                logger.LogInformation("Connected to the TgsApi realtime survey stream.");
                await closed.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The TgsApi realtime connection is unavailable; retrying.");
            }

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private static RealtimeEventEnvelope CreateCatchUpEvent() => new(
        0,
        RealtimeEventTypes.SurveyChangedV1,
        1,
        RealtimeAggregateTypes.Survey,
        "*",
        RealtimeOperations.Updated,
        "MetaGrow.Realtime",
        Guid.Empty,
        null,
        DateTime.UtcNow);

    private TgsApiConnection ResolveConnectionSettings()
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        return TgsApiConnection.Resolve(settings, configuration, environment);
    }
}
