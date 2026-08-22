#!/usr/bin/env python3
"""วัด "ฝูงยืดเป็นแถว" ของ pod — ก่อน (Reynolds boids) เทียบ หลัง (slot formation).

user 22 ส.ค. 2026: "ตรวจสอบฝูงปลากะมง ทำไมว่ายน้ำเป็นแถวตั้งตรง ไม่เป็นธรรมชาติ"

🔴 ทำไมต้องมีไฟล์นี้ ทั้งที่ tools/test.sh ผ่าน 855 ชุด
   FishSchoolSystem ต้องใช้ UnityEngine ⇒ **ไม่ได้อยู่ใน harness นั้นเลย** เทสที่ผ่านจึงไม่ใช่
   หลักฐานว่าอาการหาย (FISH_TUNING.md §3: เครื่องมือที่ไม่มีมิติที่บั๊กอยู่ จะรายงานว่าไม่มีบั๊กเสมอ)

ตัวชี้วัด = **อัตราส่วนยืด** (elongation) = แกนหลักยาวสุด ÷ สั้นสุด ของกลุ่มตำแหน่งปลา (PCA)
   1.0 = ทรงกลม · 2-3 = ก้อนรีปกติของฝูงจริง · >6 = ริบบิ้น/แถว ซึ่งคือสิ่งที่ user เห็น
   เลือกตัวนี้เพราะมันคือคำว่า "เป็นแถว" ในภาษาตัวเลข และไม่ขึ้นกับมุมกล้อง (ปัญหาที่เคยทำ
   คลิปก่อน/หลังเทียบกันไม่ได้ — FISH_TUNING.md §3.3)

รัน:  python3 tools/school_sim/pod_stretch.py
"""
import math
import sys

import numpy as np

from school_dt_sim import Pod, dt_sequence

# pod:yellowtail ตามที่ MarineMath.SchoolGeometryFor คืน (สเกล 1.0) — ดู Pod.__doc__
POD_R = 105.9
POD_FLEN = 20.8
POD_N = 50
POD_VERT_FACTOR = 0.25          # MarineMath.PodVertFactor


def axes(pos: np.ndarray):
    """ครึ่งแกนของทรงรีที่พอดีกับกลุ่มจุด เรียงยาว→สั้น."""
    c = pos - pos.mean(0)
    return np.linalg.svd(c, compute_uv=False) / math.sqrt(len(pos))


def rowness(pos: np.ndarray) -> float:
    """σ1/σ2 — "เป็นแถวแค่ไหน" (prolate).

    🔴 ตัววัดรอบแรกของไฟล์นี้ใช้ σ1/σ3 (ยาวสุด÷สั้นสุด) แล้ว **อ่านผิด**: มันให้คะแนนสูงกับ
    วงแหวนแบน (vortex 9.40) พอ ๆ กับแถวยาว ทั้งที่วงแหวนไม่ใช่ "แถว" ในสายตาคนดูเลย
    ⇒ เกือบตัดทรงผิดตัวออกจากถุงของ pod

    แถว = ยาวหนึ่งแกน สั้นสองแกน ⇒ σ1/σ2 สูง
    จานแบน/วงแหวน = ยาวสองแกน สั้นหนึ่งแกน ⇒ σ1/σ2 ≈ 1 (ไปโผล่ที่ σ2/σ3 แทน)
    """
    s = axes(pos)
    return float(s[0] / max(s[1], 1e-9))


def flatness(pos: np.ndarray) -> float:
    """σ2/σ3 — "แบนแค่ไหน" (oblate). ฝูงจริงแบนเป็นแพนเค้กอยู่แล้ว ค่านี้สูงไม่ใช่ปัญหา."""
    s = axes(pos)
    return float(s[1] / max(s[2], 1e-9))


def vertical_frac(pos: np.ndarray) -> float:
    """แกนยาวสุดเอียงไปทางแนวตั้งแค่ไหน 0..1 (1 = ตั้งฉากกับพื้นทะเล)."""
    c = pos - pos.mean(0)
    _, _, vt = np.linalg.svd(c, full_matrices=False)
    return float(abs(vt[0][1]))


def run_boids(seconds=120.0, seed=11):
    """ของเดิม: pod เดิน Reynolds boids (useForm = !IsPod)."""
    rng = np.random.default_rng(4)
    dts = dt_sequence("fixed60", seconds, rng)
    pod = Pod(n=POD_N, seed=seed)
    out = []
    for i, dt in enumerate(dts):
        pod.step(dt)
        if i % 60 == 0:                      # ทุก ~1 วินาที
            out.append((i / 60.0, rowness(pod.pos), vertical_frac(pod.pos)))
    return out


