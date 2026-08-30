from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps


ICON_SIZES = (16, 24, 32, 48, 64, 128, 256)
MASTER_SIZE = 1024
CORNER_RATIO = 0.215


def rounded_master(source_path: Path) -> Image.Image:
    with Image.open(source_path) as source:
        source = ImageOps.exif_transpose(source).convert("RGBA")
        square = ImageOps.fit(
            source,
            (MASTER_SIZE, MASTER_SIZE),
            method=Image.Resampling.LANCZOS,
            centering=(0.5, 0.5),
        )

    # Draw the mask at the master resolution so even 16 px output keeps a
    # deterministic anti-aliased edge after LANCZOS downsampling.
    mask = Image.new("L", (MASTER_SIZE, MASTER_SIZE), 0)
    draw = ImageDraw.Draw(mask)
    radius = round(MASTER_SIZE * CORNER_RATIO)
    draw.rounded_rectangle((0, 0, MASTER_SIZE - 1, MASTER_SIZE - 1), radius=radius, fill=255)
    square.putalpha(mask)
    return square


def main() -> None:
    parser = argparse.ArgumentParser(description="Create deterministic rounded PNG and multi-size ICO assets.")
    parser.add_argument("source", type=Path)
    parser.add_argument("png", type=Path)
    parser.add_argument("ico", type=Path)
    args = parser.parse_args()

    args.png.parent.mkdir(parents=True, exist_ok=True)
    args.ico.parent.mkdir(parents=True, exist_ok=True)
    master = rounded_master(args.source)
    png = master.resize((256, 256), Image.Resampling.LANCZOS)
    png.save(args.png, format="PNG", optimize=False, compress_level=9)
    master.save(args.ico, format="ICO", sizes=tuple((size, size) for size in ICON_SIZES), bitmap_format="png")

    print(f"source={args.source.resolve()}")
    print(f"png={args.png.resolve()} 256x256 RGBA")
    print(f"ico={args.ico.resolve()} sizes={','.join(map(str, ICON_SIZES))}")


if __name__ == "__main__":
    main()
