#!/usr/bin/env python3
"""Build labeled contact sheets from generated card-art manifest entries."""

from __future__ import annotations

import argparse
import json
import math
from collections import defaultdict
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--columns", type=int, default=4)
    parser.add_argument("--image-width", type=int, default=300)
    parser.add_argument("--image-height", type=int, default=228)
    return parser.parse_args()


def load_font() -> ImageFont.ImageFont:
    try:
        return ImageFont.truetype("arial.ttf", 16)
    except OSError:
        return ImageFont.load_default()


def main() -> None:
    args = parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    groups: dict[str, list[dict]] = defaultdict(list)
    for entry in manifest["entries"]:
        if Path(entry["outputPath"]).is_file():
            groups[entry["generationGroup"]].append(entry)

    args.output_dir.mkdir(parents=True, exist_ok=True)
    label_height = 30
    padding = 10
    tile_width = args.image_width + padding * 2
    tile_height = args.image_height + label_height + padding * 2
    font = load_font()

    for group, entries in sorted(groups.items()):
        entries.sort(key=lambda entry: entry["className"])
        rows = math.ceil(len(entries) / args.columns)
        sheet = Image.new("RGB", (tile_width * args.columns, tile_height * rows), "#202124")
        draw = ImageDraw.Draw(sheet)

        for index, entry in enumerate(entries):
            row, column = divmod(index, args.columns)
            left = column * tile_width + padding
            top = row * tile_height + padding
            with Image.open(entry["outputPath"]) as source:
                thumbnail = ImageOps.contain(
                    source.convert("RGB"),
                    (args.image_width, args.image_height),
                    Image.Resampling.LANCZOS,
                )
            image_left = left + (args.image_width - thumbnail.width) // 2
            image_top = top + (args.image_height - thumbnail.height) // 2
            sheet.paste(thumbnail, (image_left, image_top))
            draw.text(
                (left, top + args.image_height + 7),
                entry["className"],
                fill="#f1f3f4",
                font=font,
            )

        output_path = args.output_dir / f"{group}.jpg"
        sheet.save(output_path, quality=92, optimize=True)
        print(f"Wrote {len(entries)} cards to {output_path}")


if __name__ == "__main__":
    main()
