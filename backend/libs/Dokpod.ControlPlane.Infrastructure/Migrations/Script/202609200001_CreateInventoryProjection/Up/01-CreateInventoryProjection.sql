CREATE TABLE dokpod.inventory_projections (
    environment_id uuid NOT NULL CONSTRAINT pk_inventory_projections PRIMARY KEY,
    revision bigint NOT NULL,
    observed_at_utc timestamp with time zone NOT NULL
);

CREATE TABLE dokpod.inventory_containers (
    environment_id uuid NOT NULL,
    container_id character varying(128) NOT NULL,
    name character varying(255) NOT NULL,
    image_reference character varying(512) NOT NULL,
    state character varying(32) NOT NULL,
    revision character varying(255) NOT NULL,
    observed_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT pk_inventory_containers PRIMARY KEY (environment_id, container_id),
    CONSTRAINT fk_inventory_containers_projections FOREIGN KEY (environment_id)
        REFERENCES dokpod.inventory_projections (environment_id) ON DELETE CASCADE
);

GRANT SELECT, INSERT, UPDATE, DELETE ON
    dokpod.inventory_projections,
    dokpod.inventory_containers
    TO dokpod_runtime;
