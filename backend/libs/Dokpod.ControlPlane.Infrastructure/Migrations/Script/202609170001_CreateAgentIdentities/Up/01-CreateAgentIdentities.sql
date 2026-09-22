CREATE TABLE agent_identities (
    certificate_fingerprint character varying(64) NOT NULL CONSTRAINT pk_agent_identities PRIMARY KEY,
    environment_id uuid NOT NULL,
    revoked_at_utc timestamp with time zone NULL
);

CREATE INDEX ix_agent_identities_environment_id ON agent_identities (environment_id);
