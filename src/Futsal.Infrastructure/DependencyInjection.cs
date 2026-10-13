using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Futsal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FutsalPro")
            ?? throw new InvalidOperationException("Connection string 'FutsalPro' is required.");
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = configuration["Database:Password"] ?? string.Empty
        };

        services.AddDbContext<FutsalDbContext>(options => options.UseNpgsql(connectionStringBuilder.ConnectionString));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(3));
        return services;
    }
}
