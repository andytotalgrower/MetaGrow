using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Identity;

namespace MetaGrow.Api.Data;

/// <summary>Generates migrations without booting the API, applying migrations, or loading operational credentials.</summary>
public sealed class ApplicationDbContextDesignFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Schema generation never opens a connection. An explicit connection is required for deployment tooling.
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=MetaGrow_DesignOnly;Integrated Security=true"));
        // Identity's schema version is part of the EF model, including existing passkey tables.
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            options.Stores.MaxLengthForKeys = 450;
        }).AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider().GetRequiredService<ApplicationDbContext>();
    }
}