def slot_extent(mode: str, n=POD_N, R=POD_R, flen=POD_FLEN, seed=3):
    """ของใหม่: สลอตของ SchoolFormation.FormTarget — วัดทรงที่สลอตกางเป็น.

    ไม่ต้องจำลองการว่ายเลย เพราะประเด็นทั้งหมดของการย้ายมาระบบนี้คือ **ปลาไล่ตามสลอต**
    (SchoolFormation.cs หัวไฟล์: 'a spring, not a cruise missile') ⇒ ทรงของฝูงถูกกำหนดโดย
    ชุดสลอต ไม่ใช่โดยการสะสมของ alignment แบบ boids · ความยืดจึงมีเพดานตามสูตร ไม่ลอย
    """
    rng = np.random.default_rng(seed)
    span_xz = R
    # 🔴 ค่าที่แก้ไป 22 ส.ค.: pod ใช้ PodVertFactor×2 ไม่ใช่ 0.55 ของ formation
    span_y = R * POD_VERT_FACTOR * 2.0
    i = np.arange(n)
    r0, r1, r2 = (rng.random(n) for _ in range(3))
    ang = (i / n) * 6.283
    cyl_y = (rng.random(n) - 0.5) * 2.0
    ball_ph = rng.random(n) * math.pi
    ball_a = rng.random(n) * 2 * math.pi
    lane = (i / max(n - 1, 1)) - 0.5
    yspread = (r0 - 0.5) * flen * 1.4
    t, spin = 7.0, 0.3

    if mode == "cluster":
        x = (r0 - 0.5) * span_xz * 2.0
        y = (r1 - 0.5) * span_y
        z = (r2 - 0.5) * span_xz * 2.0
    elif mode == "stream":
        d, fwd = 0.7, lane * R * 4.0
        sway = np.sin(t * 1.1 + ang * 1.6) * R * 0.3
        x = math.cos(d) * fwd - math.sin(d) * (sway + yspread * 0.3)
        z = math.sin(d) * fwd + math.cos(d) * (sway + yspread * 0.3)
        y = yspread * 0.5 + np.sin(t * 0.7 + ang) * R * 0.12
    elif mode == "vortex":
        a = ang + t * spin
        x, z, y = np.cos(a) * R, np.sin(a) * R, yspread
    elif mode == "ball":
        a = ball_a + t * spin * 1.3
        rr, sp = R * 0.55, np.sin(ball_ph)
        x, z = np.cos(a) * sp * rr, np.sin(a) * sp * rr
        y = np.cos(ball_ph) * rr * 0.85
    elif mode == "cone":
        a = ang + t * spin
        cone_h = R * 1.8
        rr = R * (1.0 - 0.8 * ((cyl_y + 1) / 2))
        x, z, y = np.cos(a) * rr, np.sin(a) * rr, ((cyl_y + 1) / 2) * cone_h
    elif mode == "tornado":                       # ทรงที่ถูก "ตัดออก" จากถุงของ pod
        a = ang + t * spin
        rr, cyl_h = R * 0.55, R * 1.8
        x, z, y = np.cos(a) * rr, np.sin(a) * rr, cyl_y * cyl_h
    else:
        raise ValueError(mode)
    p = np.stack([x, y, z], -1)
    return rowness(p), flatness(p), vertical_frac(p)


def positive_control():
    """เครื่องมือตาบอดไหม — ป้อนของที่รู้คำตอบอยู่แล้ว (FISH_TUNING.md §3)."""
    rng = np.random.default_rng(0)
    ball = rng.normal(size=(POD_N, 3)) * 30.0
    row = np.stack([rng.normal(size=POD_N) * 300.0,
                    rng.normal(size=POD_N) * 8.0,
                    rng.normal(size=POD_N) * 8.0], -1)
    disc = np.stack([rng.normal(size=POD_N) * 200.0,
                     rng.normal(size=POD_N) * 6.0,
                     rng.normal(size=POD_N) * 200.0], -1)
    return rowness(ball), rowness(row), rowness(disc)


def main():
    ball_r, row_r, disc_r = positive_control()
    print("── positive control (เครื่องมือติดไฟไหม + แยกแถวออกจากจานแบนได้ไหม) ──")
    print(f"  ทรงกลมสุ่ม   rowness = {ball_r:5.2f}   (ควรใกล้ 1)")
    print(f"  แถวยาวสุ่ม   rowness = {row_r:5.2f}   (ควรสูงมาก)")
    print(f"  จานแบนสุ่ม   rowness = {disc_r:5.2f}   (ควรใกล้ 1 — จานไม่ใช่แถว)")
    if not (ball_r < 2.0 < row_r) or disc_r > 2.0:
        print("  ❌ เครื่องมือแยกไม่ออก — ผลด้านล่างเชื่อไม่ได้")
        return 1
    print("  ✅ แยกออกทั้งสามแบบ\n")

    print("── ก่อน: pod เดิน Reynolds boids (ของที่อยู่บนเครื่อง user ตอนนี้) ──")
    series = run_boids()
    print("   วินาที | rowness | แกนยาวตั้งแค่ไหน")
    for tt, r, vf in series:
        if int(tt) % 15 == 0:
            print(f"   {tt:6.0f} |  {r:6.2f} |  {vf:.2f}")
    tail = [r for tt, r, _ in series if tt >= 60]
    peak = max(r for _, r, _ in series)
    print(f"   สูงสุด {peak:.2f} · เฉลี่ยหลังนาทีแรก {sum(tail)/len(tail):.2f}\n")

    print("── หลัง: สลอตของ SchoolFormation (rowness = เป็นแถวแค่ไหน) ──")
    print("   ทรง        | rowness | flatness | ตั้ง | อยู่ในถุง pod?")
    bag = {"cluster", "stream", "vortex", "ball"}
    worst_in, worst_name = 0.0, ""
    for m in ("cluster", "stream", "vortex", "ball", "tornado", "cone"):
        r, f, vf = slot_extent(m)
        mark = "✅" if m in bag else "—"
        if m in bag and r > worst_in:
            worst_in, worst_name = r, m
        print(f"   {m:10s} |  {r:6.2f} |  {f:7.2f} | {vf:.2f} |  {mark}")
    print(f"\n   แย่สุดในถุง pod = {worst_name} ที่ {worst_in:.2f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
