using System.Net.Http.Json;
using ApiModels.MetaGrow;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

public sealed class SampleSurveyDeletionApiClient(
    IHttpClientFactory clients,
    AuthenticationStateProvider authenticationState,
    ApiTokenService tokens,
    ILogger<SampleSurveyDeletionApiClient> logger)
{
    private HttpClient Client => clients.CreateClient(AuthApiClient.HttpClientName);

    public Task<(MetaGrowSampleSurveyDeletionDto[]?, string?)> GetPendingAsync() =>
        SendAuthenticated<MetaGrowSampleSurveyDeletionDto[]>(HttpMethod.Get, "sample-survey-deletions/pending");

    public Task<(SampleSurveyDeletionPreviewDto?, string?)> PreviewAsync(
        MetaGrowSurveyType surveyType,
        int surveyId) =>
        SendAuthenticated<SampleSurveyDeletionPreviewDto>(
            HttpMethod.Get,
            $"sample-survey-deletions/preview/{surveyType}/{surveyId}");

    public Task<(MetaGrowSampleSurveyDeletionDto?, string?)> RequestAsync(
        int surveyId,
        bool deleteLinkedLabResults = false) =>
        RequestAsync(MetaGrowSurveyType.Sample, surveyId, deleteLinkedLabResults);

    public Task<(MetaGrowSampleSurveyDeletionDto?, string?)> RequestAsync(
        MetaGrowSurveyType surveyType,
        int surveyId,
        bool deleteLinkedLabResults = false) =>
        SendAuthenticated<MetaGrowSampleSurveyDeletionDto>(
            HttpMethod.Post,
            "sample-survey-deletions",
            new MetaGrowSampleSurveyDeletionCreateRequest
            {
                SurveyType = surveyType,
                SurveyId = surveyId,
                DeleteLinkedLabResults = deleteLinkedLabResults
            });

    public Task<(MetaGrowSampleSurveyDeletionDto?, string?)> ApproveAsync(Guid id, string? note = null) =>
        SendAuthenticated<MetaGrowSampleSurveyDeletionDto>(
            HttpMethod.Post,
            $"sample-survey-deletions/{id}/approve",
            new MetaGrowSampleSurveyDeletionReviewRequest { Note = note });

    public Task<(MetaGrowSampleSurveyDeletionDto?, string?)> RejectAsync(Guid id, string? note = null) =>
        SendAuthenticated<MetaGrowSampleSurveyDeletionDto>(
            HttpMethod.Post,
            $"sample-survey-deletions/{id}/reject",
            new MetaGrowSampleSurveyDeletionReviewRequest { Note = note });

    public Task<(MetaGrowSampleSurveyDeletionDto?, string?)> CancelAsync(Guid id) =>
        SendAuthenticated<MetaGrowSampleSurveyDeletionDto>(
            HttpMethod.Post,
            $"sample-survey-deletions/{id}/cancel");

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
            logger.LogError(exception, "Could not reach MetaGrow.Api for Sample survey deletion request {Path}", path);
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
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim('"');
        }
        catch { }
        return $"Request failed ({(int)response.StatusCode}).";
    }
}
