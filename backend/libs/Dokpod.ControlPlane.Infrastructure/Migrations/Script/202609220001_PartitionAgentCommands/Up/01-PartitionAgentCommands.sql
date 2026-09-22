ALTER TABLE dokpod.agent_commands RENAME TO agent_commands_unpartitioned;
ALTER TABLE dokpod.agent_commands_unpartitioned
    RENAME CONSTRAINT pk_agent_commands TO pk_agent_commands_unpartitioned;
ALTER TABLE dokpod.agent_commands_unpartitioned
    RENAME CONSTRAINT fk_agent_commands_environments_environment_id
    TO fk_agent_commands_unpartitioned_environments_environment_id;
ALTER TABLE dokpod.agent_commands_unpartitioned
    RENAME CONSTRAINT ck_agent_commands_kind TO ck_agent_commands_unpartitioned_kind;
ALTER TABLE dokpod.agent_commands_unpartitioned
    RENAME CONSTRAINT ck_agent_commands_state TO ck_agent_commands_unpartitioned_state;
ALTER TABLE dokpod.agent_commands_unpartitioned
    RENAME CONSTRAINT ck_agent_commands_fencing_token
    TO ck_agent_commands_unpartitioned_fencing_token;
ALTER TABLE dokpod.agent_commands_unpartitioned
    RENAME CONSTRAINT ck_agent_commands_payload_hash
    TO ck_agent_commands_unpartitioned_payload_hash;
ALTER INDEX dokpod.ix_agent_commands_environment_id_state_created_at_utc
    RENAME TO ix_agent_commands_unpartitioned_environment_state_created;
ALTER INDEX dokpod.ix_agent_commands_state_deadline_utc
    RENAME TO ix_agent_commands_unpartitioned_state_deadline;

CREATE TABLE dokpod.agent_command_keys (
    environment_id uuid NOT NULL,
    command_id uuid NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_agent_command_keys PRIMARY KEY (environment_id, command_id),
    CONSTRAINT fk_agent_command_keys_environments_environment_id
        FOREIGN KEY (environment_id) REFERENCES dokpod.environments (environment_id) ON DELETE RESTRICT
);

CREATE TABLE dokpod.agent_commands (
    environment_id uuid NOT NULL,
    command_id uuid NOT NULL,
    kind smallint NOT NULL,
    container_id character varying(128) NOT NULL,
    expected_container_revision character varying(255) NOT NULL,
    payload_hash character varying(64) NOT NULL,
    deadline_utc timestamp with time zone NOT NULL,
    fencing_token bigint NOT NULL,
    last_dispatch_fencing_token bigint,
    state smallint NOT NULL,
    failure_code character varying(128),
    observed_container_revision character varying(255),
    completed_at_utc timestamp with time zone,
    created_at_utc timestamp with time zone NOT NULL,
    updated_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_agent_commands PRIMARY KEY (environment_id, command_id, created_at_utc),
    CONSTRAINT fk_agent_commands_environments_environment_id
        FOREIGN KEY (environment_id) REFERENCES dokpod.environments (environment_id) ON DELETE RESTRICT,
    CONSTRAINT ck_agent_commands_kind CHECK (kind BETWEEN 1 AND 4),
    CONSTRAINT ck_agent_commands_state CHECK (state BETWEEN 0 AND 5),
    CONSTRAINT ck_agent_commands_fencing_token CHECK (fencing_token > 0),
    CONSTRAINT ck_agent_commands_payload_hash CHECK (payload_hash ~ '^[0-9A-F]{64}$')
) PARTITION BY RANGE (created_at_utc);

CREATE TABLE dokpod.agent_commands_2026_09 PARTITION OF dokpod.agent_commands
    FOR VALUES FROM ('2026-09-01T00:00:00Z') TO ('2026-10-01T00:00:00Z');
