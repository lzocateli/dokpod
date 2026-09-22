ALTER TABLE dokpod.agent_commands
    ADD COLUMN failure_code character varying(128),
    ADD COLUMN observed_container_revision character varying(255),
    ADD COLUMN completed_at_utc timestamp with time zone;
