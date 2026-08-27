using System.Net.Http.Headers;
using System.Text;
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
            ConnectionSettings connectionSettings;
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
                .WithUrl(connectionSettings.HubUrl, options =>
                {
                    options.Headers["Authorization"] = connectionSettings.AuthorizationHeader;
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

    private ConnectionSettings ResolveConnectionSettings()
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var overrideUrl = Environment.GetEnvironmentVariable("METAGROW_TGS_API_URL");
        var developmentUrl = configuration["TgsApi:DevelopmentBaseUrl"];
        var rootUrl = !string.IsNullOrWhiteSpace(overrideUrl)
            ? overrideUrl
            : environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentUrl)
                ? developmentUrl
                : settings.GetEncryptedSetting("TgsApiUrl");

        if (string.IsNullOrWhiteSpace(rootUrl))
            throw new InvalidOperationException("The TgsApi URL is not configured for realtime updates.");

        var clientGuid = settings.GetEncryptedSetting("TgsApiGuid");
        var email = settings.GetEncryptedSetting("TgsApiEmail");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientGuid}:{email}"));
        var authorization = new AuthenticationHeaderValue("Basic", credentials).ToString();
        var baseUri = new Uri(rootUrl.EndsWith('/') ? rootUrl : rootUrl + "/");

        return new ConnectionSettings(new Uri(baseUri, RealtimeRoutes.Hub), authorization);
    }

    private sealed record ConnectionSettings(Uri HubUrl, string AuthorizationHeader);
}
