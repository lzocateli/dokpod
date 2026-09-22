ALTER TABLE dokpod.agent_commands
    DROP COLUMN completed_at_utc,
    DROP COLUMN observed_container_revision,
    DROP COLUMN failure_code;
