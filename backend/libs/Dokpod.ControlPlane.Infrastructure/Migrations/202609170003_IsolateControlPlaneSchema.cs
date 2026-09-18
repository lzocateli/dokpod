using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Dokpod.ControlPlane.Infrastructure.Migrations;

[DbContext(typeof(ControlPlaneDbContext))]
[Migration("202609170003_IsolateControlPlaneSchema")]
public partial class IsolateControlPlaneSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SCHEMA IF NOT EXISTS dokpod;

            CREATE TABLE IF NOT EXISTS dokpod."__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" character varying(32) NOT NULL
            );

            DO $$
            BEGIN
                IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
                    INSERT INTO dokpod."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    SELECT "MigrationId", "ProductVersion"
                    FROM public."__EFMigrationsHistory"
                    WHERE "MigrationId" LIKE '202609%'
                    ON CONFLICT ("MigrationId") DO NOTHING;

                    DELETE FROM public."__EFMigrationsHistory"
                    WHERE "MigrationId" LIKE '202609%';

                    IF NOT EXISTS (SELECT 1 FROM public."__EFMigrationsHistory") THEN
                        DROP TABLE public."__EFMigrationsHistory";
                    END IF;
                END IF;
            END
            $$;

            ALTER TABLE IF EXISTS public.audit_event_keys SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events_2026_09 SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events_2026_10 SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events_2026_11 SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events_2026_12 SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events_2027_01 SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.audit_events_default SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.agent_identities SET SCHEMA dokpod;
            ALTER TABLE IF EXISTS public.environments SET SCHEMA dokpod;

            DO $$
            BEGIN
                IF to_regprocedure('public.dokpod_reject_audit_mutation()') IS NOT NULL THEN
                    ALTER FUNCTION public.dokpod_reject_audit_mutation() SET SCHEMA dokpod;
                END IF;

                IF to_regprocedure('public.dokpod_append_audit_event(uuid,timestamp with time zone,uuid,smallint,character varying,character varying,uuid,smallint,character varying,bytea)') IS NOT NULL THEN
                    ALTER FUNCTION public.dokpod_append_audit_event(
                        uuid,
                        timestamp with time zone,
                        uuid,
                        smallint,
                        character varying,
                        character varying,
                        uuid,
                        smallint,
                        character varying,
                        bytea) SET SCHEMA dokpod;
                END IF;

                IF to_regprocedure('dokpod.dokpod_append_audit_event(uuid,timestamp with time zone,uuid,smallint,character varying,character varying,uuid,smallint,character varying,bytea)') IS NOT NULL THEN
                    ALTER FUNCTION dokpod.dokpod_append_audit_event(
                        uuid,
                        timestamp with time zone,
                        uuid,
                        smallint,
                        character varying,
                        character varying,
                        uuid,
                        smallint,
                        character varying,
                        bytea)
                        SET search_path = pg_catalog, dokpod;
                END IF;
            END
            $$;

            REVOKE ALL ON SCHEMA dokpod FROM PUBLIC;
            GRANT USAGE ON SCHEMA dokpod TO dokpod_runtime;
            REVOKE ALL ON ALL TABLES IN SCHEMA dokpod FROM PUBLIC;
            GRANT SELECT ON dokpod.audit_events, dokpod.audit_event_keys TO dokpod_runtime;
            GRANT EXECUTE ON FUNCTION dokpod.dokpod_append_audit_event(
                uuid,
                timestamp with time zone,
                uuid,
                smallint,
                character varying,
                character varying,
                uuid,
                smallint,
                character varying,
                bytea)
                TO dokpod_runtime;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The schema isolation migration is forward-only to preserve evidence.");
    }
}
