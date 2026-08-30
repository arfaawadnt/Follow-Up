using FollowUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FollowUp.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("app_user");
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid"); // xmin optimistic concurrency (IDN-4)
        b.IgnoreDomainEvents();
        b.MapAuditable();

        b.Property(x => x.Username).HasMaxLength(100).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(150);
        b.HasIndex(x => x.Username).IsUnique();

        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Language).HasMaxLength(8).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.IsBuiltIn).HasDefaultValue(false); // protected built-in admin (IDN-6)
        b.Property(x => x.FailedLoginCount);
        b.Property(x => x.LockedUntil);

        // PasswordHash value object -> columns.
        b.OwnsOne(x => x.Password, p =>
        {
            p.Property(h => h.Algorithm).HasColumnName("password_algorithm").HasMaxLength(40).IsRequired();
            p.Property(h => h.Iterations).HasColumnName("password_iterations");
            p.Property(h => h.Salt).HasColumnName("password_salt").HasMaxLength(200).IsRequired();
            p.Property(h => h.Hash).HasColumnName("password_hash").HasMaxLength(400).IsRequired();
        });
        b.Navigation(x => x.Password).IsRequired();

        // user -> role (RESTRICT); user -> rep (RESTRICT, one login per rep).
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.RepresentativeId).IsUnique().HasFilter("representative_id IS NOT NULL");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("role");
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion().HasColumnName("xmin").HasColumnType("xid"); // xmin optimistic concurrency (IDN-4)
        b.IgnoreDomainEvents();
        b.Ignore(x => x.EffectivePrivileges);
        b.MapAuditable();

        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.DefaultLanguage).HasMaxLength(8);
        b.Property(x => x.DefaultTheme).HasMaxLength(16);
        b.Property(x => x.IsBuiltIn);

        b.Property(x => x.Privileges)
            .HasColumnName("privileges")
            .HasConversion<StringSetConverter>(new StringSetComparer())
            .HasColumnType("jsonb");

        b.Property(x => x.Scope)
            .HasColumnName("scope")
            .HasConversion<OrgScopeConverter>(new OrgScopeComparer())
            .HasColumnType("jsonb")
            .IsRequired();
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("user_session");
        b.HasKey(x => x.Id);
        b.IgnoreDomainEvents();

        b.Property(x => x.TokenHash).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.TokenHash);
        b.Property(x => x.Ip).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(400);

        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
    }
}
