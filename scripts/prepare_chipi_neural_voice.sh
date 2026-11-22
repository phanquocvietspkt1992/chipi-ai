#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

cache="$PWD/build/neural-voice"
release=models-2026-03-23
models="$cache/$release"
digest=0d24bbf80526456bd8640f9f484cd1c333aee8a936e618ac44e619eafb79aec9
if [[ -f "$models/.chipi-model-release" && "$(cat "$models/.chipi-model-release")" == "$release" &&
      -s "$models/kokoro_frontend.mlmodelc/coremldata.bin" &&
      -s "$models/kokoro_backend.mlmodelc/coremldata.bin" &&
      -s "$models/voices/af_heart.bin" ]]; then
    exit 0
fi

mkdir -p "$cache"
staging=$(mktemp -d "$cache/prepare.XXXXXX")
trap 'rm -rf "$staging"' EXIT
archive="$cache/models.tar.gz"
if [[ ! -f "$archive" ]]; then
    curl --fail --location --retry 2 --silent --show-error \
        "https://github.com/Jud/kokoro-coreml/releases/download/$release/kokoro-models.tar.gz" \
        --output "$staging/models.tar.gz"
    archive="$staging/models.tar.gz"
fi
printf '%s  %s\n' "$digest" "$archive" | shasum -a 256 --check
mkdir -p "$staging/model/voices"
tar -xzf "$archive" -C "$staging/model" kokoro_frontend.mlmodelc kokoro_backend.mlmodelc voices/af_heart.bin
printf '%s\n' "$release" > "$staging/model/.chipi-model-release"
if [[ -d "$models" ]]; then
    mv "$models" "$staging/previous"
fi
mv "$staging/model" "$models"
