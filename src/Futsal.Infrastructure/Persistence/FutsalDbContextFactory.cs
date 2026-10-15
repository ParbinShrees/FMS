using Futsal.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Futsal.Infrastructure.Persistence;

public sealed class FutsalDbContextFactory : IDesignTimeDbContextFactory<FutsalDbContext>
{
    public FutsalDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("src/Futsal.Api/appsettings.json", optional: true)
            .AddJsonFile("src/Futsal.Api/appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(FutsalDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("FutsalPro")
            ?? "Host=localhost;Port=5432;Database=futsalpro;Username=postgres";
        var options = new DbContextOptionsBuilder<FutsalDbContext>().UseNpgsql(connectionString, FutsalDbContext.ConfigurePostgres).Options;
        return new FutsalDbContext(options, DesignTimeCurrentUser.Instance, DesignTimeClock.Instance);
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public static DesignTimeCurrentUser Instance { get; } = new();
        public Guid? UserId => null;
        public Guid? OrganizationId => null;
        public string? Role => null;
        public bool IsSuperAdmin => true;
        public bool IsAuthenticated => false;
    }

    private sealed class DesignTimeClock : IClock
    {
        public static DesignTimeClock Instance { get; } = new();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
