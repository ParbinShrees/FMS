using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Futsal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FutsalPro")
            ?? throw new InvalidOperationException("Connection string 'FutsalPro' is required.");
        services.AddDbContext<FutsalDbContext>(options => options.UseNpgsql(connectionString, FutsalDbContext.ConfigurePostgres));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(3));
        return services;
    }
}
