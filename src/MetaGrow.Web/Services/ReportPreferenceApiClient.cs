using System.Net.Http.Headers;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

public interface IReportPreferenceApiClient
{
    Task<string> GetAsync();
    Task SaveAsync(string brandKey);
}

/// <summary>Loads preferences for each report so changes in other tabs are picked up.</summary>
public sealed class ReportPreferenceApiClient(
    IHttpClientFactory _clients, AuthenticationStateProvider _authenticationState, ApiTokenService _tokens) : IReportPreferenceApiClient
{
    public async Task<string> GetAsync()
    {
        using var response = await SendAsync(HttpMethod.Get);
        var preference = await response.Content.ReadFromJsonAsync<ReportBrandPreference>();
        return ReportBrandPreference.Normalize(preference?.BrandKey);
    }

    public async Task SaveAsync(string brandKey)
    {
        using var response = await SendAsync(HttpMethod.Put, new ReportBrandPreference { BrandKey = brandKey });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, ReportBrandPreference? preference = null)
    {
        var principal = (await _authenticationState.GetAuthenticationStateAsync()).User;
        var token = await _tokens.GetAccessTokenAsync(principal);
        if (token is null) throw new InvalidOperationException("Your session has expired. Please log in again.");
        using var request = new HttpRequestMessage(method, "report-preferences");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (preference is not null) request.Content = JsonContent.Create(preference);
        using var client = _clients.CreateClient(AuthApiClient.HttpClientName);
        var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        response.Dispose();
        // Do not silently print the wrong brand when the saved preference cannot be read.
        throw new InvalidOperationException("Report branding could not be loaded or saved. Please try again.");
    }
}
