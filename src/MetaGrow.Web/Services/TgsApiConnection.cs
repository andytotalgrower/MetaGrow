using System.Net.Http.Headers;
using System.Text;
using Metagen.Shared.Services;

namespace MetaGrow.Web.Services;

internal sealed record TgsApiConnection(Uri BaseUri, AuthenticationHeaderValue Authorization)
{
    // Share the server-side TgsApi URL and credentials between realtime and SILO reads.
    public static TgsApiConnection Resolve(ISettingsService settings, IConfiguration configuration, IHostEnvironment environment)
    {
        var overrideUrl = Environment.GetEnvironmentVariable("METAGROW_TGS_API_URL");
        var developmentUrl = configuration["TgsApi:DevelopmentBaseUrl"];
        var rootUrl = !string.IsNullOrWhiteSpace(overrideUrl)
            ? overrideUrl
            : environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentUrl)
                ? developmentUrl
                : settings.GetEncryptedSetting("TgsApiUrl");

        if (string.IsNullOrWhiteSpace(rootUrl))
            throw new InvalidOperationException("The TgsApi URL is not configured.");

        var clientGuid = settings.GetEncryptedSetting("TgsApiGuid");
        var email = settings.GetEncryptedSetting("TgsApiEmail");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientGuid}:{email}"));
        var baseUri = new Uri(rootUrl.EndsWith('/') ? rootUrl : rootUrl + "/");
        return new TgsApiConnection(baseUri, new AuthenticationHeaderValue("Basic", credentials));
    }
}
