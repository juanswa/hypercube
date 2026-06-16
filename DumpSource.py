#!/usr/bin/env python3
"""Merge all C# source files in this repository into a single text file."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
from pathlib import Path

SKIP_DIR_NAMES = {
    "bin",
    "obj",
    ".git",
    ".vs",
    "node_modules",
    "artifacts",
}

DEFAULT_OUTPUT = "dump.txt"
SEPARATOR = "=" * 80


def should_skip(path: Path) -> bool:
    return any(part in SKIP_DIR_NAMES for part in path.parts)


def collect_cs_files(root: Path) -> list[Path]:
    files = [
        path
        for path in root.rglob("*.cs")
        if path.is_file() and not should_skip(path)
    ]
    return sorted(files, key=lambda p: str(p).lower())


def merge_files(root: Path, output: Path) -> int:
    files = collect_cs_files(root)
    lines: list[str] = [
        f"Hypercube C# merge",
        f"Generated: {datetime.now(timezone.utc):%Y-%m-%d %H:%M:%S UTC}",
        f"Root: {root.resolve()}",
        f"Files: {len(files)}",
        "",
    ]

    for file_path in files:
        relative = file_path.relative_to(root)
        content = file_path.read_text(encoding="utf-8").rstrip()
        lines.extend(
            [
                SEPARATOR,
                f"FILE: {relative.as_posix()}",
                SEPARATOR,
                content,
                "",
            ]
        )

    output.write_text("\n".join(lines), encoding="utf-8")
    return len(files)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Merge all C# files into one text file."
    )
    parser.add_argument(
        "-o",
        "--output",
        default=DEFAULT_OUTPUT,
        help=f"Output file path (default: {DEFAULT_OUTPUT})",
    )
    parser.add_argument(
        "--root",
        default=".",
        help="Repository root to scan (default: current directory)",
    )
    args = parser.parse_args()

    root = Path(args.root).resolve()
    output = Path(args.output)
    if not output.is_absolute():
        output = root / output

    count = merge_files(root, output)
    print(f"Merged {count} C# files into {output}")


if __name__ == "__main__":
    main()
