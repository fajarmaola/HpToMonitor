#!/usr/bin/env python3
"""Generate brand icons for "HP ke Monitor" (PT Teleraya Digital Group).

Concept: a phone on the left, an arrow, and a monitor on the right — brand
green (#2ED47A) on a dark (#0E1116) background. Produces:
  * Windows multi-size .ico  -> windows/SecondScreen.Desktop/appicon.ico
  * Android legacy mipmaps    -> android/.../mipmap-*/ic_launcher.png + _round.png
"""
import os
from PIL import Image, ImageDraw

BG = (14, 17, 22, 255)       # #0E1116
GREEN = (46, 212, 122, 255)  # #2ED47A
GRAY = (139, 148, 158, 255)  # #8B949E

APP_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def rrect(draw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)


def draw_logo(size, round_icon=False):
    """Draw the icon at the given pixel size onto an RGBA image."""
    S = 1024
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Background (rounded square or circle for the round variant).
    if round_icon:
        d.ellipse([0, 0, S, S], fill=BG)
    else:
        rrect(d, [0, 0, S, S], radius=int(S * 0.22), fill=BG)

    u = S / 108.0  # design units -> pixels (108x108 design grid)

    def U(*vals):
        return [v * u for v in vals]

    # Phone (left) — green frame with dark screen cutout.
    rrect(d, U(20, 40, 40, 78), radius=6 * u, fill=GREEN)
    rrect(d, U(23.5, 45, 36.5, 68), radius=3 * u, fill=BG)
    d.ellipse(U(29.2, 71, 31.2, 73), fill=GREEN)  # home dot

    # Arrow (middle) pointing right.
    d.polygon([
        (41 * u, 55 * u), (52 * u, 55 * u), (52 * u, 51 * u),
        (60 * u, 59 * u),
        (52 * u, 67 * u), (52 * u, 63 * u), (41 * u, 63 * u),
    ], fill=GREEN)

    # Monitor (right) — green frame + dark screen + gray stand/base.
    rrect(d, U(62, 32, 90, 60), radius=4 * u, fill=GREEN)
    rrect(d, U(65.5, 36, 86.5, 56), radius=2 * u, fill=BG)
    d.rectangle(U(73, 60, 79, 67), fill=GRAY)
    rrect(d, U(64, 66, 88, 70), radius=1.5 * u, fill=GRAY)

    return img.resize((size, size), Image.LANCZOS)


def main():
    # ---- Windows .ico (multi-resolution) ----
    ico_path = os.path.join(APP_ROOT, "windows", "SecondScreen.Desktop", "appicon.ico")
    base = draw_logo(256)
    base.save(ico_path, format="ICO",
              sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", ico_path)

    # ---- Android legacy mipmaps ----
    densities = {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}
    res = os.path.join(APP_ROOT, "android", "app", "src", "main", "res")
    for name, px in densities.items():
        folder = os.path.join(res, f"mipmap-{name}")
        os.makedirs(folder, exist_ok=True)
        draw_logo(px, round_icon=False).save(os.path.join(folder, "ic_launcher.png"))
        draw_logo(px, round_icon=True).save(os.path.join(folder, "ic_launcher_round.png"))
        print("wrote", folder)

    # ---- Play Store / general 512 preview ----
    draw_logo(512).save(os.path.join(APP_ROOT, "artifacts", "app_icon_512.png"))
    print("done")


if __name__ == "__main__":
    main()
