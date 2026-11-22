#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
simulator="${CHIPI_SIMULATOR_UDID:-C2A3AB83-E3FD-439B-AAFE-96F9F165312D}"
unity="${CHIPI_UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
mkdir -p build
rebuild=false
for option in "$@"; do
    case "$option" in
        --build) rebuild=true ;;
        *) printf 'Unknown option: %s\n' "$option" >&2; exit 2 ;;
    esac
done
if "$rebuild"; then
    python3 scripts/prepare_chipi_pocket_voice.py
    bash scripts/prepare_chipi_neural_voice.sh
    CHIPI_IOS_SIMULATOR=1 "$unity" -batchmode -quit -projectPath "$PWD/Unity" \
        -buildTarget iOS -executeMethod ChipiBuild.IOS -logFile "$PWD/build/unity-phase1.log"
    xcodebuild -project build/Chipi-iOS-Simulator/Unity-iPhone.xcodeproj \
        -scheme Unity-iPhone -configuration Release -destination 'generic/platform=iOS Simulator' \
        -derivedDataPath "$PWD/build/XcodeSimulator-live" CODE_SIGNING_ALLOWED=NO \
        COMPILER_INDEX_STORE_ENABLE=NO build > build/xcode-phase1.log 2>&1
fi
app="$PWD/build/XcodeSimulator-live/Build/Products/Release-iphonesimulator/Chipi.app"
if [[ ! -d "$app" ]]; then
    printf 'Run again with --build to create the app.\n' >&2
    exit 1
fi
xcrun simctl boot "$simulator" 2>/dev/null || true
xcrun simctl bootstatus "$simulator" -b
xcrun simctl install "$simulator" "$app"
xcrun simctl launch --terminate-running-process --stdout="$PWD/build/phase1-simulator.log" "$simulator" com.chipi.prototype
