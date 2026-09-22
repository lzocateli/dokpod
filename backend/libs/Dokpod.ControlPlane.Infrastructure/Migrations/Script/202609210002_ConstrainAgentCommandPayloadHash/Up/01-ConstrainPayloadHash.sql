ALTER TABLE dokpod.agent_commands
    ALTER COLUMN payload_hash TYPE character varying(64),
    ADD CONSTRAINT ck_agent_commands_payload_hash
        CHECK (payload_hash ~ '^[0-9A-F]{64}$');
