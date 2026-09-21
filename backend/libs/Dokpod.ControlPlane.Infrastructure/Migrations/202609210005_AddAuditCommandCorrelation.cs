using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609210005_AddAuditCommandCorrelation")]
public sealed class AddAuditCommandCorrelation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dokpod.audit_events
                ADD COLUMN command_id uuid;

            CREATE INDEX ix_audit_events_environment_command_time
                ON dokpod.audit_events (environment_id, command_id, occurred_at_utc)
                WHERE command_id IS NOT NULL;

            CREATE FUNCTION dokpod.dokpod_append_audit_event(
                p_event_id uuid,
                p_occurred_at_utc timestamptz,
                p_correlation_id uuid,
                p_actor_kind smallint,
                p_actor_id varchar(128),
                p_action varchar(128),
                p_environment_id uuid,
                p_command_id uuid,
                p_outcome smallint,
                p_failure_code varchar(128),
                p_payload_hash bytea)
            RETURNS void
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = pg_catalog, dokpod
            AS $$
            DECLARE
                inserted_event_id uuid;
                existing_payload_hash bytea;
            BEGIN
                INSERT INTO audit_event_keys (event_id, payload_hash)
                VALUES (p_event_id, p_payload_hash)
                ON CONFLICT (event_id) DO NOTHING
                RETURNING event_id INTO inserted_event_id;

                IF inserted_event_id IS NULL THEN
                    SELECT payload_hash
                    INTO existing_payload_hash
                    FROM audit_event_keys
                    WHERE event_id = p_event_id;

                    IF existing_payload_hash IS DISTINCT FROM p_payload_hash THEN
                        RAISE EXCEPTION 'audit event % already exists with conflicting payload', p_event_id
                            USING ERRCODE = '23505';
                    END IF;

                    RETURN;
                END IF;

                INSERT INTO audit_events (
                    event_id, occurred_at_utc, correlation_id, actor_kind,
                    actor_id, action, environment_id, command_id, outcome, failure_code)
                VALUES (
                    p_event_id, p_occurred_at_utc, p_correlation_id, p_actor_kind,
                    p_actor_id, p_action, p_environment_id, p_command_id, p_outcome, p_failure_code);
            END;
            $$;

            REVOKE ALL ON FUNCTION dokpod.dokpod_append_audit_event(
                uuid, timestamptz, uuid, smallint, varchar, varchar, uuid, uuid, smallint, varchar, bytea)
                FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION dokpod.dokpod_append_audit_event(
                uuid, timestamptz, uuid, smallint, varchar, varchar, uuid, uuid, smallint, varchar, bytea)
                TO dokpod_runtime;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The audit migration is forward-only to preserve evidence.");
    }
}