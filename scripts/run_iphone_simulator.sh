#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
simulator="${CHIPI_SIMULATOR_UDID:-F00BA7A6-BBDF-4C1D-960C-CD299A39D614}"
unity="${CHIPI_UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity}"
mkdir -p build
rebuild=false
restart_backend=false
for option in "$@"; do
    case "$option" in
        --build) rebuild=true; restart_backend=true ;;
        --restart-backend) restart_backend=true ;;
        *) printf 'Unknown option: %s\n' "$option" >&2; exit 2 ;;
    esac
done
if "$rebuild"; then
    mvn -B -f backend/pom.xml package
    CHIPI_IOS_SIMULATOR=1 "$unity" -batchmode -quit -projectPath "$PWD/Unity" \
        -buildTarget iOS -executeMethod ChipiBuild.IOS -logFile "$PWD/build/unity-phase1.log"
    xcodebuild -project build/Chipi-iOS-Simulator/Unity-iPhone.xcodeproj \
        -scheme Unity-iPhone -configuration Release -destination 'generic/platform=iOS Simulator' \
        -derivedDataPath "$PWD/build/XcodeSimulator-live" CODE_SIGNING_ALLOWED=NO \
        COMPILER_INDEX_STORE_ENABLE=NO build > build/xcode-phase1.log 2>&1
fi
app="$PWD/build/XcodeSimulator-live/Build/Products/Release-iphonesimulator/Chipi.app"
jar="$PWD/backend/target/chipi-backend-0.1.0.jar"
if [[ ! -d "$app" || ! -f "$jar" ]]; then
    printf 'Run again with --build to create the app and backend.\n' >&2
    exit 1
fi
(
    # Keep backend credentials out of the Unity and Simulator processes.
    if [[ -f .env ]]; then
        set -a
        source ./.env
        set +a
    fi
    expected_mode=demo
    if [[ "${CHIPI_VOICE_PROVIDER:-openai}" == gemini ]]; then
        expected_mode=gemini
    elif [[ "${CHIPI_DEMO:-true}" == false ]]; then
        expected_mode=live
    fi
    if "$restart_backend"; then
        old_pid=$(lsof -nP -tiTCP@127.0.0.1:8080 -sTCP:LISTEN || true)
        if [[ -n "$old_pid" ]]; then
            command=$(ps -p "$old_pid" -o command=)
            case "$command" in
                *"-jar $jar"|*"-jar $PWD/build/backend-runtime/chipi-"*.jar|*"-jar build/backend-runtime/chipi-"*.jar) kill "$old_pid" ;;
                *) printf 'A different process owns port 8080. Backend was left running.\n' >&2; exit 1 ;;
            esac
            for attempt in {1..12}; do
                if ! lsof -nP -tiTCP@127.0.0.1:8080 -sTCP:LISTEN >/dev/null; then break; fi
                sleep 1
            done
        fi
    fi
    if ! curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1; then
        mkdir -p build/backend-runtime
        digest=$(shasum -a 256 "$jar" | cut -d ' ' -f 1)
        runtime_jar="$PWD/build/backend-runtime/chipi-$digest.jar"
        if [[ ! -f "$runtime_jar" ]]; then cp "$jar" "$runtime_jar"; fi
        nohup env CHIPI_BIND=127.0.0.1 CHIPI_PORT=8080 java -jar "$runtime_jar" > build/phase1-backend.log 2>&1 < /dev/null &
        backend_pid=$!
        for attempt in {1..20}; do
            if curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1; then break; fi
            if ! kill -0 "$backend_pid" 2>/dev/null; then
                tail -20 build/phase1-backend.log >&2
                exit 1
            fi
            sleep 1
        done
    fi
    health=$(curl -fsS http://127.0.0.1:8080/health)
    if [[ "$health" != *"\"mode\":\"$expected_mode\""* ]]; then
        printf 'The backend on port 8080 is running in a different mode. Stop it, then rerun this script.\n' >&2
        exit 1
    fi
    printf '%s\n' "$health"
)
xcrun simctl boot "$simulator" 2>/dev/null || true
xcrun simctl bootstatus "$simulator" -b
xcrun simctl install "$simulator" "$app"
xcrun simctl launch --terminate-running-process --stdout="$PWD/build/phase1-simulator.log" "$simulator" com.chipi.prototype
