using Dokpod.ControlPlane.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class InfrastructureCompositionTests
{
    [Fact]
    public void AddControlPlaneInfrastructure_RejectsInvalidConnectionString()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddControlPlaneInfrastructure("not-a-postgresql-connection-string"));
    }
}
