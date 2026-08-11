using System.Net.Http.Json;
using ApiModels.MetaGrow;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

public sealed class ReportShareApiClient(
    IHttpClientFactory clients,
    AuthenticationStateProvider authenticationState,
    ApiTokenService tokens,
    ILogger<ReportShareApiClient> logger)
{
    private HttpClient Client => clients.CreateClient(AuthApiClient.HttpClientName);

    public Task<(MetaGrowReportShareDto[]?, string?)> GetForSurveyAsync(int surveyId) =>
        SendAuthenticated<MetaGrowReportShareDto[]>(HttpMethod.Get, $"report-shares/survey/{surveyId}");

    public Task<(MetaGrowReportShareDto?, string?)> CreateAsync(int surveyId, string? name) =>
        SendAuthenticated<MetaGrowReportShareDto>(HttpMethod.Post, "report-shares",
            new MetaGrowReportShareCreateRequest { SurveyId = surveyId, Name = name });

    public async Task<string?> RevokeAsync(Guid id)
    {
        var (_, error) = await SendAuthenticated<object>(HttpMethod.Post, $"report-shares/{id}/revoke", new { });
        return error;
    }

    public async Task<(MetaGrowReportShareResolveResponse?, string?)> ResolveAsync(Guid id, string token)
    {
        try
        {
            var response = await Client.PostAsJsonAsync($"report-shares/{id}/resolve",
                new MetaGrowReportShareResolveRequest { Token = token });
            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<MetaGrowReportShareResolveResponse>(), null);

            return (null, response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "This report link is invalid or has been revoked."
                : await ReadError(response));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not validate a shared report link with MetaGrow.Api.");
            return (null, "The shared report service could not be reached. Please try again shortly.");
        }
    }

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
            if (typeof(T) == typeof(object)) return (default, null);
            return (await response.Content.ReadFromJsonAsync<T>(), null);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not reach MetaGrow.Api for report-share request {Path}.", path);
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
