"""Deterministically extract Astellar's six PSD card-frame layers.

The reference checkout is read-only.  Generated PNGs are build-time assets for
the .NET application, so the shipped application does not require Python or
psd-tools.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image
from psd_tools import PSDImage


FRAME_NAMES = {
    "card_frame00": "Normal",
    "card_frame01": "Effect",
    "card_frame02": "Ritual",
    "card_frame03": "Fusion",
    "card_frame07": "Spell",
    "card_frame08": "Trap",
    "card_frame09": "Token",
    "card_frame10": "Synchro",
    "card_frame12": "Xyz",
    "card_frame13": "PendulumNormal",
    "card_frame14": "PendulumEffect",
    "card_frame15": "PendulumXyz",
    "card_frame16": "PendulumSynchro",
    "card_frame17": "PendulumFusion",
    "card_frame18": "Link",
    "card_frame19": "PendulumRitual",
}

LAYERS = ("PeriFrame", "NameBox", "ArtFrame", "EffFrame", "EffBox", "BackGround")
EXPECTED_SIZE = (704, 1024)


def extract(reference: Path, output: Path) -> None:
    output.mkdir(parents=True, exist_ok=True)
    for key, source_name in FRAME_NAMES.items():
        source = reference / f"{source_name}.psd"
        if not source.is_file():
            raise FileNotFoundError(source)

        psd = PSDImage.open(source)
        if tuple(psd.size) != EXPECTED_SIZE:
            raise ValueError(f"{source.name}: expected {EXPECTED_SIZE}, got {psd.size}")

        found: dict[str, object] = {}
        for layer in psd.descendants():
            if not layer.is_group() and layer.name.strip() in LAYERS:
                found[layer.name.strip()] = layer
        missing = sorted(set(LAYERS) - set(found))
        if missing:
            raise ValueError(f"{source.name}: missing layers: {', '.join(missing)}")

        target = output / key
        target.mkdir(parents=True, exist_ok=True)
        for layer_name in LAYERS:
            layer = found[layer_name]
            composite = layer.composite().convert("RGBA")
            canvas = Image.new("RGBA", EXPECTED_SIZE, (0, 0, 0, 0))
            # Preserve the PSD layer's straight alpha verbatim. Passing the image
            # again as a paste mask multiplies semi-transparent edge alpha twice.
            canvas.paste(composite, tuple(layer.offset))
            canvas.save(target / f"{layer_name}.png", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("reference", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    extract(args.reference.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
