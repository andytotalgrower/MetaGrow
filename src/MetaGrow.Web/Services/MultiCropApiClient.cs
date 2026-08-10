using System.Net.Http.Headers;
using System.Text.Json;
using ApiModels;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

/// <summary>Authenticated MetaGrow API access for the multi-crop work area.</summary>
public sealed class MultiCropApiClient(
    IHttpClientFactory clients,
    AuthenticationStateProvider authenticationStateProvider,
    ApiTokenService tokens,
    ILogger<MultiCropApiClient> logger)
{
    public Task<(List<MultiCropSurveySummaryDto>? Value, string? Error)> GetSurveysAsync(
        DateTime startDate,
        DateTime endDate) =>
        GetAsync<List<MultiCropSurveySummaryDto>>(
            $"multicrop/surveys?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");

    public Task<(List<MultiCropSurveyTypeDto>? Value, string? Error)> GetSurveyTypesAsync() =>
        GetAsync<List<MultiCropSurveyTypeDto>>("multicrop/survey-types");

    private async Task<(T? Value, string? Error)> GetAsync<T>(string path)
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var accessToken = await tokens.GetAccessTokenAsync(authenticationState.User);
        if (accessToken is null)
            return (default, "Your session has expired. Please log in again.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await clients.CreateClient(AuthApiClient.HttpClientName).SendAsync(request);

            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<T>(), null);

            return (default, await ReadErrorAsync(response));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not reach MetaGrow.Api while requesting {Path}", path);
            return (default, "MetaGrow.Api could not be reached. Please try again shortly.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("detail", out var detail) &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
                return detail.GetString()!;

            if (document.RootElement.TryGetProperty("title", out var title) &&
                !string.IsNullOrWhiteSpace(title.GetString()))
                return title.GetString()!;
        }
        catch (JsonException)
        {
            // Fall through to a status-based message for non-JSON errors.
        }

        return $"Request failed ({(int)response.StatusCode}).";
    }
}
