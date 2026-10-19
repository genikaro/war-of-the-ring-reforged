"""Verify or restore cataloged original-game research copies, never the install.

The manifest is relative to the repository root. Required source files can be
restored; optional contact sheets/text/previews are local browsing aids.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
MEDIA_ROOT = ROOT / "converted/references/original-game"
CATALOG = ROOT / "references/original-game/catalog.json"


def media_path(record: dict) -> Path:
    path = (ROOT / record["path"]).resolve()
    if not path.is_relative_to(MEDIA_ROOT.resolve()):
        raise ValueError(f"Reference path outside media folder: {record['path']}")
    return path


def validate(data: bytes, record: dict) -> None:
    if len(data) != record["bytes"]:
        raise ValueError(f"{record['id']}: file size differs from catalog")
    if hashlib.sha256(data).hexdigest() != record["sha256"]:
        raise ValueError(f"{record['id']}: SHA-256 differs from catalog")


def verify(records: list[dict]) -> int:
    checked = required = optional_missing = 0
    errors = []
    for record in records:
        try:
            path = media_path(record)
            if record.get("optional") and not path.exists():
                optional_missing += 1
                continue
            validate(path.read_bytes(), record)
            checked += 1
            required += not record.get("optional", False)
        except (OSError, ValueError) as error:
            errors.append(f"{record['id']}: {error}")
    print(f"Verified {checked} files ({required} required); {optional_missing} optional aids absent.")
    for error in errors:
        print(error, file=sys.stderr)
    return bool(errors)


def restore(records: list[dict], sevenzip: str | None) -> int:
    by_id = {record["id"]: record for record in records}
    restored = 0
    # Archive sources are restored before their members, independent of order.
    ordered = sorted(records, key=lambda record: "archive_member" in record)
    for record in ordered:
        if record.get("optional"):
            continue
        dest = media_path(record)
        if dest.exists():
            validate(dest.read_bytes(), record)
            continue
        if "download_url" in record:
            request = urllib.request.Request(record["download_url"], headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(request, timeout=40) as response:
                data = response.read(record["bytes"] + 1)
        elif "local_source" in record:
            source = (ROOT / record["local_source"]).resolve()
            if not source.is_relative_to((ROOT / "TLOTR - War of the Ring").resolve()):
                raise ValueError("Local source must be inside the read-only game install")
            data = source.read_bytes()
        elif "archive_member" in record:
            if not sevenzip:
                raise ValueError("7-Zip is required for editor help; pass --sevenzip PATH")
            archive = by_id[record["archive_id"]]
            archive_path = media_path(archive)
            validate(archive_path.read_bytes(), archive)
            result = subprocess.run([sevenzip, "x", "-so", str(archive_path), record["archive_member"]],
                                    capture_output=True, check=True)
            data = result.stdout
        else:
            raise ValueError(f"No restoration source for {record['id']}")
        # Verify before writing. Changed upstream files never replace a snapshot.
        validate(data, record)
        dest.parent.mkdir(parents=True, exist_ok=True)
        with dest.open("xb") as stream:
            stream.write(data)
        restored += 1
        print(f"Restored {record['id']}")
    print(f"Restored {restored} missing source files; existing files preserved.")
    return verify(records)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["verify", "restore"])
    parser.add_argument("--sevenzip", default=shutil.which("7z") or (
        "C:/Program Files/7-Zip/7z.exe" if Path("C:/Program Files/7-Zip/7z.exe").exists() else None))
    args = parser.parse_args()
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    records = catalog["files"]
    try:
        return verify(records) if args.action == "verify" else restore(records, args.sevenzip)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"Reference operation stopped: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
