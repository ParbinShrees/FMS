-- Run with psql as postgres, passing role passwords through psql variables from a protected environment.
-- Example: psql --set=app_password="$APP_DB_PASSWORD" --set=migration_password="$MIGRATION_DB_PASSWORD" --set=backup_password="$BACKUP_DB_PASSWORD" -f roles.sql
CREATE ROLE futsal_migrate LOGIN PASSWORD :'migration_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE futsal_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE futsal_backup LOGIN PASSWORD :'backup_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CONNECT ON DATABASE futsalpro TO futsal_migrate,futsal_app,futsal_backup;
GRANT USAGE,CREATE ON SCHEMA public TO futsal_migrate;
GRANT USAGE ON SCHEMA public TO futsal_app,futsal_backup;
-- Extensions are installed once by the DBA. The app cannot install or change them.
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
ALTER DEFAULT PRIVILEGES FOR ROLE futsal_migrate IN SCHEMA public GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO futsal_app;
ALTER DEFAULT PRIVILEGES FOR ROLE futsal_migrate IN SCHEMA public GRANT USAGE,SELECT ON SEQUENCES TO futsal_app;
ALTER DEFAULT PRIVILEGES FOR ROLE futsal_migrate IN SCHEMA public GRANT SELECT ON TABLES TO futsal_backup;
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO futsal_app;
GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO futsal_app;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO futsal_backup;
