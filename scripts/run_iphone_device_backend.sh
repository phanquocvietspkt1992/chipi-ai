#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -s .env || ! -s build/iphone-device-token ]]; then
    echo "The Gemini environment and paired iPhone test token are required." >&2
    exit 1
fi
set -a
source ./.env
set +a
export CHIPI_BIND="$(ipconfig getifaddr en0)" CHIPI_PORT=8081
export CHIPI_DEVICE_TOKEN="$(cat build/iphone-device-token)"
if [[ -z "$CHIPI_BIND" || -z "$CHIPI_DEVICE_TOKEN" ]]; then
    echo "Connect the Mac to the test Wi-Fi network and retry." >&2
    exit 1
fi
exec /usr/bin/java -jar backend/target/chipi-backend-0.1.0.jar
