using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ApiModels.MetaGrow;
using MetaGrow.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MetaGrow.Web.Tests;

public sealed class SessionRenewalTests
{
    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity(
        [new Claim(AuthConstants.SessionClaim, "session")], "Cookies"));
    private static readonly TokenEntry OldToken = new("old-access", DateTime.UtcNow.AddMinutes(-1), "old-refresh");

    [Theory]
    [InlineData("server-error")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("rate-limit")]
    public async Task Temporary_failure_preserves_session_and_next_attempt_recovers(string failure)
    {
        var calls = 0;
        var fixture = Create(_ => ++calls == 1 ? Fail(failure) : Success());
        await fixture.Store.SetAsync("session", OldToken);
        var expired = false;
        fixture.Tokens.SessionExpired += () => { expired = true; return Task.CompletedTask; };

        await Assert.ThrowsAsync<TokenRefreshUnavailableException>(() => fixture.Tokens.GetAccessTokenAsync(User));

        Assert.Equal(OldToken, await fixture.Store.GetAsync("session"));
        Assert.False(expired);
        Assert.Equal("new-access", await fixture.Tokens.GetAccessTokenAsync(User));
        Assert.Equal("new-refresh", (await fixture.Store.GetAsync("session"))!.RefreshToken);
    }

    [Fact]
    public async Task Concurrent_expired_requests_refresh_only_once()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var fixture = Create(async _ =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            await release.Task;
            return await Success();
        });
        await fixture.Store.SetAsync("session", OldToken);
        var first = fixture.Tokens.GetAccessTokenAsync(User);
        await started.Task;
        var second = fixture.Tokens.GetAccessTokenAsync(User);
        release.SetResult();

        Assert.All(await Task.WhenAll(first, second), token => Assert.Equal("new-access", token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Rejected_refresh_expires_session_without_disposing_lock_used_by_waiters()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var fixture = Create(async _ =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        await fixture.Store.SetAsync("session", OldToken);
        var expired = 0;
        fixture.Tokens.SessionExpired += () => { Interlocked.Increment(ref expired); return Task.CompletedTask; };
        var first = fixture.Tokens.GetAccessTokenAsync(User);
        await started.Task;
        var second = fixture.Tokens.GetAccessTokenAsync(User);
        release.SetResult();

        Assert.All(await Task.WhenAll(first, second), token => Assert.Null(token));
        Assert.Null(await fixture.Store.GetAsync("session"));
        Assert.Equal(1, calls);
        Assert.Equal(2, expired);
    }

    [Fact]
    public async Task Missing_credentials_signal_expiration_without_calling_api()
    {
        var fixture = Create(_ => throw new InvalidOperationException("Unexpected API call"));
        var expired = false;
        fixture.Tokens.SessionExpired += () => { expired = true; return Task.CompletedTask; };
        Assert.Null(await fixture.Tokens.GetAccessTokenAsync(User));
        Assert.True(expired);
    }

    [Fact]
    public async Task Queue_reports_temporary_renewal_failure_and_can_be_retried()
    {
        var fail = true;
        var fixture = Create(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/auth/refresh")
                return fail ? Fail("server-error") : Success();
            Assert.Equal("new-access", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(Array.Empty<MetaGrowPropertyDeletionDto>()) });
        });
        await fixture.Store.SetAsync("session", OldToken);
        var client = new PropertyDeletionApiClient(fixture.Factory, new UserState(), fixture.Tokens,
            NullLogger<PropertyDeletionApiClient>.Instance);

        var (failedQueue, error) = await client.GetPendingAsync();
        Assert.Null(failedQueue);
        Assert.Contains("temporarily unavailable", error);
        Assert.NotNull(await fixture.Store.GetAsync("session"));
        fail = false;
        var (queue, retryError) = await client.GetPendingAsync();
        Assert.NotNull(queue);
        Assert.Null(retryError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cookie_validation_rejects_only_sessions_missing_saved_credentials(bool credentialsExist)
    {
        var fixture = Create(_ => throw new InvalidOperationException("Cookie validation must not call API"));
        if (credentialsExist) await fixture.Store.SetAsync("session", OldToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication("Cookies").AddCookie();
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var context = new CookieValidatePrincipalContext(http,
            new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(), new AuthenticationTicket(User, "Cookies"));

        await new SessionCookieEvents(fixture.Store).ValidatePrincipal(context);

        Assert.Equal(credentialsExist, context.Principal is not null);
        Assert.Equal(!credentialsExist, http.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task Store_reads_authoritative_cache_after_another_instance_changes_session()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var protection = new EphemeralDataProtectionProvider();
        var first = new ServerTokenStore(cache, protection);
        var second = new ServerTokenStore(cache, protection);
        await first.SetAsync("session", OldToken);
        Assert.Equal(OldToken, await first.GetAsync("session"));
        var fresh = OldToken with { AccessToken = "updated" };
        await second.SetAsync("session", fresh);
        Assert.Equal(fresh, await first.GetAsync("session"));
        await second.RemoveAsync("session");
        Assert.Null(await first.GetAsync("session"));
    }

    private static Task<HttpResponseMessage> Fail(string failure) => failure switch
    {
        "network" => throw new HttpRequestException("Connection refused"),
        "timeout" => throw new TaskCanceledException("Timed out"),
        "malformed" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }),
        "empty" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }),
        "rate-limit" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)),
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
    };

    private static Task<HttpResponseMessage> Success() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new MetaGrowAuthResponse
        {
            AccessToken = "new-access", AccessTokenExpiresUtc = DateTime.UtcNow.AddMinutes(15), RefreshToken = "new-refresh"
        })
    });

    private static (ServerTokenStore Store, ApiTokenService Tokens, IHttpClientFactory Factory) Create(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var store = new ServerTokenStore(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            new EphemeralDataProtectionProvider());
        var factory = new ClientFactory(new Handler(send));
        var auth = new AuthApiClient(factory, NullLogger<AuthApiClient>.Instance);
        return (store, new ApiTokenService(store, auth), factory);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://metagrow.test/") };
    }

    private sealed class UserState : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
    }
}
