using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Dokpod.ControlPlane.Infrastructure;

public static class ControlPlaneInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddControlPlaneInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Control Plane database connection string is required.", nameof(connectionString));
        }

        var parsedConnectionString = new NpgsqlConnectionStringBuilder(connectionString);
        services.AddDbContext<ControlPlaneDbContext>(options =>
            options.UseNpgsql(parsedConnectionString.ConnectionString));

        return services;
    }
}
