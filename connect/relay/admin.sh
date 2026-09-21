#!/bin/sh
set -eu
cd "$(dirname "$0")"
case "${1:-}" in
  renew|rotate-token) ;;
  *) echo 'Usage: ./admin.sh renew | rotate-token'; exit 1;;
esac
docker compose stop
docker compose run --rm --no-deps --entrypoint python relay /app/admin.py "$1"
docker compose up -d
