using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests.Agents;

public sealed class AgentIdentityRegistryTests
{
    [Fact]
    public async Task FindByFingerprintAsync_ReturnsActiveIdentityWithNormalizedFingerprint()
    {
        await using var dbContext = CreateDbContext();
        var environmentId = Guid.NewGuid();
        dbContext.AgentIdentities.Add(new AgentIdentityEntity
        {
            EnvironmentId = environmentId,
            CertificateFingerprint = "AABBCC",
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var identity = await new PostgresAgentIdentityRegistry(dbContext)
            .FindByFingerprintAsync(" aabbcc ", TestContext.Current.CancellationToken);

        Assert.NotNull(identity);
        Assert.Equal(environmentId, identity!.EnvironmentId);
        Assert.Equal("AABBCC", identity.CertificateFingerprint);
    }

    [Fact]
    public async Task FindByFingerprintAsync_DoesNotReturnRevokedIdentity()
    {
        await using var dbContext = CreateDbContext();
        dbContext.AgentIdentities.Add(new AgentIdentityEntity
        {
            EnvironmentId = Guid.NewGuid(),
            CertificateFingerprint = "AABBCC",
            RevokedAtUtc = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var identity = await new PostgresAgentIdentityRegistry(dbContext)
            .FindByFingerprintAsync("AABBCC", TestContext.Current.CancellationToken);

        Assert.Null(identity);
    }

    [Fact]
    public async Task UnavailableRegistry_DoesNotAuthenticateAnyCertificate()
    {
        var identity = await new UnavailableAgentIdentityRegistry()
            .FindByFingerprintAsync("AABBCC", TestContext.Current.CancellationToken);

        Assert.Null(identity);
    }

    private static ControlPlaneDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ControlPlaneDbContext(options);
    }
}