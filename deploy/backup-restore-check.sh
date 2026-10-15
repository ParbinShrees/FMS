#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
: "${PGDATABASE:?}" "${BACKUP_DIR:?}" "${BACKUP_RECIPIENT:?}" "${BACKUP_IDENTITY:?}"
target="futsal_restore_$(date -u +%s)_${RANDOM}"
trap 'dropdb --if-exists "$target" >/dev/null' EXIT
before=$(psql -Atc "SELECT (SELECT count(*) FROM organizations)||':'||(SELECT count(*) FROM bookings)||':'||(SELECT count(*) FROM payments)||':'||(SELECT count(*) FROM customers)||':'||(SELECT coalesce(sum(amount),0) FROM payments)")
bash "$(dirname "$0")/backup.sh"
archive=$(find "$BACKUP_DIR/daily" -maxdepth 1 -type f -name '*.dump.age' | sort | tail -n 1)
bash "$(dirname "$0")/restore.sh" "$archive" "$target"
after=$(psql --dbname="$target" -Atc "SELECT (SELECT count(*) FROM organizations)||':'||(SELECT count(*) FROM bookings)||':'||(SELECT count(*) FROM payments)||':'||(SELECT count(*) FROM customers)||':'||(SELECT coalesce(sum(amount),0) FROM payments)")
[[ "$before" == "$after" ]] || { echo 'Restore counts/totals differ' >&2; exit 1; }
echo 'Backup/restore row counts and signed payment totals match.'
