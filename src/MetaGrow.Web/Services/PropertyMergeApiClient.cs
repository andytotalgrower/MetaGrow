using System.Net.Http.Json;
using ApiModels;
using ApiModels.MetaGrow;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

public sealed class PropertyMergeApiClient(
    IHttpClientFactory clients,
    AuthenticationStateProvider authenticationState,
    ApiTokenService tokens,
    ILogger<PropertyMergeApiClient> logger)
{
    private HttpClient Client => clients.CreateClient(AuthApiClient.HttpClientName);

    public Task<(MetaGrowPropertyMergeRequestDto[]?, string?)> GetPendingAsync() =>
        SendAuthenticated<MetaGrowPropertyMergeRequestDto[]>(HttpMethod.Get, "property-merges/pending");

    public Task<(MetaGrowPropertyMergeRequestDto?, string?)> RequestAsync(PropertyMergePreviewRequest plan) =>
        SendAuthenticated<MetaGrowPropertyMergeRequestDto>(
            HttpMethod.Post,
            "property-merges",
            new MetaGrowPropertyMergeCreateRequest { Plan = plan });

    public Task<(MetaGrowPropertyMergeRequestDto?, string?)> RejectAsync(Guid id, string? note = null) =>
        SendAuthenticated<MetaGrowPropertyMergeRequestDto>(
            HttpMethod.Post,
            $"property-merges/{id}/reject",
            new MetaGrowPropertyMergeReviewRequest { Note = note });

    private async Task<(T?, string?)> SendAuthenticated<T>(HttpMethod method, string path, object? body = null)
    {
        var principal = (await authenticationState.GetAuthenticationStateAsync()).User;
        var accessToken = await tokens.GetAccessTokenAsync(principal);
        if (accessToken is null) return (default, "Your session has expired. Please log in again.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body);

        try
        {
            using var response = await Client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (default, await ReadError(response));
            return (await response.Content.ReadFromJsonAsync<T>(), null);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not reach MetaGrow.Api for property merge request {Path}", path);
            return (default, "MetaGrow.Api could not be reached. Please try again shortly.");
        }
    }

    private static async Task<string> ReadError(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MetaGrowAuthError>();
            if (error is { Errors.Length: > 0 }) return string.Join(" ", error.Errors);
        }
        catch { }

        return $"Request failed ({(int)response.StatusCode}).";
    }
}
