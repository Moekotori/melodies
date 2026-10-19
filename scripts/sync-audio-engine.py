#!/usr/bin/env python3
"""Import committed ECHOSteam native sources or verify the pinned local snapshot."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "native/AUDIO_ENGINE_UPSTREAM.json"
CMAKE = "native/audio-host/CMakeLists.txt"
OVERLAY = b"\n# C#-specific analysis target (not part of the upstream snapshot).\ninclude(cmake/EchoTrackAnalysis.cmake)\n"


def digest(data):
    return hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()


def verify(manifest):
    errors = []
    for entry in manifest["files"]:
        path = ROOT / entry["path"]
        if not path.is_file() or digest(path.read_bytes()) != entry["localSha256"]:
            errors.append(entry["path"])
    if errors:
        raise SystemExit("Local native changes require review:\n" + "\n".join(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, help="local ECHOSteam checkout; reads Git objects only")
    parser.add_argument("--revision", default="HEAD")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if args.check:
        manifest = json.loads(MANIFEST.read_text())
        verify(manifest)
        print(f"PASS {len(manifest['files'])} pinned native files at {manifest['revision']}")
        return
    if args.source is None:
        parser.error("--source is required for an import")
    if MANIFEST.exists():
        verify(json.loads(MANIFEST.read_text()))

    def git(*arguments):
        return subprocess.check_output(["git", "-C", str(args.source), *arguments])

    remote = git("remote", "get-url", "origin").decode().strip()
    if not remote.lower().removesuffix(".git").rstrip("/").endswith(("github.com/moekotori/echosteam", "github.com:moekotori/echosteam")):
        raise SystemExit("Source must be the ECHOSteam repository")
    revision = git("rev-parse", "--verify", args.revision + "^{commit}").decode().strip()
    names = git("ls-tree", "-rz", "--name-only", revision,
                "native/audio-engine", "native/audio-host").decode().split("\0")
    # Stage all Git objects before writing, so a missing object cannot leave a partial import.
    staged = []
    for name in filter(None, names):
        data = git("show", f"{revision}:{name}")
        local = data + OVERLAY if name == CMAKE else data
        staged.append((name, data, local))
    entries = []
    for name, data, local in staged:
        path = ROOT / name
        path.parent.mkdir(parents=True, exist_ok=True)
        existing = path.read_bytes() if path.exists() else None
        if existing is None or digest(existing) != digest(local):
            if existing is not None and b"\r\n" in existing and b"\0" not in local:
                local = local.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
            path.write_bytes(local)
        entries.append({"path": name, "upstreamSha256": digest(data), "localSha256": digest(local)})
    manifest = {"schemaVersion": 1, "repository": "https://github.com/moekotori/echosteam",
                "revision": revision, "hashNormalization": "CRLF to LF", "files": entries}
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Imported {len(entries)} committed native files at {revision}; existing platform binaries are unchanged")


if __name__ == "__main__":
    main()
