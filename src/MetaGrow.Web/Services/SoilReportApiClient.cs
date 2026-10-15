using System.Net.Http.Headers;
using ApiModels.MetaGrow;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

/// <summary>All report persistence uses the MetaGrow API and the current user's identity.</summary>
public sealed class SoilReportApiClient(IHttpClientFactory _clients, AuthenticationStateProvider _authentication, ApiTokenService _tokens)
{
    public Task<SoilReportSourceOptions> Options() => Send<SoilReportSourceOptions>(HttpMethod.Get, "options");
    public Task<List<SoilReportListItem>> List(string status) => Send<List<SoilReportListItem>>(HttpMethod.Get, "?status=" + Uri.EscapeDataString(status));
    public Task<SoilReportSettings> Settings(int surveyId) => Send<SoilReportSettings>(HttpMethod.Get, $"{surveyId}/settings");
    public Task<SoilReportPreparation> Preparation(int surveyId) => Send<SoilReportPreparation>(HttpMethod.Get, $"{surveyId}/preparation");
    public Task<SoilReportMatchingReview> Matching(int surveyId, bool tissue = false) => Send<SoilReportMatchingReview>(HttpMethod.Get, $"{surveyId}/{(tissue ? "tissue-matching" : "matching")}");
    public Task<SoilReportSettings> SaveMatching(int surveyId, SoilReportMatchingChange change, bool tissue = false) => Send<SoilReportSettings>(HttpMethod.Put, $"{surveyId}/{(tissue ? "tissue-matching" : "matching")}", change);
    public Task<SoilReportSettings> SaveNotSampled(int surveyId, SoilReportNotSampledChange change) => Send<SoilReportSettings>(HttpMethod.Put, $"{surveyId}/not-sampled", change);
    public Task<SoilReportSettings> SaveSettings(SoilReportSettings settings) => Send<SoilReportSettings>(HttpMethod.Put, $"{settings.SurveyId}/settings", settings);
    public Task<SoilReportWorkspace> Open(int surveyId) => Send<SoilReportWorkspace>(HttpMethod.Post, $"{surveyId}/open");
    public Task<SoilReportWorkspace> Save(int surveyId, SoilReportEdit edit) => Send<SoilReportWorkspace>(HttpMethod.Put, $"{surveyId}", edit);
    public Task<SoilReportWorkspace> Transition(int surveyId, SoilReportTransition request) => Send<SoilReportWorkspace>(HttpMethod.Post, $"{surveyId}/workflow", request);
    public Task<List<SoilReportAuditEntry>> History(int surveyId) => Send<List<SoilReportAuditEntry>>(HttpMethod.Get, $"{surveyId}/history");
    public Task<List<SoilReportRevisionSummary>> Revisions(int surveyId) => Send<List<SoilReportRevisionSummary>>(HttpMethod.Get, $"{surveyId}/revisions");
    public Task<SoilReportDocument> Revision(int surveyId, Guid id) => Send<SoilReportDocument>(HttpMethod.Get, $"{surveyId}/revisions/{id}");

    private async Task<T> Send<T>(HttpMethod method, string path, object? body = null)
    {
        var token = await _tokens.GetAccessTokenAsync((await _authentication.GetAuthenticationStateAsync()).User)
            ?? throw new InvalidOperationException("Your session has expired. Please sign in again.");
        using var request = new HttpRequestMessage(method, "soil-reports/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var client = _clients.CreateClient(AuthApiClient.HttpClientName);
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict
                && response.Content.Headers.ContentType?.MediaType == "text/plain"
                ? await response.Content.ReadAsStringAsync() : "The soil report request could not be completed. Refresh and try again.";
            throw new InvalidOperationException(message);
        }
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new InvalidOperationException("No soil report data was returned.");
    }
}
