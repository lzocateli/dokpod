using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609170003_IsolateControlPlaneSchema")]
public partial class IsolateControlPlaneSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MigrationSqlScriptLoader.Load(
            "202609170003_IsolateControlPlaneSchema/Up/01-IsolateControlPlaneSchema.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The schema isolation migration is forward-only to preserve evidence.");
    }
}
