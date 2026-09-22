ALTER TABLE dokpod.agent_commands
    DROP CONSTRAINT ck_agent_commands_payload_hash,
    ALTER COLUMN payload_hash TYPE character varying(128);
