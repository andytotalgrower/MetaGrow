using System.Net;
using System.Text;
using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Api.Services.SoilReports;
using Metagen.Shared.Services;
using Microsoft.Extensions.Logging;

namespace MetaGrow.Api.Tests;

public sealed class SoilReportSourceClientTests
{
    [Fact]
    public async Task Missing_options_endpoint_is_logged_without_exposing_server_content_to_the_popup()
    {
        // Reproduce an old production API returning 404 while keeping the request entirely in memory.
        using var client = new HttpClient(new ResponseHandler("Internal server diagnostic", HttpStatusCode.NotFound))
            { BaseAddress = new("https://localhost/") };
        var api = new TgsApiService(client, new TestSettings());
        var logger = new RecordingLogger();
        var service = new SoilReportService(null!, api, null!, logger);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Options("test-token"));

        Assert.Contains("Check the API deployment", error.Message);
        Assert.DoesNotContain("Internal server diagnostic", error.Message);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("TgsApi.Core GET /soil-reports/options", entry.Message);
        Assert.Contains(api.ErrorMessage!, entry.Message);
        Assert.DoesNotContain("test-token", entry.Message);
    }

    [Fact]
    public async Task Source_client_preserves_raw_lab_metadata_across_the_api_boundary()
    {
        // Exercise the real shared HTTP client: the calculator fakes cannot catch JSON-library mismatches.
        using var metadata = JsonDocument.Parse("""{"Calcium":1714.0,"CropName":"Banana","Missing":null,"Approved":false}""");
        var source = new SoilReportSource
        {
            Samples = [new() { LabSoilId = 42, Values = new() { ["Calcium"] = 1714, ["Missing"] = null },
                Metadata = metadata.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone()) }]
        };
        var json = JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var client = new HttpClient(new ResponseHandler(json)) { BaseAddress = new("https://localhost/") };
        var api = new TgsApiService(client, new TestSettings());

        var result = await api.GetSoilReportSource(7218, 13113, "test-token");

        Assert.True(result is not null, api.ErrorMessage);
        var sample = Assert.Single(result.Samples);
        Assert.Equal(1714, sample.Values["Calcium"]);
        Assert.Null(sample.Values["Missing"]);
        Assert.Equal(1714, sample.Metadata["Calcium"].GetDouble());
        Assert.Equal("Banana", sample.Metadata["CropName"].GetString());
        Assert.Equal(JsonValueKind.Null, sample.Metadata["Missing"].ValueKind);
        Assert.False(sample.Metadata["Approved"].GetBoolean());
        Assert.Null(api.ErrorMessage);
    }

    // The fake handler never opens a network connection or accesses a database.
    private sealed class ResponseHandler(string _json, HttpStatusCode _status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer test-token", Assert.Single(request.Headers.GetValues("X-MetaGrow-Authorization")));
            return Task.FromResult(new HttpResponseMessage(_status)
                { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class RecordingLogger : ILogger<SoilReportService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class TestSettings : ISettingsService
    {
        public string GetSetting(string key) => "test";
        public string GetEncryptedSetting(string key) => "test";
    }
}
