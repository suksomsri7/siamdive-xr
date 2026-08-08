#!/usr/bin/env python3
"""
Measure the symptom instead of guessing at it.

The user has reported "ปลาหมุน/ส่ายหัวไปมาถี่ๆ" ten times and every fix has been aimed at a
mechanism nobody measured. This reads the user's OWN screen recording and turns the complaint
into two numbers per fish: how many DEGREES the body swings, and at what FREQUENCY.

Any candidate cause then has to match those numbers or it is not the cause.

Method
------
  * segment fish from water by saturation (the water is a saturated blue, the barracuda are
    near-grey) — no model, no training, and it fails loudly rather than quietly
  * track one fish frame to frame by nearest centroid
  * body axis per frame = principal axis of the blob (image moments)
  * subtract the MEAN angle of all tracked fish each frame, which removes camera roll —
    what is left is the fish moving relative to its own school
  * high-pass at 0.2 s: swimming is slow, shaking is not

Usage:  python3 measure_video_shake.py /tmp/shake            # dir of PNG frames, in order
"""

import sys
import glob
import math

import numpy as np
from PIL import Image


def load(path):
    im = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    mx = im.max(-1)
    mn = im.min(-1)
    sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)
    # water = saturated blue; fish bodies = pale grey/white. Value guards against the dark wreck.
    return (sat < 0.30) & (mx > 0.45), im


def blobs(mask, min_px=900, max_px=60000):
    from scipy import ndimage
    lab, n = ndimage.label(mask)
    if n == 0:
        return []
    sizes = ndimage.sum(mask, lab, range(1, n + 1))
    out = []
    for i, s in enumerate(sizes, start=1):
        if not (min_px <= s <= max_px):
            continue
        ys, xs = np.where(lab == i)
        cx, cy = xs.mean(), ys.mean()
        x, y = xs - cx, ys - cy
        u20, u02, u11 = (x * x).mean(), (y * y).mean(), (x * y).mean()
        ang = 0.5 * math.atan2(2 * u11, u20 - u02)          # principal axis, ±90°
        elong = math.sqrt(max(u20 + u02 + math.hypot(2 * u11, u20 - u02), 1e-9) /
                          max(u20 + u02 - math.hypot(2 * u11, u20 - u02), 1e-9))
        out.append(dict(cx=cx, cy=cy, ang=ang, size=s, elong=elong))
    return out


def main():
    d = sys.argv[1] if len(sys.argv) > 1 else "/tmp/shake"
    files = sorted(glob.glob(f"{d}/*.png"))
    if not files:
        raise SystemExit(f"no frames in {d}")
    fps = float(sys.argv[2]) if len(sys.argv) > 2 else 60.0

    per_frame = []
    for f in files:
        mask, _ = load(f)
        bl = [b for b in blobs(mask) if b["elong"] > 2.2]     # long thin = a barracuda, not a rock
        per_frame.append(bl)
    print(f"{len(files)} frames, mean {np.mean([len(b) for b in per_frame]):.1f} fish/frame")

    # track: greedy nearest-centroid chains started from the first frame
    tracks = [[b] for b in per_frame[0]]
    for fr in per_frame[1:]:
        used = set()
        for tr in tracks:
            if tr[-1] is None:
                tr.append(None)
                continue
            last = tr[-1]
            best, bd = None, 1e9
            for j, b in enumerate(fr):
                if j in used:
                    continue
                dd = math.hypot(b["cx"] - last["cx"], b["cy"] - last["cy"])
                if dd < bd:
                    best, bd = j, dd
            if best is not None and bd < 45 and abs(fr[best]["size"] - last["size"]) < last["size"] * 0.6:
                used.add(best)
                tr.append(fr[best])
            else:
                tr.append(None)

    full = [tr for tr in tracks if all(b is not None for b in tr)]
    print(f"{len(full)} fish tracked through every frame")
    if not full:
        raise SystemExit("tracking failed — tighten the crop or shorten the clip")

    ang = np.array([[math.degrees(b["ang"]) for b in tr] for tr in full]).T   # [frame, fish]
    ang = np.unwrap(ang * 2, axis=0) / 2      # the axis is mod 180°, so unwrap on the double angle
    ang -= ang.mean(1, keepdims=True)         # remove camera roll / global swing

    w = max(3, int(0.2 * fps) | 1)
    k = np.ones(w) / w
    pad = np.pad(ang, ((w // 2, w // 2), (0, 0)), mode="edge")
    smooth = np.stack([np.convolve(pad[:, i], k, mode="valid")[:ang.shape[0]]
                       for i in range(ang.shape[1])], -1)
    hp = ang - smooth

    rms = np.sqrt(np.mean(hp ** 2, 0))
    spec = np.abs(np.fft.rfft(hp * np.hanning(len(hp))[:, None], axis=0)) ** 2
    freq = np.fft.rfftfreq(len(hp), 1.0 / fps)
    peak = freq[1:][np.argmax(spec[1:], 0)]

    print(f"\nBODY-AXIS WOBBLE, measured on the user's own recording")
    print(f"  fish tracked         : {len(full)}")
    print(f"  RMS wobble (>0.2 s)  : {rms.mean():.2f}°   (per fish: "
          f"{', '.join(f'{v:.1f}' for v in np.sort(rms)[::-1][:6])} …)")
    print(f"  peak-to-peak (95th)  : {np.percentile(np.abs(hp), 97.5) * 2:.2f}°")
    print(f"  dominant frequency   : {np.median(peak):.2f} Hz  (per fish "
          f"{np.percentile(peak, 25):.1f}–{np.percentile(peak, 75):.1f} Hz)")
    print(f"  slow swing (total)   : {ang.std(0).mean():.2f}° RMS")


if __name__ == "__main__":
    main()
