using MetaGrow.Shared.Models;
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
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Soil_links_require_approval_and_keep_the_original_revision_after_later_edits(bool soil, bool tissue)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await database.Database.EnsureCreatedAsync();
        var revisionId = Guid.NewGuid();
        var document = new SoilReportDocument { Id = revisionId, SurveyId = 44, Revision = 1, PropertyName = "Frozen farm", Trace = [new() { Rule = "internal" }], DefinitionSnapshot = [new() { Key = "internal" }],
            Options = new() { IncludeSoil = soil, IncludeTissue = tissue },
            SampleMatches = [new(1, 2, "North", "manual", "Internal confirmation", "reviewer", "reviewer@example.test")],
            Tissue = new() { Samples = [new() { LabTissueId = 1, BlockName = "North", Values = new() { ["NitrogenPc"] = new() { Value = 3.6 } } }],
                Matches = [new(1, 3, "North", "manual", "Internal tissue confirmation", "reviewer", "reviewer@example.test")] } };
        var state = new SoilReportState { SurveyId = 44, Version = 1, CurrentRevisionId = revisionId };
        database.SoilReportStates.Add(state);
        database.SoilReportRevisions.Add(new() { Id = revisionId, SurveyId = 44, Number = 1, DocumentJson = System.Text.Json.JsonSerializer.Serialize(document) });
        await database.SaveChangesAsync();
        var controller = new ReportSharesController(database, new ReportShareTokenService(new EphemeralDataProtectionProvider()))
        { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "reviewer"), new Claim(ClaimTypes.Email, "reviewer@example.test")], "Test")) } } };
        var request = new MetaGrowReportShareCreateRequest { SurveyId = 44, ReportArea = MetaGrowReportAreas.Soil, SoilRevisionId = revisionId };
        Assert.IsType<BadRequestObjectResult>((await controller.Create(request)).Result);
        state.Status = "Approved";
        database.SoilReportAudits.Add(new() { SurveyId = 44, RevisionId = revisionId, Action = "approve", Status = "Approved" });
        await database.SaveChangesAsync();
        var share = Assert.IsType<MetaGrowReportShareDto>(Assert.IsType<OkObjectResult>((await controller.Create(request)).Result).Value);
        state.Status = "Editing"; state.CurrentRevisionId = Guid.NewGuid(); await database.SaveChangesAsync();
        var access = Assert.IsType<SharedReportAccess>(Assert.IsType<OkObjectResult>((await controller.Resolve(share.Id, new() { Token = share.Token! })).Result).Value);
        Assert.Equal(revisionId, access.SoilReport!.Id); Assert.Equal("Approved", access.SoilReport.Status);
        // Public rendering must honour the exact material choices approved in the linked revision.
        Assert.Equal(soil, access.SoilReport.Options.IncludeSoil); Assert.Equal(tissue, access.SoilReport.Options.IncludeTissue);
        Assert.Empty(access.SoilReport.Trace); Assert.Empty(access.SoilReport.DefinitionSnapshot);
        Assert.Empty(access.SoilReport.SampleMatches);
        // Clients receive the frozen tissue reading without internal matching reasons or staff attribution.
        Assert.Empty(access.SoilReport.Tissue.Matches);
        Assert.Equal(3.6, Assert.Single(access.SoilReport.Tissue.Samples).Values["NitrogenPc"].Value);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(request)).Result);
        Assert.IsType<NotFoundResult>((await controller.Resolve(share.Id, new() { Token = "wrong token" })).Result);
        await controller.Revoke(share.Id);
        Assert.IsType<NotFoundResult>((await controller.Resolve(share.Id, new() { Token = share.Token! })).Result);
    }

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
        var resolved = Assert.IsType<SharedReportAccess>(
            Assert.IsType<OkObjectResult>(resolvedResult.Result).Value);
        Assert.Equal(57646, resolved.SurveyId);
        Assert.Equal(MetaGrowReportAreas.Banana, resolved.ReportArea);

        // Existing links follow their creator's preference, regardless of who opens the link.
        database.Users.Add(new ApplicationUser { Id = "user-1" });
        await database.SaveChangesAsync();
        await new ReportBrandPreferenceStore(database).SaveAsync("user-1", ReportBrandPreference._tgs);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var brandedResult = await controller.Resolve(created.Id, new MetaGrowReportShareResolveRequest { Token = created.Token });
        var branded = Assert.IsType<SharedReportAccess>(Assert.IsType<OkObjectResult>(brandedResult.Result).Value);
        Assert.Equal(ReportBrandPreference._tgs, branded.BrandKey);

        database.ChangeTracker.Clear();
        var persisted = await database.ReportShares.SingleAsync();
        Assert.Equal(2, persisted.ViewCount);
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
