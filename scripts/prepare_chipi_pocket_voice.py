#!/usr/bin/env python3
"""Fetch the pinned, offline Pocket TTS model and its single Chipi voice."""
import concurrent.futures
import hashlib
import json
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
REPO = "FluidInference/pocket-tts-coreml"
REVISION = "c2e998e269e4d4b51f74061faddb8450cd8f9a9a"
PACK = "v4/english_18m_2026-10"
DEST = ROOT / "build" / "pocket-voice" / "models"


def download(url, output):
    subprocess.run(["curl", "--fail", "--silent", "--show-error", "--location",
                    "--retry", "2", "--max-time", "120", url, "-o", str(output)], check=True)


def needed(path):
    return (path in {"arch.json", "pocket_state.json", "constants_bin/alba.safetensors"}
            or path.startswith(("pocket_state.mlmodelc/", "mimi_decoder.mlmodelc/"))
            or (path.startswith("constants_bin/") and not path.endswith(".safetensors")))


def main():
    if (DEST / ".revision").is_file() and (DEST / ".revision").read_text() == REVISION:
        return
    DEST.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=DEST.parent) as temporary:
        stage = pathlib.Path(temporary)
        tree = stage / "tree.json"
        download(f"https://huggingface.co/api/models/{REPO}/tree/{REVISION}/{PACK}?recursive=true", tree)
        model = stage / "model"

        def fetch(entry):
            relative = entry["path"].removeprefix(PACK + "/")
            output = model / relative
            output.parent.mkdir(parents=True, exist_ok=True)
            download(f"https://huggingface.co/{REPO}/resolve/{REVISION}/{entry['path']}", output)
            data = output.read_bytes()
            expected = entry.get("lfs", {}).get("oid")
            actual = hashlib.sha256(data).hexdigest() if expected else hashlib.sha1(
                f"blob {len(data)}\0".encode() + data).hexdigest()
            if actual != (expected or entry["oid"]) or len(data) != entry["size"]:
                raise ValueError(f"Invalid model file: {relative}")

        entries = [entry for entry in json.loads(tree.read_text()) if entry["type"] == "file"
                   and needed(entry["path"].removeprefix(PACK + "/"))]
        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
            list(pool.map(fetch, entries))
        if not (model / "constants_bin/alba.safetensors").is_file():
            raise FileNotFoundError("Pocket TTS voice is missing")
        (model / ".revision").write_text(REVISION)
        if DEST.exists():
            DEST.rename(stage / "previous")
        model.rename(DEST)


if __name__ == "__main__":
    main()
