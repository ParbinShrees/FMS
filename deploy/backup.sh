#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
: "${BACKUP_DIR:?}" "${BACKUP_RECIPIENT:?}" "${PGDATABASE:?}" "${PGHOST:?}" "${PGUSER:?}" "${PGPASSWORD:?}"
mkdir -p "$BACKUP_DIR/daily" "$BACKUP_DIR/weekly"
tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
trap 'echo "$(date -u +%FT%TZ) backup FAILED" >&2' ERR
stamp=$(date -u +%Y%m%dT%H%M%SZ)
pg_dump --format=custom --compress=9 --no-owner --no-acl --file="$tmp/database.dump"
age --recipient "$BACKUP_RECIPIENT" --output "$tmp/database.dump.age" "$tmp/database.dump"
mv -- "$tmp/database.dump.age" "$BACKUP_DIR/daily/$stamp.dump.age"
# One weekly archive per ISO week, in addition to daily rotation.
week=$(date -u +%G-W%V)
if [[ ! -e "$BACKUP_DIR/weekly/$week.dump.age" ]]; then cp -- "$BACKUP_DIR/daily/$stamp.dump.age" "$BACKUP_DIR/weekly/$week.dump.age"; fi
# Only fixed archive directories and matching regular files are rotated.
find "$BACKUP_DIR/daily" -maxdepth 1 -type f -name '*.dump.age' -printf '%f\n' | sort -r | tail -n +15 | while IFS= read -r f; do rm -- "$BACKUP_DIR/daily/$f"; done
find "$BACKUP_DIR/weekly" -maxdepth 1 -type f -name '*.dump.age' -printf '%f\n' | sort -r | tail -n +9 | while IFS= read -r f; do rm -- "$BACKUP_DIR/weekly/$f"; done
echo "$(date -u +%FT%TZ) backup OK $stamp"
