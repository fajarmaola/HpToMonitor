#!/usr/bin/env python3
"""Splash/logo assets for the "HP ke Monitor" desktop app (transparent mark)."""
import os
from PIL import Image, ImageDraw

GREEN = (46, 212, 122, 255)
GRAY = (139, 148, 158, 255)
APP_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def rrect(d, box, r, fill):
    d.rounded_rectangle(box, radius=r, fill=fill)


def draw_mark(size):
    S = 1024
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = S / 108.0

    def U(*v):
        return [x * u for x in v]

    rrect(d, U(20, 40, 40, 78), 6 * u, GREEN)
    rrect(d, U(23.5, 45, 36.5, 68), 3 * u, (0, 0, 0, 0))  # transparent screen cutout
    # redraw phone screen as slightly darker green so it reads on any bg
    rrect(d, U(23.5, 45, 36.5, 68), 3 * u, (18, 53, 31, 255))
    d.ellipse(U(29.2, 71, 31.2, 73), fill=GREEN)
    d.polygon([(41 * u, 55 * u), (52 * u, 55 * u), (52 * u, 51 * u), (60 * u, 59 * u),
               (52 * u, 67 * u), (52 * u, 63 * u), (41 * u, 63 * u)], fill=GREEN)
    rrect(d, U(62, 32, 90, 60), 4 * u, GREEN)
    rrect(d, U(65.5, 36, 86.5, 56), 2 * u, (18, 53, 31, 255))
    d.rectangle(U(73, 60, 79, 67), fill=GRAY)
    rrect(d, U(64, 66, 88, 70), 1.5 * u, GRAY)
    return img.resize((size, size), Image.LANCZOS)


def main():
    out = os.path.join(APP_ROOT, "windows", "SecondScreen.Desktop", "assets")
    os.makedirs(out, exist_ok=True)
    draw_mark(400).save(os.path.join(out, "logo.png"))
    print("wrote", os.path.join(out, "logo.png"))


if __name__ == "__main__":
    main()
