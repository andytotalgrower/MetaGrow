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

    [Fact]
    public async Task Only_one_open_property_merge_request_is_allowed_per_source_property()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.PropertyMergeRequests.Add(MergeRequest(MetaGrowPropertyMergeStatus.Pending));
            await setup.SaveChangesAsync();
        }

        await using (var duplicate = new ApplicationDbContext(options))
        {
            duplicate.PropertyMergeRequests.Add(MergeRequest(MetaGrowPropertyMergeStatus.Processing));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using (var historical = new ApplicationDbContext(options))
        {
            historical.PropertyMergeRequests.Add(MergeRequest(MetaGrowPropertyMergeStatus.Rejected));
            await historical.SaveChangesAsync();
            historical.PropertyMergeRequests.Add(MergeRequest(MetaGrowPropertyMergeStatus.Failed));
            await historical.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Only_one_open_sample_survey_deletion_request_is_allowed_per_survey()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.SampleSurveyDeletionRequests.Add(SampleDeletionRequest(MetaGrowSampleSurveyDeletionStatus.Pending));
            await setup.SaveChangesAsync();
        }

        await using (var duplicate = new ApplicationDbContext(options))
        {
            duplicate.SampleSurveyDeletionRequests.Add(SampleDeletionRequest(MetaGrowSampleSurveyDeletionStatus.Processing));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using (var historical = new ApplicationDbContext(options))
        {
            historical.SampleSurveyDeletionRequests.Add(SampleDeletionRequest(MetaGrowSampleSurveyDeletionStatus.Rejected));
            await historical.SaveChangesAsync();
            historical.SampleSurveyDeletionRequests.Add(SampleDeletionRequest(MetaGrowSampleSurveyDeletionStatus.Cancelled));
            await historical.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Sample_survey_deletion_request_locks_in_lab_counts_and_destructive_choice()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var request = SampleDeletionRequest(MetaGrowSampleSurveyDeletionStatus.Pending);
        request.LinkedSoilResultCount = 2;
        request.LinkedTissueResultCount = 3;
        request.LinkedSapResultCount = 4;
        request.LinkedQuickSoilResultCount = 5;
        request.LinkedLegacyResultCount = 6;
        request.DeleteLinkedLabResults = true;
        db.SampleSurveyDeletionRequests.Add(request);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await db.SampleSurveyDeletionRequests.SingleAsync();
        Assert.Equal(2, saved.LinkedSoilResultCount);
        Assert.Equal(3, saved.LinkedTissueResultCount);
        Assert.Equal(4, saved.LinkedSapResultCount);
        Assert.Equal(5, saved.LinkedQuickSoilResultCount);
        Assert.Equal(6, saved.LinkedLegacyResultCount);
        Assert.True(saved.DeleteLinkedLabResults);
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

    private static PropertyMergeRequest MergeRequest(MetaGrowPropertyMergeStatus status) => new()
    {
        Id = Guid.NewGuid(),
        SourcePropertyId = 123,
        SourcePropertyName = "Duplicate farm",
        TargetPropertyId = 456,
        TargetPropertyName = "Surviving farm",
        PlanJson = "{}",
        PlanHash = new string('A', 64),
        Status = status,
        RequestedByUserId = "user",
        RequestedByEmail = "user@example.com",
        RequestedUtc = DateTime.UtcNow
    };

    private static SampleSurveyDeletionRequest SampleDeletionRequest(MetaGrowSampleSurveyDeletionStatus status) => new()
    {
        Id = Guid.NewGuid(),
        SurveyId = 7187,
        PropertyId = 123,
        PropertyName = "Example farm",
        SurveyDate = new DateTime(2025, 11, 13),
        SampleCount = 4,
        Status = status,
        RequestedByUserId = "user",
        RequestedByEmail = "user@example.com",
        RequestedUtc = DateTime.UtcNow
    };
}
