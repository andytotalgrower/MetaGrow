using System.Security.Claims;
using ApiModels.MetaGrow;
using MetaGrow.Api.Controllers;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Tests;

public sealed class ReportSharesControllerTests
{
    [Fact]
    public async Task Permanent_share_can_be_resolved_counted_and_revoked()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options);
        await database.Database.EnsureCreatedAsync();

        var tokenService = new ReportShareTokenService(new EphemeralDataProtectionProvider());
        var controller = new ReportSharesController(database, tokenService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "user-1"),
                        new Claim(ClaimTypes.Email, "agronomist@example.com")
                    ], "Test"))
                }
            }
        };

        var createdResult = await controller.Create(new MetaGrowReportShareCreateRequest
        {
            SurveyId = 57646,
            ReportArea = MetaGrowReportAreas.Banana,
            Name = "Farm hands"
        });
        var created = Assert.IsType<MetaGrowReportShareDto>(
            Assert.IsType<OkObjectResult>(createdResult.Result).Value);
        Assert.NotNull(created.Token);
        Assert.Null(created.RevokedUtc);
        Assert.Equal(MetaGrowReportAreas.Banana, created.ReportArea);

        var resolvedResult = await controller.Resolve(created.Id,
            new MetaGrowReportShareResolveRequest { Token = created.Token });
        var resolved = Assert.IsType<MetaGrowReportShareResolveResponse>(
            Assert.IsType<OkObjectResult>(resolvedResult.Result).Value);
        Assert.Equal(57646, resolved.SurveyId);
        Assert.Equal(MetaGrowReportAreas.Banana, resolved.ReportArea);

        database.ChangeTracker.Clear();
        var persisted = await database.ReportShares.SingleAsync();
        Assert.Equal(1, persisted.ViewCount);
        Assert.NotNull(persisted.LastViewedUtc);

        Assert.IsType<NoContentResult>(await controller.Revoke(created.Id));
        var revokedResult = await controller.Resolve(created.Id,
            new MetaGrowReportShareResolveRequest { Token = created.Token });
        Assert.IsType<NotFoundResult>(revokedResult.Result);
    }

    [Fact]
    public async Task Survey_ids_are_scoped_by_report_area()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.ReportShares.AddRange(
            Share(MetaGrowReportAreas.MultiCrop),
            Share(MetaGrowReportAreas.Banana));
        await database.SaveChangesAsync();

        var controller = new ReportSharesController(
            database,
            new ReportShareTokenService(new EphemeralDataProtectionProvider()));

        var result = await controller.GetForSurvey(42, MetaGrowReportAreas.Banana);
        var shares = Assert.IsType<MetaGrowReportShareDto[]>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        var share = Assert.Single(shares);
        Assert.Equal(MetaGrowReportAreas.Banana, share.ReportArea);
    }

    private static ReportShare Share(string reportArea) => new()
    {
        Id = Guid.NewGuid(),
        SurveyId = 42,
        ReportArea = reportArea,
        Name = reportArea,
        TokenHash = Guid.NewGuid().ToString("N"),
        ProtectedToken = "protected",
        CreatedByUserId = "user-1",
        CreatedByEmail = "agronomist@example.com",
        CreatedUtc = DateTime.UtcNow
    };
}
