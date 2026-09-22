using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210002_ConstrainAgentCommandPayloadHash")]
public sealed class ConstrainAgentCommandPayloadHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSqlScriptLoader.Load(
            "202609210002_ConstrainAgentCommandPayloadHash/Up/01-ConstrainPayloadHash.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSqlScriptLoader.Load(
            "202609210002_ConstrainAgentCommandPayloadHash/Down/01-RestorePayloadHash.sql"));
    }
}