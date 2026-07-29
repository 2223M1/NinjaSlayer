from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageFilter, ImageOps, ImageStat


def pixel_data(image: Image.Image):
    if hasattr(image, "get_flattened_data"):
        return image.get_flattened_data()
    return image.getdata()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Normalize and inspect generated NinjaSlayer card art.")
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--width", required=True, type=int)
    parser.add_argument("--height", required=True, type=int)
    parser.add_argument("--reference", required=True, type=Path)
    parser.add_argument("--comparison", required=True, type=Path)
    parser.add_argument("--qa-output", required=True, type=Path)
    parser.add_argument("--centering-x", type=float, default=0.5)
    parser.add_argument("--centering-y", type=float, default=0.5)
    return parser.parse_args()


def difference_hash(image: Image.Image) -> str:
    grayscale = image.convert("L").resize((17, 16), Image.Resampling.LANCZOS)
    pixels = list(pixel_data(grayscale))
    bits = []
    for row in range(16):
        offset = row * 17
        bits.extend(pixels[offset + column] > pixels[offset + column + 1] for column in range(16))
    value = 0
    for bit in bits:
        value = (value << 1) | int(bit)
    return f"{value:064x}"


def hue_distribution(image: Image.Image) -> list[dict[str, float | int]]:
    sampled = image.resize((128, 128), Image.Resampling.BILINEAR).convert("HSV")
    bins = [0] * 12
    colorful = 0
    for hue, saturation, value in pixel_data(sampled):
        if saturation < 70 or value < 35:
            continue
        bins[min(11, hue * 12 // 256)] += 1
        colorful += 1
    if colorful == 0:
        return []
    return [
        {"bin": index, "fractionOfColorfulPixels": round(count / colorful, 4)}
        for index, count in sorted(enumerate(bins), key=lambda item: item[1], reverse=True)
        if count
    ][:4]


def edge_density(image: Image.Image) -> float:
    sample = image.convert("L").resize((256, 256), Image.Resampling.BILINEAR)
    edges = sample.filter(ImageFilter.FIND_EDGES)
    return round(sum(value >= 48 for value in pixel_data(edges)) / (256 * 256), 4)


def image_metrics(image: Image.Image) -> dict[str, object]:
    luminance = image.convert("L")
    stats = ImageStat.Stat(luminance)
    return {
        "size": list(image.size),
        "mode": image.mode,
        "luminanceMean": round(stats.mean[0], 2),
        "luminanceStdDev": round(stats.stddev[0], 2),
        "edgeDensity": edge_density(image),
        "dominantHueBins": hue_distribution(image),
        "differenceHash": difference_hash(image),
    }


def main() -> None:
    args = parse_args()
    if not 0 <= args.centering_x <= 1 or not 0 <= args.centering_y <= 1:
        raise SystemExit("Centering values must be between 0 and 1.")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.comparison.parent.mkdir(parents=True, exist_ok=True)
    args.qa_output.parent.mkdir(parents=True, exist_ok=True)

    target_size = (args.width, args.height)
    with Image.open(args.input) as source:
        normalized = ImageOps.fit(
            source.convert("RGB"),
            target_size,
            Image.Resampling.LANCZOS,
            centering=(args.centering_x, args.centering_y),
        )
        normalized.save(args.output, "PNG", optimize=True)

    with Image.open(args.reference) as reference:
        normalized_reference = ImageOps.fit(
            reference.convert("RGB"),
            target_size,
            Image.Resampling.LANCZOS,
        )

    gap = 24
    comparison = Image.new("RGB", (args.width * 2 + gap, args.height), (24, 24, 24))
    comparison.paste(normalized_reference, (0, 0))
    comparison.paste(normalized, (args.width + gap, 0))
    comparison.save(args.comparison, "PNG", optimize=True)

    qa = {
        "source": str(args.input.resolve()),
        "output": str(args.output.resolve()),
        "reference": str(args.reference.resolve()),
        "comparison": str(args.comparison.resolve()),
        "generated": image_metrics(normalized),
        "referenceMetrics": image_metrics(normalized_reference),
        "selfReview": {
            "status": "pending",
            "checks": {
                "economicalStyleMatch": None,
                "edgeTreatment": None,
                "dominantHueSeparation": None,
                "anatomyAndCrop": None,
                "objectCount": None,
                "visibleMenpo": None,
                "menpoEngraving": "advisory-not-gated",
                "forbiddenContent": None,
            },
            "notes": [],
        },
    }
    args.qa_output.write_text(f"{json.dumps(qa, ensure_ascii=False, indent=2)}\n", encoding="utf-8")
    print(json.dumps(qa, ensure_ascii=False))


if __name__ == "__main__":
    main()
