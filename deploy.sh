#!/bin/sh
set -eu

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 api|frontend <image-tag>" >&2
  exit 2
fi

component=$1
image_tag=$2
case "$component" in
  api) service=api; tag_key=BACKEND_IMAGE_TAG ;;
  frontend) service=frontend; tag_key=FRONTEND_IMAGE_TAG ;;
  *) echo "Unknown component: $component" >&2; exit 2 ;;
esac

case "$image_tag" in
  *[!a-f0-9]*|'') echo "Image tag must be a commit SHA (hexadecimal)." >&2; exit 2 ;;
esac
[ "${#image_tag}" -eq 40 ] || { echo "Image tag must be a full 40-character commit SHA." >&2; exit 2; }

cd "$(dirname "$0")"
[ -f .env ] || { echo "Missing $PWD/.env; create it before deployment." >&2; exit 1; }
[ -f docker-compose.prod.yml ] || { echo "Missing docker-compose.prod.yml." >&2; exit 1; }
[ -f nginx.conf.template ] || { echo "Missing nginx.conf.template." >&2; exit 1; }

# Serialize deploys triggered by the independent frontend/backend repositories.
exec 9>"$PWD/.deploy.lock"
flock -w 300 9 || { echo "Timed out waiting for another deployment." >&2; exit 1; }

tmp_env=$(mktemp "$PWD/.env.XXXXXX")
trap 'rm -f "$tmp_env"' EXIT HUP INT TERM
awk -F= -v key="$tag_key" -v value="$image_tag" '
  $1 == key { print key "=" value; found = 1; next }
  { print }
  END { if (!found) print key "=" value }
' .env > "$tmp_env"
chmod 600 "$tmp_env"
mv "$tmp_env" .env
trap - EXIT HUP INT TERM

export COMPOSE_PROJECT_NAME=lms

docker compose --env-file .env -f docker-compose.prod.yml config --quiet
docker compose --env-file .env -f docker-compose.prod.yml pull "$service"
if [ "$component" = api ]; then
  docker compose --env-file .env -f docker-compose.prod.yml up -d api
else
  docker compose --env-file .env -f docker-compose.prod.yml up -d frontend nginx
fi

docker compose --env-file .env -f docker-compose.prod.yml ps
