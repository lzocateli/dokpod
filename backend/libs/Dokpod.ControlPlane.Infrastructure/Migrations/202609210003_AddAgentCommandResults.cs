using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210003_AddAgentCommandResults")]
public sealed class AddAgentCommandResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dokpod.agent_commands
                ADD COLUMN failure_code character varying(128),
                ADD COLUMN observed_container_revision character varying(255),
                ADD COLUMN completed_at_utc timestamp with time zone;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dokpod.agent_commands
                DROP COLUMN completed_at_utc,
                DROP COLUMN observed_container_revision,
                DROP COLUMN failure_code;
            """);
    }
}