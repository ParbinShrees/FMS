#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
: "${BACKUP_IDENTITY:?}" "${PGHOST:?}" "${PGUSER:?}" "${PGPASSWORD:?}"
archive=${1:?Usage: restore.sh archive.dump.age scratch_database}
target=${2:?Scratch database name required}
# Never restore over an existing or production database.
[[ "$target" =~ ^futsal_restore_[a-z0-9_]+$ ]] || { echo 'Target must start with futsal_restore_' >&2; exit 2; }
tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
trap 'echo "$(date -u +%FT%TZ) restore FAILED" >&2' ERR
printf '%s\n' "$BACKUP_IDENTITY" > "$tmp/identity"
age --decrypt --identity "$tmp/identity" --output "$tmp/database.dump" "$archive"
createdb "$target"
pg_restore --dbname="$target" --no-owner --no-acl --exit-on-error "$tmp/database.dump"
psql --dbname="$target" --set=ON_ERROR_STOP=1 --file="$(dirname "$0")/verify-restore.sql"
echo "$(date -u +%FT%TZ) restore OK $target"
