using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ReportShare> ReportShares => Set<ReportShare>();
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

        builder.Entity<ReportShare>(entity =>
        {
            entity.Property(share => share.Name).HasMaxLength(120).IsRequired();
            entity.Property(share => share.TokenHash).HasMaxLength(44).IsRequired();
            entity.Property(share => share.ProtectedToken).HasMaxLength(1000).IsRequired();
            entity.Property(share => share.CreatedByUserId).HasMaxLength(450).IsRequired();
            entity.Property(share => share.CreatedByEmail).HasMaxLength(256).IsRequired();
            entity.Property(share => share.RevokedByUserId).HasMaxLength(450);
            entity.Property(share => share.RevokedByEmail).HasMaxLength(256);
            entity.HasIndex(share => share.TokenHash).IsUnique();
            entity.HasIndex(share => new { share.SurveyId, share.CreatedUtc });
        });
    }
}
