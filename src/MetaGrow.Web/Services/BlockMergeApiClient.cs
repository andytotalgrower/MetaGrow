using System.Net.Http.Json;
using ApiModels;
using ApiModels.MetaGrow;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

public sealed class BlockMergeApiClient(IHttpClientFactory clients, AuthenticationStateProvider authenticationState, ApiTokenService tokens)
{
    public Task<(MultiCropBlockMergePreview?, string?)> Preview(MultiCropBlockMergePlan plan) =>
        Send<MultiCropBlockMergePreview>(HttpMethod.Post, "block-merges/preview", plan);

    public Task<(MultiCropBlockMergeStatus?, string?)> Execute(MultiCropBlockMergeExecution execution) =>
        Send<MultiCropBlockMergeStatus>(HttpMethod.Post, "block-merges/execute", execution);

    public Task<(MultiCropBlockMergeStatus?, string?)> Status(Guid requestId) =>
        Send<MultiCropBlockMergeStatus>(HttpMethod.Get, $"block-merges/{requestId}/status");

    private async Task<(T?, string?)> Send<T>(HttpMethod method, string path, object? body = null)
    {
        try
        {
            var user = (await authenticationState.GetAuthenticationStateAsync()).User;
            var token = await tokens.GetAccessTokenAsync(user);
            if (token is null) return (default, "Your session has expired. Please log in again.");
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await clients.CreateClient(AuthApiClient.HttpClientName).SendAsync(request);
            if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<T>(), null);
            var error = await response.Content.ReadFromJsonAsync<MetaGrowAuthError>();
            return (default, error is { Errors.Length: > 0 } ? string.Join(" ", error.Errors) : "Block merging is unavailable.");
        }
        catch (TokenRefreshUnavailableException exception) { return (default, exception.Message); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (default, "The block merge service could not be reached. If you submitted a merge, check its status before retrying.");
        }
    }
}
