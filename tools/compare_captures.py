"""Compare two game captures pixel by pixel (build-157).

Presentation optimizations must not change what is drawn: the stress benchmark's phase A and B captures of
two builds should report 0 differing pixels outside the ignored rectangles (the top-right build/date/FPS line
always differs). Exit code 0 when identical, 1 otherwise.

    python tools/compare_captures.py before.png after.png --ignore 1180,0,1600,40 [--diff diff.png]
"""

import argparse
import sys

from PIL import Image, ImageChops


def parse_rect(text):
    x0, y0, x1, y1 = (int(v) for v in text.split(","))
    if x1 <= x0 or y1 <= y0:
        raise argparse.ArgumentTypeError(f"empty rectangle {text}")
    return x0, y0, x1, y1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("before")
    parser.add_argument("after")
    parser.add_argument("--ignore", type=parse_rect, action="append", default=[],
                        help="x0,y0,x1,y1 rectangle to leave out (repeatable)")
    parser.add_argument("--diff", help="write an image marking the differing pixels")
    args = parser.parse_args()

    before = Image.open(args.before).convert("RGB")
    after = Image.open(args.after).convert("RGB")
    if before.size != after.size:
        print(f"SIZE_MISMATCH {before.size} {after.size}")
        return 1
    for rect in args.ignore:
        before.paste((0, 0, 0), rect)
        after.paste((0, 0, 0), rect)
    difference = ImageChops.difference(before, after)
    mask = difference.convert("L").point(lambda v: 255 if v else 0)
    changed = mask.histogram()[255]
    box = mask.getbbox()
    peak = max(band[1] for band in difference.getextrema())
    total = before.size[0] * before.size[1]
    print(f"DIFFERING_PIXELS {changed} of {total} ({100.0 * changed / total:.4f}%) max_channel_delta {peak} bbox {box}")
    if args.diff and changed:
        marked = after.copy()
        marked.paste((255, 0, 255), mask=mask)
        marked.save(args.diff)
    return 0 if changed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