CREATE TABLE dokpod.agent_commands_2026_10 PARTITION OF dokpod.agent_commands
    FOR VALUES FROM ('2026-10-01T00:00:00Z') TO ('2026-11-01T00:00:00Z');
CREATE TABLE dokpod.agent_commands_2026_11 PARTITION OF dokpod.agent_commands
    FOR VALUES FROM ('2026-11-01T00:00:00Z') TO ('2026-12-01T00:00:00Z');
CREATE TABLE dokpod.agent_commands_2026_12 PARTITION OF dokpod.agent_commands
    FOR VALUES FROM ('2026-12-01T00:00:00Z') TO ('2027-01-01T00:00:00Z');
CREATE TABLE dokpod.agent_commands_2027_01 PARTITION OF dokpod.agent_commands
    FOR VALUES FROM ('2027-01-01T00:00:00Z') TO ('2027-02-01T00:00:00Z');
CREATE TABLE dokpod.agent_commands_2027_02 PARTITION OF dokpod.agent_commands
    FOR VALUES FROM ('2027-02-01T00:00:00Z') TO ('2027-03-01T00:00:00Z');
CREATE TABLE dokpod.agent_commands_default PARTITION OF dokpod.agent_commands DEFAULT;

CREATE INDEX ix_agent_commands_environment_id_state_created_at_utc
    ON dokpod.agent_commands (environment_id, state, created_at_utc);
CREATE INDEX ix_agent_commands_state_deadline_utc
    ON dokpod.agent_commands (state, deadline_utc);

CREATE FUNCTION dokpod.dokpod_reserve_agent_command_key()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, dokpod
AS $$
BEGIN
    INSERT INTO agent_command_keys (environment_id, command_id, created_at_utc)
    VALUES (NEW.environment_id, NEW.command_id, NEW.created_at_utc)
    ON CONFLICT (environment_id, command_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN NULL;
    END IF;

    RETURN NEW;
END;
$$;

CREATE FUNCTION dokpod.dokpod_release_agent_command_key()
RETURNS trigger
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, dokpod
AS $$
BEGIN
    DELETE FROM agent_command_keys
    WHERE environment_id = OLD.environment_id
      AND command_id = OLD.command_id;
    RETURN OLD;
END;
$$;

CREATE TRIGGER trg_agent_commands_reserve_key
BEFORE INSERT ON dokpod.agent_commands
FOR EACH ROW EXECUTE FUNCTION dokpod.dokpod_reserve_agent_command_key();

CREATE TRIGGER trg_agent_commands_release_key
AFTER DELETE ON dokpod.agent_commands
FOR EACH ROW EXECUTE FUNCTION dokpod.dokpod_release_agent_command_key();

REVOKE ALL ON FUNCTION dokpod.dokpod_reserve_agent_command_key() FROM PUBLIC;
REVOKE ALL ON FUNCTION dokpod.dokpod_release_agent_command_key() FROM PUBLIC;

INSERT INTO dokpod.agent_commands (
    environment_id, command_id, kind, container_id,
    expected_container_revision, payload_hash, deadline_utc,
    fencing_token, last_dispatch_fencing_token, state, failure_code,
    observed_container_revision, completed_at_utc, created_at_utc, updated_at_utc)
SELECT
    environment_id, command_id, kind, container_id,
    expected_container_revision, payload_hash, deadline_utc,
    fencing_token, last_dispatch_fencing_token, state, failure_code,
    observed_container_revision, completed_at_utc, created_at_utc, updated_at_utc
FROM dokpod.agent_commands_unpartitioned;

DROP TABLE dokpod.agent_commands_unpartitioned;

REVOKE ALL ON dokpod.agent_commands, dokpod.agent_command_keys FROM PUBLIC;
GRANT SELECT, INSERT, UPDATE, DELETE ON dokpod.agent_commands TO dokpod_runtime;
GRANT SELECT ON dokpod.agent_command_keys TO dokpod_runtime;
