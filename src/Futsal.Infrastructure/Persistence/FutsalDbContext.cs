using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Npgsql.NameTranslation;

namespace Futsal.Infrastructure.Persistence;

public sealed class FutsalDbContext(DbContextOptions<FutsalDbContext> options, ICurrentUser currentUser, IClock clock) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.HasPostgresEnum<OrganizationRole>(name: "member_role", nameTranslator: new NpgsqlNullNameTranslator());

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.Phone).HasColumnName("phone");
            entity.HasIndex(x => x.Phone).IsUnique();
            entity.Property(x => x.Email).HasColumnName("email").HasColumnType("citext");
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.FullName).HasColumnName("full_name").IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash");
            entity.Property(x => x.IsPlatformAdmin).HasColumnName("is_platform_admin");
            entity.Property(x => x.IsActive).HasColumnName("is_active");
            entity.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count");
            entity.Property(x => x.LockedUntil).HasColumnName("locked_until");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.Name).HasColumnName("name").IsRequired();
            entity.Property(x => x.PanNumber).HasColumnName("pan_number");
            entity.Property(x => x.Phone).HasColumnName("phone");
            entity.Property(x => x.Email).HasColumnName("email").HasColumnType("citext");
            entity.Property(x => x.Status).HasColumnName("status").HasDefaultValue("ACTIVE");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.Id == currentUser.OrganizationId));
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("memberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Role).HasColumnName("role").HasColumnType("member_role");
            entity.Property(x => x.VenueId).HasColumnName("venue_id");
            entity.HasIndex(x => new { x.OrganizationId, x.UserId, x.VenueId }).IsUnique();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            entity.Property(x => x.ReplacedBy).HasColumnName("replaced_by");
            entity.Property(x => x.CreatedIp).HasColumnName("created_ip");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OtpCode>(entity =>
        {
            entity.ToTable("otp_codes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            entity.Property(x => x.Phone).HasColumnName("phone").IsRequired();
            entity.Property(x => x.CodeHash).HasColumnName("code_hash").IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.Attempts).HasColumnName("attempts");
            entity.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasIndex(x => new { x.Phone, x.CreatedAt });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(x => x.OrganizationId).HasColumnName("organization_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Action).HasColumnName("action").IsRequired();
            entity.Property(x => x.EntityType).HasColumnName("entity_type").IsRequired();
            entity.Property(x => x.EntityId).HasColumnName("entity_id");
            entity.Property(x => x.Before).HasColumnName("before").HasColumnType("jsonb");
            entity.Property(x => x.After).HasColumnName("after").HasColumnType("jsonb");
            entity.Property(x => x.Ip).HasColumnName("ip");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
            entity.HasQueryFilter(x => currentUser.IsSuperAdmin || (currentUser.OrganizationId != null && x.OrganizationId == currentUser.OrganizationId));
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecordRoleChanges();
        EnforceTenantWriteScope();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RecordRoleChanges();
        EnforceTenantWriteScope();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceTenantWriteScope()
    {
        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Audit entries are immutable.");
        }

        if (!currentUser.IsAuthenticated || currentUser.IsSuperAdmin) return;

        foreach (var entry in ChangeTracker.Entries<IOrganizationScoped>().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (currentUser.OrganizationId is null || entry.Entity.OrganizationId != currentUser.OrganizationId)
                throw new UnauthorizedAccessException("The current user cannot change data outside their organization.");
        }

        foreach (var entry in ChangeTracker.Entries<Organization>().Where(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.Id != currentUser.OrganizationId)
                throw new UnauthorizedAccessException("The current user cannot change another organization.");
        }
    }

    private void RecordRoleChanges()
    {
        foreach (var entry in ChangeTracker.Entries<Membership>().Where(x => x.State == EntityState.Modified && x.Property(y => y.Role).IsModified))
        {
            var previousRole = entry.Property(x => x.Role).OriginalValue;
            var currentRole = entry.Entity.Role;
            if (previousRole == currentRole) continue;

            AuditLogs.Add(new AuditLog
            {
                OrganizationId = entry.Entity.OrganizationId,
                UserId = currentUser.UserId,
                Action = "ROLE_CHANGED",
                EntityType = "Membership",
                EntityId = entry.Entity.Id,
                Before = JsonSerializer.Serialize(new { role = RoleName(previousRole) }),
                After = JsonSerializer.Serialize(new { role = RoleName(currentRole) }),
                CreatedAt = clock.UtcNow
            });
        }
    }

    private static string RoleName(OrganizationRole role) => role switch
    {
        OrganizationRole.OWNER => "Owner",
        OrganizationRole.MANAGER => "Manager",
        OrganizationRole.CASHIER => "Cashier",
        _ => role.ToString()
    };
}
