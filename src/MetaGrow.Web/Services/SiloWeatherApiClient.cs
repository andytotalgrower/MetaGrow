using ApiModels;
using Metagen.Shared.Services;

namespace MetaGrow.Web.Services;

public sealed class SiloWeatherApiClient(
    HttpClient httpClient,
    ISettingsService settings,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    public Task<List<SiloCollectionPointDto>> GetRegionsAsync(CancellationToken cancellationToken) =>
        ReadAsync<List<SiloCollectionPointDto>>("silo/points", cancellationToken);

    // One calendar read populates the picker and lets crop changes reuse the loaded schedules.
    public Task<List<SiloCropSeasonDto>> GetCropSeasonsAsync(int recipientId, CancellationToken cancellationToken) =>
        ReadAsync<List<SiloCropSeasonDto>>(FormattableString.Invariant($"silo/crop-seasons?recipientId={recipientId}"),
            cancellationToken);

    // Yield is already mapped to the collection point and can be reused across crop and date changes.
    public Task<List<SiloCropYieldDto>> GetCropYieldsAsync(int recipientId, CancellationToken cancellationToken) =>
        ReadAsync<List<SiloCropYieldDto>>(FormattableString.Invariant($"silo/crop-yields?recipientId={recipientId}"),
            cancellationToken);

    // Always send the recipient ID. Omitting it would request all grid cells.
    public Task<List<SiloDailyWeatherDto>> GetHistoryAsync(int recipientId, DateOnly throughDate,
        CancellationToken cancellationToken) =>
        ReadAsync<List<SiloDailyWeatherDto>>(FormattableString.Invariant(
            $"silo/weather?recipientId={recipientId}&startDate=1889-01-01&endDate={throughDate:yyyy-MM-dd}"),
            cancellationToken);

    private async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        var connection = TgsApiConnection.Resolve(settings, configuration, environment);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(connection.BaseUri, path));
        request.Headers.Authorization = connection.Authorization;
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidDataException("TgsApi returned an empty SILO response.");
    }
}
