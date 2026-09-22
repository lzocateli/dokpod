CREATE TABLE environments (
    environment_id uuid NOT NULL CONSTRAINT pk_environments PRIMARY KEY,
    name character varying(128) NOT NULL,
    host character varying(255) NOT NULL,
    enabled boolean NOT NULL,
    scopes character varying(2048) NOT NULL,
    created_at_utc timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX ux_environments_name ON environments (name);
