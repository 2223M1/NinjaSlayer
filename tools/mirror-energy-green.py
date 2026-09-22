"""Mirror only the approved Layer4 green mask, leaving all other RGBA pixels intact."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    before = np.asarray(Image.open(args.source).convert("RGBA")).copy()
    red, green, blue, alpha = np.moveaxis(before, -1, 0)
    mask = (alpha > 0) & (green > red) & (green > blue)
    if not mask.any() or np.nonzero(mask)[1].mean() <= (before.shape[1] - 1) / 2:
        raise ValueError("Expected the original right-side green stripe; refusing to flip twice")
    layer = np.zeros_like(before)
    layer[mask] = before[mask]
    base = before.copy()
    base[mask] = 0
    after_image = Image.alpha_composite(Image.fromarray(base), Image.fromarray(layer[:, ::-1].copy()))
    after = np.asarray(after_image)
    allowed = mask | mask[:, ::-1]
    outside = int(np.count_nonzero(np.any(before != after, axis=2) & ~allowed))
    if outside:
        raise AssertionError(f"Changed {outside} pixels outside the stripe")
    original_sha = digest(args.source)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    after_image.save(args.output)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    report = dict(source_sha256=original_sha, output_sha256=digest(args.output),
                  green_pixels=int(mask.sum()), changed_pixels_outside_mask=outside,
                  source_green_center_x=float(np.nonzero(mask)[1].mean()),
                  output_green_center_x=float(before.shape[1] - 1 - np.nonzero(mask)[1].mean()))
    args.report.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
