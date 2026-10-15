using System.Security.Claims;
using MetaGrow.Api.Controllers;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Tests;

public sealed class ReportPreferencesTests
{
    [Fact]
    public async Task Preference_is_persisted_per_user_and_invalid_values_do_not_overwrite_it()
    {
        // An isolated in-memory database verifies persistence without touching application data.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options);
        await database.Database.EnsureCreatedAsync();
        database.Users.AddRange(new ApplicationUser { Id = "one" }, new ApplicationUser { Id = "two" });
        await database.SaveChangesAsync();
        var store = new ReportBrandPreferenceStore(database);
        var controller = new ReportPreferencesController(database, store)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "one")], "Test"))
            } }
        };

        Assert.Equal(ReportBrandPreference._metagen, await store.GetAsync("one"));
        Assert.IsType<NoContentResult>(await controller.Save(new() { BrandKey = ReportBrandPreference._tgs }));
        database.ChangeTracker.Clear();
        Assert.Equal(ReportBrandPreference._tgs, await store.GetAsync("one"));
        Assert.Equal(ReportBrandPreference._metagen, await store.GetAsync("two"));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(new() { BrandKey = "invalid" }));
        Assert.Equal(ReportBrandPreference._tgs, await store.GetAsync("one"));
        Assert.IsType<NoContentResult>(await controller.Save(new() { BrandKey = ReportBrandPreference._metagen }));
        Assert.Single(await database.UserTokens.ToListAsync());

        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.IsType<UnauthorizedResult>((await controller.Get()).Result);
        Assert.IsType<UnauthorizedResult>(await controller.Save(new() { BrandKey = ReportBrandPreference._tgs }));
    }
}
