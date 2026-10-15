using System.Net.Http.Headers;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

/// <summary>Uses the signed-in MetaGrow identity; maintenance never calls the operational database.</summary>
public sealed class SoilReportDefinitionApiClient(
    IHttpClientFactory _clients, AuthenticationStateProvider _authentication, ApiTokenService _tokens)
{
    public Task<List<SoilReportDefinition>> GetAsync() => SendAsync<List<SoilReportDefinition>>(HttpMethod.Get, "");
    public Task<List<SoilDefinitionField>> FieldsAsync() => SendAsync<List<SoilDefinitionField>>(HttpMethod.Get, "/fields");
    public Task<List<SoilDefinitionChange>> HistoryAsync(string key) => SendAsync<List<SoilDefinitionChange>>(HttpMethod.Get, $"/{Uri.EscapeDataString(key)}/history");
    public Task<SoilReportDefinition> SaveAsync(SoilReportDefinition definition, string reason) =>
        SendAsync<SoilReportDefinition>(HttpMethod.Put, $"/{Uri.EscapeDataString(definition.Key)}", new SaveSoilDefinition(definition, reason));

    private async Task<T> SendAsync<T>(HttpMethod method, string suffix, SaveSoilDefinition? body = null)
    {
        var token = await _tokens.GetAccessTokenAsync((await _authentication.GetAuthenticationStateAsync()).User)
            ?? throw new InvalidOperationException("Your session has expired. Please sign in again.");
        using var request = new HttpRequestMessage(method, "soil-report-definitions" + suffix);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var client = _clients.CreateClient(AuthApiClient.HttpClientName);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            // Only expose deliberate validation/conflict messages, never server exception details.
            var message = response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict
                && response.Content.Headers.ContentType?.MediaType == "text/plain"
                ? await response.Content.ReadAsStringAsync() : "Report definitions could not be loaded or saved. Please refresh and try again.";
            throw new InvalidOperationException(message);
        }
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("No report definition data was returned.");
    }
}
