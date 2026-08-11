using MetaGrow.Web.Components;
using MetaGrow.Web.Services;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Serilog;
using Serilog.Debugging;

const string logDirectory = @"C:\Logs\MetaGrow";
Directory.CreateDirectory(logDirectory);
SelfLog.Enable(message => File.AppendAllText(Path.Combine(logDirectory, "metagrow.serilog.log"), message));
Log.Logger = new LoggerConfiguration().MinimumLevel.Information().Enrich.FromLogContext()
    .WriteTo.File(Path.Combine(logDirectory, "metagrow.log"), rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 10, shared: true).CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDevExpressBlazor(options => options.SizeMode = DevExpress.Blazor.SizeMode.Medium);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAuthorization();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ClientIpForwardingHandler>();
builder.Services.AddHttpClient(AuthApiClient.HttpClientName, client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<ClientIpForwardingHandler>();
builder.Services.AddSingleton<ServerTokenStore>();
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<ApiTokenService>();
builder.Services.AddScoped<AccountApiClient>();
builder.Services.AddScoped<ReportShareApiClient>();
builder.Services.AddScoped<MfaFlowState>();
builder.Services.AddScoped<MetaGrow.Web.Components.Account.IdentityRedirectManager>();

builder.Services.AddScoped<EncryptionService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddHttpClient<ITgsApiService, TgsApiService>(client =>
{
    var developmentBaseUrl = builder.Configuration["TgsApi:DevelopmentBaseUrl"];
    if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentBaseUrl))
        client.BaseAddress = new Uri(developmentBaseUrl);
});

var bootstrapSettings = new SettingsService(builder.Configuration, new EncryptionService());
var cacheConnection = GetConnectionString(bootstrapSettings)
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No MetaGrow token-cache database is configured.");
EnsureTokenCacheTable(cacheConnection);
builder.Services.AddDistributedSqlServerCache(options =>
{
    options.ConnectionString = cacheConnection;
    options.SchemaName = "dbo";
    options.TableName = "TokenCache";
});
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("MetaGrow.Web");

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/r"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    await next();
});
app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapGet("/downloads/surveys/multicrop/{surveyId:int}/pbd", async (int surveyId, ITgsApiService tgsApi) =>
{
    var file = await tgsApi.GetMultiCropPbdWorkbook(surveyId);
    return file == null
        ? Results.Problem(tgsApi.ErrorMessage ?? "The PBD workbook could not be generated.")
        : Results.File(file.Content, file.ContentType, file.FileName);
}).RequireAuthorization();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AllowAnonymous();
app.MapAccountEndpoints();
app.Run();

static string? GetConnectionString(ISettingsService settings)
{
    var template = settings.GetSetting("DbTemplate");
    var server = settings.GetSetting("Server1");
    if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(server)) return null;
    return template.Replace("[SERVER]", settings.GetEncryptedSetting("Server1"))
        .Replace("[DATABASE]", settings.GetSetting("DatabaseName"))
        .Replace("[USER]", settings.GetEncryptedSetting("User1"))
        .Replace("[PASSWORD]", settings.GetEncryptedSetting("Password1"));
}

static void EnsureTokenCacheTable(string connectionString)
{
    const string sql = """
        IF OBJECT_ID(N'dbo.TokenCache', N'U') IS NULL
        CREATE TABLE dbo.TokenCache (
            Id nvarchar(449) COLLATE SQL_Latin1_General_CP1_CS_AS NOT NULL,
            Value varbinary(MAX) NOT NULL,
            ExpiresAtTime datetimeoffset(7) NOT NULL,
            SlidingExpirationInSeconds bigint NULL,
            AbsoluteExpiration datetimeoffset(7) NULL,
            CONSTRAINT PK_TokenCache PRIMARY KEY CLUSTERED (Id),
            INDEX Index_ExpiresAtTime NONCLUSTERED (ExpiresAtTime));
        """;
    using var connection = new SqlConnection(connectionString);
    connection.Open();
    using var command = new SqlCommand(sql, connection);
    command.ExecuteNonQuery();
}

public partial class Program { }
