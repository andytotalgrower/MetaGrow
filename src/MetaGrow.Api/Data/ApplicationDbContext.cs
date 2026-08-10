using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserEmailAddress> UserEmailAddresses => Set<UserEmailAddress>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.Property(token => token.TokenHash).HasMaxLength(88).IsRequired();
            entity.Property(token => token.CreatedByIp).HasMaxLength(45);
            entity.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserEmailAddress>(entity =>
        {
            entity.Property(address => address.Email).HasMaxLength(256).IsRequired();
            entity.Property(address => address.NormalizedEmail).HasMaxLength(256).IsRequired();
            entity.HasIndex(address => address.NormalizedEmail).IsUnique();
            entity.HasIndex(address => address.UserId)
                .HasFilter("[IsPrimary] = 1")
                .IsUnique();
            entity.HasOne(address => address.User)
                .WithMany(user => user.EmailAddresses)
                .HasForeignKey(address => address.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
