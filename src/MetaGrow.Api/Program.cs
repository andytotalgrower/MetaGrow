using System.Text;
using System.Threading.RateLimiting;
using MetaGrow.Api.Auth;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File(@"C:\Logs\MetaGrow\metagrow-api-.log", rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31, shared: true));

var bootstrapSettings = new SettingsService(builder.Configuration, new EncryptionService());
var connectionString = GetConnectionString(bootstrapSettings)
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No MetaGrow database connection is configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 64)
    throw new InvalidOperationException("Jwt:SigningKey must contain at least 64 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<ITokenService, TokenService>();

var graphMail = builder.Configuration.GetSection(GraphMailOptions.SectionName).Get<GraphMailOptions>() ?? new();
DecryptGraphMailOptions(graphMail);
builder.Services.AddSingleton(graphMail);
builder.Services.AddSingleton<IGraphMailService>(_ => new GraphMailService(new HttpClient(), graphMail));
builder.Services.AddSingleton<MailQueue>();
builder.Services.AddHostedService<MailDispatcher>();

builder.Services.AddScoped<EncryptionService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<ITgsApiService, TgsApiService>(client =>
{
    var developmentBaseUrl = builder.Configuration["TgsApi:DevelopmentBaseUrl"];
    if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(developmentBaseUrl))
        client.BaseAddress = new Uri(developmentBaseUrl);
});
builder.Services.AddHttpContextAccessor();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permits = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 10);
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1), PermitLimit = permits, QueueLimit = 0
        }));
});

var webOrigin = builder.Configuration["Cors:WebOrigin"];
if (!string.IsNullOrWhiteSpace(webOrigin))
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();
await DbSeeder.InitialiseAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "MetaGrow API"));
}
else app.UseHsts();

app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });
app.UseHttpsRedirection();
app.UseRateLimiter();
if (!string.IsNullOrWhiteSpace(webOrigin)) app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

static string? GetConnectionString(ISettingsService settings)
{
    var template = settings.GetSetting("DbTemplate");
    var server = settings.GetSetting("Server1");
    if (string.IsNullOrWhiteSpace(template) || string.IsNullOrWhiteSpace(server)) return null;

    return template
        .Replace("[SERVER]", settings.GetEncryptedSetting("Server1"))
        .Replace("[DATABASE]", settings.GetSetting("DatabaseName"))
        .Replace("[USER]", settings.GetEncryptedSetting("User1"))
        .Replace("[PASSWORD]", settings.GetEncryptedSetting("Password1"));
}

static void DecryptGraphMailOptions(GraphMailOptions options)
{
    var encryption = new EncryptionService { KI = 5 };
    options.ApplicationClientId = Decrypt(options.ApplicationClientId);
    options.ObjectId = Decrypt(options.ObjectId);
    options.DirectoryTenantId = Decrypt(options.DirectoryTenantId);
    options.SecretId = Decrypt(options.SecretId);
    options.SecretValue = Decrypt(options.SecretValue);
    return;

    string Decrypt(string value) => string.IsNullOrWhiteSpace(value) ? value : encryption.Decrypt(value);
}

public partial class Program { }
