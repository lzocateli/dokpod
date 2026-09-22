using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210005_AddAuditCommandCorrelation")]
public sealed class AddAuditCommandCorrelation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSqlScriptLoader.Load(
            "202609210005_AddAuditCommandCorrelation/Up/01-AddAuditCommandCorrelation.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The audit migration is forward-only to preserve evidence.");
    }
}