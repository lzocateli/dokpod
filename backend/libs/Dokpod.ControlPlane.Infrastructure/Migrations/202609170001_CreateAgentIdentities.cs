using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609170001_CreateAgentIdentities")]
public partial class CreateAgentIdentities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE agent_identities (
                certificate_fingerprint character varying(64) NOT NULL CONSTRAINT pk_agent_identities PRIMARY KEY,
                environment_id uuid NOT NULL,
                revoked_at_utc timestamp with time zone NULL
            );

            CREATE INDEX ix_agent_identities_environment_id ON agent_identities (environment_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE agent_identities;");
    }
}