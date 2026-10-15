#!/usr/bin/env bash
set -Eeuo pipefail
cd /opt/futsalpro
[[ "${1:?commit SHA required}" =~ ^[a-f0-9]{40}$ ]] || exit 2
# Image names and pinned registry repository are maintained in the host's protected .env.
set -a
source .env
set +a
export API_IMAGE="${API_IMAGE%:*}:$1" WEB_IMAGE="${WEB_IMAGE%:*}:$1" BACKUP_IMAGE="${BACKUP_IMAGE%:*}:$1"
docker compose --env-file .env -f deploy/docker-compose.prod.yml --profile tools run --rm backup
docker compose --env-file .env -f deploy/docker-compose.prod.yml pull api web migrate
docker compose --env-file .env -f deploy/docker-compose.prod.yml --profile tools run --rm migrate
docker compose --env-file .env -f deploy/docker-compose.prod.yml up -d api web caddy
curl --fail --retry 12 --retry-delay 5 "https://$SITE_HOST/health/ready"

# Persist the healthy release for subsequent host restarts. Keep secrets untouched.
release_file=$(mktemp .env.release.XXXXXX)
chmod 600 "$release_file"
sed '/^API_IMAGE=/d; /^WEB_IMAGE=/d; /^BACKUP_IMAGE=/d' .env > "$release_file"
printf '\nAPI_IMAGE=%s\nWEB_IMAGE=%s\nBACKUP_IMAGE=%s\n' "$API_IMAGE" "$WEB_IMAGE" "$BACKUP_IMAGE" >> "$release_file"
mv -- "$release_file" .env
