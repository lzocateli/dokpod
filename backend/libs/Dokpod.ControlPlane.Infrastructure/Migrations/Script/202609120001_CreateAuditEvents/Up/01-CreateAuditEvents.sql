DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'dokpod_runtime') THEN
        CREATE ROLE dokpod_runtime NOLOGIN;
    END IF;
END
$$;

CREATE TABLE audit_event_keys (
    event_id uuid NOT NULL CONSTRAINT pk_audit_event_keys PRIMARY KEY,
    payload_hash bytea NOT NULL
);

CREATE TABLE audit_events (
    event_id uuid NOT NULL,
    occurred_at_utc timestamp with time zone NOT NULL,
    correlation_id uuid NOT NULL,
    actor_kind smallint NOT NULL,
    actor_id character varying(128) NOT NULL,
    action character varying(128) NOT NULL,
    environment_id uuid NOT NULL,
    outcome smallint NOT NULL,
    failure_code character varying(128),
    CONSTRAINT pk_audit_events PRIMARY KEY (event_id, occurred_at_utc)
) PARTITION BY RANGE (occurred_at_utc);

CREATE TABLE audit_events_2026_09 PARTITION OF audit_events
    FOR VALUES FROM ('2026-09-01T00:00:00Z') TO ('2026-10-01T00:00:00Z');
CREATE TABLE audit_events_2026_10 PARTITION OF audit_events
    FOR VALUES FROM ('2026-10-01T00:00:00Z') TO ('2026-11-01T00:00:00Z');
CREATE TABLE audit_events_2026_11 PARTITION OF audit_events
    FOR VALUES FROM ('2026-11-01T00:00:00Z') TO ('2026-12-01T00:00:00Z');
CREATE TABLE audit_events_2026_12 PARTITION OF audit_events
    FOR VALUES FROM ('2026-12-01T00:00:00Z') TO ('2027-01-01T00:00:00Z');
CREATE TABLE audit_events_2027_01 PARTITION OF audit_events
    FOR VALUES FROM ('2027-01-01T00:00:00Z') TO ('2027-02-01T00:00:00Z');
CREATE TABLE audit_events_default PARTITION OF audit_events DEFAULT;

CREATE INDEX ix_audit_events_correlation_id ON audit_events (correlation_id);
CREATE INDEX ix_audit_events_environment_time ON audit_events (environment_id, occurred_at_utc);

CREATE FUNCTION dokpod_reject_audit_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'audit events are append-only';
END;
$$;

CREATE TRIGGER trg_audit_events_append_only
BEFORE UPDATE OR DELETE ON audit_events
FOR EACH ROW EXECUTE FUNCTION dokpod_reject_audit_mutation();

CREATE TRIGGER trg_audit_event_keys_append_only
BEFORE UPDATE OR DELETE ON audit_event_keys
FOR EACH ROW EXECUTE FUNCTION dokpod_reject_audit_mutation();

CREATE FUNCTION dokpod_append_audit_event(
    p_event_id uuid,
    p_occurred_at_utc timestamptz,
    p_correlation_id uuid,
    p_actor_kind smallint,
    p_actor_id varchar(128),
    p_action varchar(128),
    p_environment_id uuid,
    p_outcome smallint,
    p_failure_code varchar(128),
    p_payload_hash bytea)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, public
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
        actor_id, action, environment_id, outcome, failure_code)
    VALUES (
        p_event_id, p_occurred_at_utc, p_correlation_id, p_actor_kind,
        p_actor_id, p_action, p_environment_id, p_outcome, p_failure_code);
END;
$$;

REVOKE ALL ON audit_events, audit_event_keys FROM PUBLIC;
GRANT SELECT ON audit_events, audit_event_keys TO dokpod_runtime;
GRANT EXECUTE ON FUNCTION dokpod_append_audit_event(
    uuid, timestamptz, uuid, smallint, varchar, varchar, uuid, smallint, varchar, bytea)
    TO dokpod_runtime;
