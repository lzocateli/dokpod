using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210002_ConstrainAgentCommandPayloadHash")]
public sealed class ConstrainAgentCommandPayloadHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dokpod.agent_commands
                ALTER COLUMN payload_hash TYPE character varying(64),
                ADD CONSTRAINT ck_agent_commands_payload_hash
                    CHECK (payload_hash ~ '^[0-9A-F]{64}$');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dokpod.agent_commands
                DROP CONSTRAINT ck_agent_commands_payload_hash,
                ALTER COLUMN payload_hash TYPE character varying(128);
            """);
    }
}