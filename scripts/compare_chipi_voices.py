#!/usr/bin/env python3
"""Run Chipi's simulator-only Pocket TTS and Kokoro comparison."""
import array
import math
import os
import pathlib
import re
import shutil
import subprocess
import tempfile
import time
import wave

ROOT = pathlib.Path(__file__).resolve().parents[1]
SIMULATOR = os.environ.get("CHIPI_SIMULATOR_UDID", "C2A3AB83-E3FD-439B-AAFE-96F9F165312D")
BUNDLE = "com.chipi.prototype"
LOG_ROOT = ROOT / "build" / "pocket-voice"


def simctl(*args, env=None):
    return subprocess.check_output(["xcrun", "simctl", *args], env=env, text=True).strip()


def validate_audio(source_files, destination):
    with wave.open(str(destination), "wb") as combined:
        combined.setparams((1, 2, 24_000, 0, "NONE", "not compressed"))
        for index, source in enumerate(source_files):
            with wave.open(str(source)) as wav:
                assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (1, 2, 24_000)
                pcm = wav.readframes(wav.getnframes())
                samples = array.array("h", pcm)
                rms = math.sqrt(sum(value * value for value in samples) / len(samples)) / 32768
                assert rms > 0.01, f"Silent sample: {source}"
                if index:
                    combined.writeframes(b"\0" * 24_000)
                combined.writeframes(pcm)


def compare(engine, log_folder, container):
    stdout = log_folder / f"{engine}.log"
    stderr = log_folder / f"{engine}-native.log"
    env = {key: value for key, value in os.environ.items() if not key.startswith("SIMCTL_CHILD_CHIPI_")}
    env["SIMCTL_CHILD_CHIPI_CHECK_VOICE_COMPARISON"] = "1"
    env["SIMCTL_CHILD_CHIPI_EXPORT_NEURAL_SAMPLE"] = "1"
    if engine == "kokoro":
        env["SIMCTL_CHILD_CHIPI_COMPARE_KOKORO"] = "1"
    simctl("launch", "--terminate-running-process", f"--stdout={stdout}", f"--stderr={stderr}",
           SIMULATOR, BUNDLE, env=env)
    deadline = time.monotonic() + 100
    while time.monotonic() < deadline:
        output = stdout.read_text() if stdout.exists() else ""
        if "voice.comparison FAIL" in output:
            raise RuntimeError(f"{engine} playback failed. See {stderr}")
        if "voice.comparison PASS" in output:
            break
        time.sleep(1)
    else:
        raise TimeoutError(f"{engine} comparison did not finish. See {stdout} and {stderr}")
    samples = []
    for number in range(1, 4):
        file = log_folder / f"{engine}-{number}.wav"
        shutil.copyfile(container / "tmp" / f"ChipiVoiceSample-{number}.wav", file)
        samples.append(file)
    validate_audio(samples, log_folder / f"{engine}.wav")
    timing = re.findall(r"voice=([^ ]+) synthesisMs=(\d+) durationMs=(\d+)", stderr.read_text())
    assert len(timing) == 3, f"Expected three measured samples in {stderr}"
    print(f"{engine}: synthesis milliseconds {[int(row[1]) for row in timing]}", flush=True)


def main():
    LOG_ROOT.mkdir(parents=True, exist_ok=True)
    log_folder = pathlib.Path(tempfile.mkdtemp(prefix="comparison-", dir=LOG_ROOT))
    print(log_folder, flush=True)
    container = pathlib.Path(simctl("get_app_container", SIMULATOR, BUNDLE, "data"))
    try:
        compare("pocket", log_folder, container)
        compare("kokoro", log_folder, container)
    finally:
        simctl("launch", "--terminate-running-process", f"--stdout={log_folder / 'ready.log'}",
               f"--stderr={log_folder / 'ready-native.log'}", SIMULATOR, BUNDLE)


if __name__ == "__main__":
    main()
