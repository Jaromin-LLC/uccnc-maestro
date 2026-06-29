#!/usr/bin/env python3
"""Generate PWA icons from the Maestro logo (app/icons/mark.png).

Produces standard "any" icons (transparent), a maskable icon (logo centered in
the brand-colored safe zone so Android's mask never clips it), and an Apple
touch icon (opaque background, since iOS ignores transparency and rounds the
corners itself).
"""
import os
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..")
ICONS = os.path.join(ROOT, "app", "icons")
SRC = os.path.join(ICONS, "mark.png")

BRAND_BG = (14, 17, 22, 255)  # #0e1116 - matches manifest background_color


def fit(img, box):
    """Return a copy of img scaled to fit within a (box x box) square."""
    src = img.copy()
    src.thumbnail((box, box), Image.LANCZOS)
    return src


def centered(size, content_box, bg):
    """size x size canvas (bg fill) with the logo scaled to content_box, centered."""
    canvas = Image.new("RGBA", (size, size), bg)
    logo = fit(Image.open(SRC).convert("RGBA"), content_box)
    canvas.alpha_composite(logo, ((size - logo.width) // 2, (size - logo.height) // 2))
    return canvas


def transparent(size):
    return centered(size, size, (0, 0, 0, 0))


def main():
    # Plain "any" icons - transparent, logo fills the tile.
    transparent(192).save(os.path.join(ICONS, "icon-192.png"))
    transparent(512).save(os.path.join(ICONS, "icon-512.png"))

    # Maskable - logo confined to the ~80% safe zone over the brand background.
    centered(512, int(512 * 0.8), BRAND_BG).save(os.path.join(ICONS, "maskable-512.png"))

    # Apple touch icon - opaque background, no transparency.
    centered(180, 180, BRAND_BG).save(os.path.join(ICONS, "apple-touch-icon.png"))

    print("Generated:", ", ".join(sorted(
        f for f in os.listdir(ICONS) if f.endswith(".png"))))


if __name__ == "__main__":
    main()
