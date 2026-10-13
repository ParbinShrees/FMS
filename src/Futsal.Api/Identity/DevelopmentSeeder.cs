using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Identity;

public sealed class DevelopmentSeeder(
    FutsalDbContext database,
    PasswordHasher<AppUser> passwordHasher,
    IConfiguration configuration,
    IClock clock,
    ILogger<DevelopmentSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var superAdminPassword = configuration["Seed:SuperAdminPassword"];
        var ownerPassword = configuration["Seed:OwnerPassword"];
        if (string.IsNullOrWhiteSpace(superAdminPassword) || string.IsNullOrWhiteSpace(ownerPassword))
        {
            logger.LogWarning("Development identity seeding skipped because seed passwords are missing from user-secrets.");
            return;
        }

        var superAdminEmail = configuration["Seed:SuperAdminEmail"] ?? "superadmin@futsalpro.local";
        var ownerEmail = configuration["Seed:OwnerEmail"] ?? "owner@sample-futsal.local";
        if (string.Equals(superAdminEmail, ownerEmail, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Development SuperAdmin and Owner seed emails must be different.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var superAdmin = await database.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Email == superAdminEmail, cancellationToken);
        if (superAdmin is null)
        {
            superAdmin = new AppUser
            {
                Email = superAdminEmail,
                FullName = "FutsalPro SuperAdmin",
                IsPlatformAdmin = true,
                CreatedAt = clock.UtcNow
            };
            superAdmin.PasswordHash = passwordHasher.HashPassword(superAdmin, superAdminPassword);
            database.Users.Add(superAdmin);
        }

        var organization = await database.Organizations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Name == "Sample Futsal", cancellationToken);
        if (organization is null)
        {
            organization = new Organization { Name = "Sample Futsal", CreatedAt = clock.UtcNow };
            database.Organizations.Add(organization);
        }

        var owner = await database.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Email == ownerEmail, cancellationToken);
        if (owner is null)
        {
            owner = new AppUser { Email = ownerEmail, FullName = "Sample Futsal Owner", CreatedAt = clock.UtcNow };
            owner.PasswordHash = passwordHasher.HashPassword(owner, ownerPassword);
            database.Users.Add(owner);
        }

        var hasOwnerMembership = await database.Memberships.IgnoreQueryFilters()
            .AnyAsync(x => x.OrganizationId == organization.Id && x.UserId == owner.Id, cancellationToken);
        if (!hasOwnerMembership)
        {
            database.Memberships.Add(new Membership
            {
                OrganizationId = organization.Id,
                UserId = owner.Id,
                Role = OrganizationRole.OWNER
            });
            database.AuditLogs.Add(new AuditLog
            {
                OrganizationId = organization.Id,
                UserId = superAdmin.Id,
                Action = "ROLE_ASSIGNED",
                EntityType = "Membership",
                EntityId = owner.Id,
                After = "{\"role\":\"Owner\"}",
                CreatedAt = clock.UtcNow
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Development SuperAdmin and sample Owner are ready.");
    }
}
