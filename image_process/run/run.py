#!/usr/bin/env python3
"""Wrapper for running image_process scripts from the run folder."""

import argparse
import subprocess
import sys
from pathlib import Path

from PIL import Image
from rembg import remove

ROOT = Path(__file__).resolve().parent
IMAGE_PROCESS_DIR = ROOT.parent


def find_background_removed_image(image_path: Path) -> Path | None:
    """Find a background-removed variant of the selected image if one exists."""
    base = image_path.stem
    for suffix in ["_sword", "_no_bg", "_no_bg2"]:
        if base.endswith(suffix):
            base = base[: -len(suffix)]
            break

    parent = image_path.parent
    keywords = ["_no_bg", "_sword", "_bg_removed", "_transparent"]
    candidates = []

    # Preferred names first
    candidates.extend([
        parent / f"{base}_no_bg.png",
        parent / f"{base}_sword.png",
        parent / f"{base}_sword_sword.png",
        parent / f"{base}_bg_removed.png",
        parent / f"{base}_transparent.png",
    ])

    # Also search for any same-base png containing a known keyword
    for path in sorted(parent.glob(f"{base}*.png")):
        if path == image_path:
            continue
        stem = path.stem.lower()
        if any(keyword in stem for keyword in keywords):
            candidates.append(path)

    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def resolve_image_path(image_path: Path) -> Path:
    """Resolve relative image paths against the image_process folder."""
    if image_path.exists():
        return image_path

    if not image_path.is_absolute():
        parts = image_path.parts
        if parts and parts[0].lower() == "image_process":
            candidate = IMAGE_PROCESS_DIR.joinpath(*parts[1:])
            if candidate.exists():
                return candidate
        candidate = IMAGE_PROCESS_DIR / image_path
        if candidate.exists():
            return candidate
        candidate = IMAGE_PROCESS_DIR / image_path.name
        if candidate.exists():
            return candidate

    return image_path


def save_background_removed_image(src_image: Path, dst_image: Path) -> None:
    if dst_image.exists():
        return
    print(f"Creating background-removed image: {dst_image.name}")
    img = Image.open(src_image).convert("RGB")
    result = remove(img)
    result.save(dst_image)


def run_script(script_name: str, image_path: Path) -> int:
    script_path = IMAGE_PROCESS_DIR / script_name
    if not script_path.exists():
        raise FileNotFoundError(f"Script not found: {script_path}")

    bg_removed = find_background_removed_image(image_path)
    if bg_removed and bg_removed != image_path:
        print(f"Found background-removed image for {image_path.name}: {bg_removed.name}")
        image_path = bg_removed
    else:
        bg_removed = image_path.with_name(f"{image_path.stem}_no_bg.png")
        save_background_removed_image(image_path, bg_removed)
        image_path = bg_removed

    cmd = [sys.executable, str(script_path), str(image_path)]
    print("Running:", " ".join(map(str, cmd)))
    return subprocess.run(cmd, cwd=str(IMAGE_PROCESS_DIR)).returncode


def main() -> int:
    parser = argparse.ArgumentParser(description="Run image_process scripts from the run folder.")
    parser.add_argument("command", choices=["color_extraction", "image_processing"], help="Which script to run")
    parser.add_argument("image", type=Path, help="Path to the input image file")
    args = parser.parse_args()

    resolved_image = resolve_image_path(args.image)
    if not resolved_image.exists():
        raise FileNotFoundError(f"Image not found: {args.image} (resolved to {resolved_image})")

    if args.command == "color_extraction":
        return run_script("color_extraction.py", resolved_image)
    if args.command == "image_processing":
        return run_script("image_Processing.py", resolved_image)

    parser.print_help()
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
