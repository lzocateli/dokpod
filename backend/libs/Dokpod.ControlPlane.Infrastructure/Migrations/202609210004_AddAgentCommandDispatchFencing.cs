using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210004_AddAgentCommandDispatchFencing")]
public sealed class AddAgentCommandDispatchFencing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "last_dispatch_fencing_token",
            schema: "dokpod",
            table: "agent_commands",
            type: "bigint",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "last_dispatch_fencing_token",
            schema: "dokpod",
            table: "agent_commands");
    }
}