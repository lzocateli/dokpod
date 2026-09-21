using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.ControlPlane.Application.Inventory;
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
        void ConfigureNpgsql(DbContextOptionsBuilder options) => options.UseNpgsql(
            parsedConnectionString.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ControlPlaneSchema.Name));

        services.AddDbContext<ControlPlaneDbContext>(ConfigureNpgsql);
        services.AddSingleton<AuditEventMetrics>();
        services.AddScoped<IAgentIdentityRegistry, PostgresAgentIdentityRegistry>();
        services.AddScoped<IAgentCommandStore, PostgresAgentCommandStore>();
        services.AddScoped<IAuditEventWriter, PostgresAuditEventWriter>();
        services.AddScoped<IEnvironmentRegistrationStore, PostgresEnvironmentRegistrationStore>();
        services.AddScoped<IInventoryProjectionStore, PostgresInventoryProjectionStore>();

        return services;
    }
}
