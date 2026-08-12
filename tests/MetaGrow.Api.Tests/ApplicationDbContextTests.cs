using MetaGrow.Api.Data;
using ApiModels.MetaGrow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Tests;

public sealed class ApplicationDbContextTests
{
    [Fact]
    public async Task Historical_email_address_must_be_unique_across_users()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = "one", UserName = "one@example.com" },
            new ApplicationUser { Id = "two", UserName = "two@example.com" });
        db.UserEmailAddresses.Add(new UserEmailAddress
        {
            UserId = "one", Email = "historic@example.com", NormalizedEmail = "HISTORIC@EXAMPLE.COM"
        });
        await db.SaveChangesAsync();

        db.UserEmailAddresses.Add(new UserEmailAddress
        {
            UserId = "two", Email = "HISTORIC@example.com", NormalizedEmail = "HISTORIC@EXAMPLE.COM"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Report_share_token_hash_must_be_unique()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ReportShares.Add(new ReportShare
        {
            Id = Guid.NewGuid(), SurveyId = 1, Name = "First", TokenHash = "same-hash",
            ProtectedToken = "protected-one", CreatedByUserId = "user", CreatedByEmail = "user@example.com"
        });
        await db.SaveChangesAsync();

        db.ReportShares.Add(new ReportShare
        {
            Id = Guid.NewGuid(), SurveyId = 2, Name = "Second", TokenHash = "same-hash",
            ProtectedToken = "protected-two", CreatedByUserId = "user", CreatedByEmail = "user@example.com"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_open_property_deletion_request_is_allowed_per_property()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.PropertyDeletionRequests.Add(DeletionRequest(MetaGrowPropertyDeletionStatus.Pending));
            await setup.SaveChangesAsync();
        }

        await using (var duplicate = new ApplicationDbContext(options))
        {
            duplicate.PropertyDeletionRequests.Add(DeletionRequest(MetaGrowPropertyDeletionStatus.Processing));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using (var historical = new ApplicationDbContext(options))
        {
            historical.PropertyDeletionRequests.Add(DeletionRequest(MetaGrowPropertyDeletionStatus.Completed));
            await historical.SaveChangesAsync();
        }
    }

    private static PropertyDeletionRequest DeletionRequest(MetaGrowPropertyDeletionStatus status) => new()
    {
        Id = Guid.NewGuid(),
        PropertyId = 123,
        PropertyName = "Example farm",
        Status = status,
        RequestedByUserId = "user",
        RequestedByEmail = "user@example.com",
        RequestedUtc = DateTime.UtcNow
    };
}
