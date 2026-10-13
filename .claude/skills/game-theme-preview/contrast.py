"""WCAG contrast utilities for Bloom game theme colors.

Reusable helper for measuring the contrast ratio between two colors as defined by
WCAG 2.x. Handles named colors (white/black), 3/6/8-digit hex, and alpha
compositing (an 8-digit hex is composited over a supplied background before the
ratio is computed, which is what a viewer actually perceives).

Thresholds worth remembering:
  * 4.5:1  - normal text (WCAG 1.4.3 AA)
  * 3.0:1  - large/bold text AND non-text UI components / graphics (WCAG 1.4.11)
"""

_NAMED = {"white": "ffffff", "black": "000000", "transparent": "00000000"}


def parse(color, bg=None):
    """Parse a CSS color to an (r, g, b) tuple in 0..255.

    If the color carries alpha (8-digit hex) and `bg` (an r,g,b tuple) is given,
    the color is alpha-composited over `bg` so the result reflects what is seen.
    """
    c = color.strip().lstrip("#").lower()
    c = _NAMED.get(c, c)
    if len(c) == 3:
        c = "".join(ch * 2 for ch in c)
    if len(c) == 8:
        r, g, b = int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16)
        a = int(c[6:8], 16) / 255
        if bg is not None:
            br, bgc, bb = bg
            r = round(r * a + br * (1 - a))
            g = round(g * a + bgc * (1 - a))
            b = round(b * a + bb * (1 - a))
        return (r, g, b)
    if len(c) != 6:
        raise ValueError(f"Cannot parse color: {color!r}")
    return (int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16))


def _lin(v):
    v = v / 255
    return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4


def luminance(rgb):
    """Relative luminance of an (r, g, b) tuple per WCAG."""
    r, g, b = rgb
    return 0.2126 * _lin(r) + 0.7152 * _lin(g) + 0.0722 * _lin(b)


def contrast(fg, bg):
    """WCAG contrast ratio (1..21) between two colors.

    `fg` and `bg` may be color strings or pre-parsed (r,g,b) tuples. A string
    foreground is composited over the (parsed) background when it has alpha.
    """
    bg_rgb = bg if isinstance(bg, tuple) else parse(bg)
    fg_rgb = fg if isinstance(fg, tuple) else parse(fg, bg_rgb)
    hi, lo = luminance(fg_rgb), luminance(bg_rgb)
    if hi < lo:
        hi, lo = lo, hi
    return (hi + 0.05) / (lo + 0.05)


def rating(ratio, threshold=3.0):
    """Short human label for a ratio against a pass threshold (default UI 3:1)."""
    if ratio >= 4.5:
        return "GOOD"
    if ratio >= threshold:
        return "OK"
    if ratio >= 2.0:
        return "WEAK"
    return "FAIL"


if __name__ == "__main__":
    import sys

    if len(sys.argv) == 3:
        r = contrast(sys.argv[1], sys.argv[2])
        print(f"{sys.argv[1]} on {sys.argv[2]}: {r:.2f}:1  ({rating(r)})")
    else:
        print("usage: py contrast.py <foreground> <background>")
