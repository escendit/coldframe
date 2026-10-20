#!/bin/sh
# The roles and databases of the charts' `database` release, for the Compose reference example
# (deploy/compose/README.md). The postgres image runs this once, on the first start with an empty
# volume; a changed password in .env afterwards needs ALTER ROLE by hand.
#
#   coldframe owns coldframe; temporal owns temporal and temporal_visibility; keycloak owns keycloak.
set -eu

psql --variable ON_ERROR_STOP=1 --username "${POSTGRES_USER:-postgres}" --dbname postgres \
  --set coldframe_password="${COLDFRAME_DB_PASSWORD}" \
  --set temporal_password="${TEMPORAL_DB_PASSWORD}" \
  --set keycloak_password="${KEYCLOAK_DB_PASSWORD}" <<'SQL'
CREATE ROLE coldframe LOGIN PASSWORD :'coldframe_password';
CREATE DATABASE coldframe OWNER coldframe;
CREATE ROLE temporal LOGIN PASSWORD :'temporal_password';
CREATE DATABASE temporal OWNER temporal;
CREATE DATABASE temporal_visibility OWNER temporal;
CREATE ROLE keycloak LOGIN PASSWORD :'keycloak_password';
CREATE DATABASE keycloak OWNER keycloak;
SQL
