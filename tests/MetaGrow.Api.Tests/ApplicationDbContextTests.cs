using MetaGrow.Api.Data;
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
}
