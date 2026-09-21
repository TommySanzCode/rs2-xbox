#!/bin/sh
set -eu
cd "$(dirname "$0")"
if [ "$#" -lt 1 ]; then echo 'Usage: ./setup.sh relay.example.com [public-port]'; exit 1; fi
case "${2:-7000}" in ''|*[!0-9]*) echo 'Port must be a whole number.'; exit 1;; esac
if [ "${2:-7000}" -lt 1024 ] || [ "${2:-7000}" -gt 65535 ]; then echo 'Port must be 1024–65535.'; exit 1; fi
if [ -e data/relay.rs2relay ]; then echo 'Already configured. Use admin.sh renew or rotate-token.'; exit 1; fi
umask 077
mkdir -p data
printf 'RS2_UID=%s\nRS2_GID=%s\nRS2_RELAY_PORT=%s\n' "$(id -u)" "$(id -g)" "${2:-7000}" > .env
docker compose build
docker compose run --rm --no-deps --entrypoint python relay /app/admin.py init "$1" "${2:-7000}"
echo 'Private profile: data/relay.rs2relay. Copy it securely to the world host.'
echo 'Run: docker compose up -d'
