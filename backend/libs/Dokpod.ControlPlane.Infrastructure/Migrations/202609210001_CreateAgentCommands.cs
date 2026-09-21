using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210001_CreateAgentCommands")]
public sealed class CreateAgentCommands : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dokpod.agent_commands (
                environment_id uuid NOT NULL,
                command_id uuid NOT NULL,
                kind smallint NOT NULL,
                container_id character varying(128) NOT NULL,
                expected_container_revision character varying(255) NOT NULL,
                payload_hash character varying(128) NOT NULL,
                deadline_utc timestamp with time zone NOT NULL,
                fencing_token bigint NOT NULL,
                state smallint NOT NULL,
                created_at_utc timestamp with time zone NOT NULL,
                updated_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT pk_agent_commands PRIMARY KEY (environment_id, command_id),
                CONSTRAINT fk_agent_commands_environments_environment_id
                    FOREIGN KEY (environment_id) REFERENCES dokpod.environments (environment_id) ON DELETE RESTRICT,
                CONSTRAINT ck_agent_commands_kind CHECK (kind BETWEEN 1 AND 4),
                CONSTRAINT ck_agent_commands_state CHECK (state BETWEEN 0 AND 5),
                CONSTRAINT ck_agent_commands_fencing_token CHECK (fencing_token > 0)
            );

            CREATE INDEX ix_agent_commands_environment_id_state_created_at_utc
                ON dokpod.agent_commands (environment_id, state, created_at_utc);
            CREATE INDEX ix_agent_commands_state_deadline_utc
                ON dokpod.agent_commands (state, deadline_utc);

            GRANT SELECT, INSERT, UPDATE, DELETE ON dokpod.agent_commands TO dokpod_runtime;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE dokpod.agent_commands;");
    }
}