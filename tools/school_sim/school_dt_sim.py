#!/usr/bin/env python3
"""
dt-variance rig for the school jitter (user: "ฝูงบาราคูด้า/กะมง ส่ายหัว/กระตุก บนเครื่องจริงเท่านั้น").

WHY THIS FILE EXISTS
--------------------
Every instrument used so far — SchoolClip CI (Time.captureFramerate=30), every Python
sim, every filmstrip — ran with a CONSTANT dt.  The schools integrate PER FRAME
(step × Fs, Fs = clamp(dt/16.667ms, 0.5, 2.5)), so any bug whose cause is the
VARIANCE of dt is invisible to all of them by construction.  This rig is the first
instrument with a dt axis.

It is a line-for-line port of the two paths the user's two schools actually run:

  * CALM FORMATION  (school:barracuda)  -> BoidsJob.FormationStep, calm branch
                                           + FishSchoolSystem.BuildSlots
                                           + Core/SchoolFormation.FormTarget / mode wheel
  * REYNOLDS POD    (pod:yellowtail = ปลากะมง) -> BoidsJob.Execute, non-formation branch

plus the DRAW layer (bank tanh + DrawRot slerp τ=0.45 s) because that is what the eye
sees, not the sim state.

Run:
    python3 school_dt_sim.py --report              # metric table, every dt profile
    python3 school_dt_sim.py --gif /tmp/out.gif    # side-by-side top-down movie
"""

import argparse
import math

import numpy as np

# ── constants transcribed from the C# (do not "improve" these) ───────────────
MS_BASE   = 16.667e-3          # MarineMath.MsBase
BASE_STEP = 0.016              # MarineMath.BaseStep
FS_MIN, FS_MAX = 0.5, 2.5      # MarineMath.FsMin/FsMax

CALM_TURN_CLAMP   = 0.05       # SchoolFormation.CalmTurnCapPerFrame
CALM_CHASE        = 0.05       # CalmChasePerFrame
CALM_CAP_MUL      = 1.8
CALM_VERT_EASE    = 0.06
CALM_VERT_RATIO   = 0.6
TURN_CAP_PF       = 0.045      # TurnCapPerFrame (pod path: rad/frame at Fs=1)
DEADBAND          = 0.025      # commit 00f0ff4 (build 345), all three turn paths
DRAWROT_TAU       = 0.45       # FishSchoolSystem line 1077
BANK_REF          = 1.2        # SwimStyle.BankRad reference rad/s
YAWRATE_LERP      = 0.25       # FishSchoolSystem line 1055

W_SEP, W_ALIGN, W_COH, W_ANCHOR, W_WANDER = 1.6, 1.0, 1.0, 0.6, 0.5

GOLDEN_ANGLE = 2.39996
GOLDEN_RATIO = 0.61803398875
WEB_TWO_PI   = 6.283


# ── dt profiles ──────────────────────────────────────────────────────────────
def dt_sequence(profile: str, seconds: float, rng: np.random.Generator) -> np.ndarray:
    """A wall-clock dt stream. Same wall duration for every profile so the
    comparison is 'what the eye saw in 20 seconds', not 'per frame'."""
    out = []
    t = 0.0
    while t < seconds:
        if profile == "fixed60":            # what every instrument so far ran at
            dt = MS_BASE
        elif profile == "fixed30":          # SchoolClip CI (captureFramerate=30)
            dt = 1.0 / 30.0
        elif profile == "vsync":            # 60 fps that misses a vsync now and then
            dt = MS_BASE * (2.0 if rng.random() < 0.18 else 1.0)
        elif profile == "gauss":            # mild thermal wobble around 60
            dt = float(np.clip(rng.normal(MS_BASE, 3.0e-3), 8e-3, 60e-3))
        elif profile == "device":           # measured-iPhone-shaped: mostly 60, hitches
            r = rng.random()
            dt = MS_BASE if r < 0.78 else (2 * MS_BASE if r < 0.94 else
                                           (1.5 * MS_BASE if r < 0.99 else 3 * MS_BASE))
        else:
            raise SystemExit(f"unknown dt profile: {profile}")
        out.append(dt)
        t += dt
    return np.asarray(out)


def fs_of(dt: float) -> float:
    return min(FS_MAX, max(FS_MIN, dt / MS_BASE))


def delta_angle(to, frm):
    return np.arctan2(np.sin(to - frm), np.cos(to - frm))


# ── quaternion helpers (the draw layer slerps quaternions, so we do too) ─────
def q_yaw_bank(yaw, bank):
    """Unity: Quaternion.Euler(0, yawDeg, 0) * Quaternion.Euler(0, 0, bankDeg)."""
    hy, hb = yaw * 0.5, bank * 0.5
    qy = np.stack([np.zeros_like(hy), np.sin(hy), np.zeros_like(hy), np.cos(hy)], -1)
    qb = np.stack([np.zeros_like(hb), np.zeros_like(hb), np.sin(hb), np.cos(hb)], -1)
    return q_mul(qy, qb)


def q_mul(a, b):
    ax, ay, az, aw = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    bx, by, bz, bw = b[..., 0], b[..., 1], b[..., 2], b[..., 3]
    return np.stack([
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    ], -1)


def q_slerp(a, b, k):
    """Unity Quaternion.Slerp, vectorised, with the shortest-arc flip."""
    dot = np.sum(a * b, -1, keepdims=True)
    b = np.where(dot < 0, -b, b)
    dot = np.abs(dot).clip(-1.0, 1.0)
    theta = np.arccos(dot)
    lin = theta < 1e-4
    sin_t = np.sin(theta)
    w1 = np.where(lin, 1.0 - k, np.sin((1.0 - k) * theta) / np.where(lin, 1.0, sin_t))
    w2 = np.where(lin, k, np.sin(k * theta) / np.where(lin, 1.0, sin_t))
    out = a * w1 + b * w2
    return out / np.linalg.norm(out, axis=-1, keepdims=True)


def q_yaw_of(q):
    """Unity yaw convention used by the fish: forward = (sin h, 0, cos h)."""
    x, y, z, w = q[..., 0], q[..., 1], q[..., 2], q[..., 3]
    fx = 2 * (x * z + w * y)
    fz = 1 - 2 * (x * x + y * y)
    return np.arctan2(fx, fz)


# ══════════════════════════════════════════════════════════════════════════════
#  CALM FORMATION — school:barracuda
# ══════════════════════════════════════════════════════════════════════════════
class CalmSchool:
    """BoidsJob.FormationStep (calm) + BuildSlots + SchoolFormation, for one school.

    Geometry is the real Chang barracuda row (FishSchoolSystem line 449):
        fish 17.1 u, R 143.9 u, ±39.6 u, 4.0 u/s, calm, swimMul 0.06.
    """

    MODES = ["cluster", "cluster", "cluster", "stream",
             "cluster", "stream", "vortex", "tornado",
             "cone", "tornado", "cone_up", "ball"]
    MODE_DUR = {"cluster": 7.0, "vortex": 13.0, "tornado": 16.0, "cone": 15.0,
                "cone_up": 15.0, "ball": 9.0, "stream": 12.0}

    def __init__(self, n=160, seed=7, mode_lock=None, drift=True):
        rng = np.random.default_rng(seed)
        self.n = n
        # 🔴 The school CENTRE travels on the wall clock (FishSchoolSystem:1551,
        # Vector3.MoveTowards(HomeNow, want, MaxSpeed × Time.deltaTime)) while the fish
        # travel on the CLAMPED Fs. That mismatch is a dt-variance amplifier and it only
        # exists when the school is actually going somewhere, so it must be modelled.
        self.drift = drift
        self.drift_speed = 4.0 * 0.5        # half cruise, a school ambling across the map
        self.want = None
        self.want_until = 0.0
        self.diver = None                   # set by drive_diver(); TourController:617
        self.bubble = 16.0                  # DiveLightMath.FishBubble × 2
        self.diver_speed = 30.0 * 0.35      # DroneFlight.Speed, an unhurried swim-through
        self.flen = 17.1
        self.R = 143.9
        self.cap = 4.0 / 60.0                    # CapPerFrame = MaxSpeed/60
        self.spin = (0.22 + rng.random() * 0.22) * 0.1     # spinMul 0.1
        self.mode_dur_mul = 2.2
        self.trans_dur_mul = 2.5
        self.anchor = np.array([0.0, -60.0, 0.0])
        self.safe_r = self.R * 3.2
        self.cap_y = -10.0 - self.flen
        self.floor_y = -140.0

        i = np.arange(n)
        span_xz, span_y = self.R, self.R * 0.55
        r0, r1, r2, r6 = (rng.random(n) for _ in range(4))
        self.cx = (r0 - 0.5) * span_xz * 2.0
        self.cy = (r1 - 0.5) * span_y
        self.cz = (r2 - 0.5) * span_xz * 2.0
        self.ang = (i / n) * WEB_TWO_PI
        self.yspread = (r0 - 0.5) * self.flen * 1.4
        self.cyl_y = (i * GOLDEN_RATIO) % 1.0
        self.ball_ph = np.arccos(1.0 - 2.0 * (i + 0.5) / n)
        self.ball_a = i * GOLDEN_ANGLE
        self.lane = i / n - 0.5
        self.tr_jit = r6 * 0.35

        # mode wheel
        self.mode = mode_lock or "cluster"
        self.mode_lock = mode_lock
        self.prev_mode = self.mode
        self.has_prev = False
        self.until = 0.0
        self.trans_t0 = 0.0
        self.trans_dur = 8.0 * self.trans_dur_mul
        self.stream_dir = rng.random() * 2 * math.pi
        self.prev_stream_dir = self.stream_dir
        self.group_heading = rng.random() * 2 * math.pi
        self.rng = rng

        # fish state: start ON the slots, heading = formation heading (settled school)
        gx, gy, gz, vx, vz = self.form_target(self.mode, 0.0)
        self.pos = np.stack([self.anchor[0] + gx, self.anchor[1] + gy, self.anchor[2] + gz], -1)
        self.head = np.arctan2(vx, vz)
        self.vel = np.zeros((n, 3))

        # draw layer
        self.draw_q = None
        self.yaw_prev = self.head.copy()
        self.yaw_rate = np.zeros(n)
        self.max_bank = math.radians(22.0)       # SwimStyle table, fish
        self.t = 0.0

    # ── SchoolFormation.FormTarget, vectorised ──────────────────────────────
    def form_target(self, mode, t, stream_dir=None):
        R, flen, spin = self.R, self.flen, self.spin
        if mode == "vortex":
            a = self.ang + t * spin
            return np.cos(a) * R, self.yspread, np.sin(a) * R, -np.sin(a), np.cos(a)
        if mode == "tornado":
            a = self.ang + t * spin
            rr, cyl_h = R * 0.55, R * 1.8
            return np.cos(a) * rr, self.cyl_y * cyl_h, np.sin(a) * rr, -np.sin(a), np.cos(a)
        if mode == "cone":
            a = self.ang + t * spin
            rr = R * (1.0 - 0.8 * self.cyl_y)
            return np.cos(a) * rr, self.cyl_y * R * 1.8, np.sin(a) * rr, -np.sin(a), np.cos(a)
        if mode == "cone_up":
            a = self.ang + t * spin
            rr = R * (0.2 + 0.8 * self.cyl_y)
            return np.cos(a) * rr, self.cyl_y * R * 1.8, np.sin(a) * rr, -np.sin(a), np.cos(a)
        if mode == "ball":
            a = self.ball_a + t * spin * 1.3
            rr, sp = R * 0.55, np.sin(self.ball_ph)
            return (np.cos(a) * sp * rr, np.cos(self.ball_ph) * rr * 0.85,
                    np.sin(a) * sp * rr, -np.sin(a), np.cos(a))
        if mode == "stream":
            d = self.stream_dir if stream_dir is None else stream_dir
            fwd = self.lane * R * 4.0
            sway = np.sin(t * 1.1 + self.ang * 1.6) * R * 0.3
            x = np.cos(d) * fwd - np.sin(d) * (sway + self.yspread * 0.3)
            z = np.sin(d) * fwd + np.cos(d) * (sway + self.yspread * 0.3)
            y = self.yspread * 0.5 + np.sin(t * 0.7 + self.ang) * R * 0.12
            return x, y, z, np.full(self.n, np.cos(d)), np.full(self.n, np.sin(d))
        # cluster
        x = self.cx + np.sin(t * 0.5 + self.ang * 1.7) * flen * 0.5
        y = self.cy + np.sin(t * 0.7 + self.ang * 1.3) * flen * 0.35
        z = self.cz + np.cos(t * 0.5 + self.ang * 1.7) * flen * 0.5
        gh = self.group_heading
        return x, y, z, np.full(self.n, np.cos(gh)), np.full(self.n, np.sin(gh))

    def mode_step(self, t):
        if self.mode_lock:
            return
        if t > self.until:
            m = self.MODES[int(self.rng.random() * len(self.MODES))]
            hold = self.MODE_DUR[m] * self.mode_dur_mul + self.rng.random() * 6.0 * self.mode_dur_mul
            if m == self.mode:
                self.until = t + hold
                return
            self.prev_mode = self.mode
            self.prev_stream_dir = self.stream_dir
            self.has_prev = True
            self.trans_t0 = t
            self.trans_dur = 8.0 * self.trans_dur_mul
            self.mode = m
            self.until = t + hold
            if m == "stream":
                self.stream_dir = self.rng.random() * 2 * math.pi
        if self.has_prev and (t - self.trans_t0) / self.trans_dur > 1.4:
            self.has_prev = False

    def slots(self, t):
        gx, gy, gz, vx, vz = self.form_target(self.mode, t)
        if self.has_prev:
            kk = np.clip((t - self.trans_t0) / self.trans_dur - self.tr_jit, 0.0, 1.0)
            e = kk * kk * (3.0 - 2.0 * kk)
            px, py, pz, pvx, pvz = self.form_target(self.prev_mode, t, self.prev_stream_dir)
            gx = px + (gx - px) * e
            gy = py + (gy - py) * e
            gz = pz + (gz - pz) * e
            vx = pvx + (vx - pvx) * e
            vz = pvz + (vz - pvz) * e
        return (np.stack([self.anchor[0] + gx, self.anchor[1] + gy, self.anchor[2] + gz], -1),
                vx, vz)

    # ── one frame ───────────────────────────────────────────────────────────
    def move_anchor(self, dt):
        """FishSchoolSystem:1551 + BuildSlots:1194-1199 — the centre walks on WALL time and
        the formation heading is a per-frame LerpAngle(…, 0.05) of the drift direction."""
        if not self.drift:
            return
        if self.want is None or self.t > self.want_until:
            a = self.rng.random() * 2 * math.pi
            r = 200.0 + self.rng.random() * 250.0
            self.want = np.array([math.cos(a) * r, self.anchor[1], math.sin(a) * r])
            self.want_until = self.t + 12.0 + self.rng.random() * 10.0
        prev = self.anchor.copy()
        to = self.want - self.anchor
        d = float(np.linalg.norm(to))
        stepd = self.drift_speed * dt                      # ← wall clock, NOT Fs
        if d > 1e-6:
            self.anchor = self.anchor + to / d * min(stepd, d)
        drift = self.anchor - prev
        if drift[0] ** 2 + drift[2] ** 2 > 1e-8:
            tgt = math.atan2(drift[2], drift[0])
            self.group_heading += math.atan2(math.sin(tgt - self.group_heading),
                                             math.cos(tgt - self.group_heading)) * 0.05

    def drive_diver(self, dt):
        """A diver swimming a straight line through the shoal at a third of drone top
        speed — the one thing every previous instrument left out, because SchoolClip's
        camera stands still."""
        if self.diver is None:
            return
        self.diver = self.diver + self.diver_dir * (self.diver_speed * dt)

    def step(self, dt, deadband=DEADBAND, fixed_step=False, drawrot=True):
        fs = fs_of(dt)
        sim_dt = BASE_STEP * fs
        self.t += dt
        self.drive_diver(dt)
        self.move_anchor(dt)
        self.mode_step(self.t)
        slot, vdx, vdz = self.slots(self.t)

        ddx = slot[:, 0] - self.pos[:, 0]
        ddz = slot[:, 2] - self.pos[:, 2]
        dh = np.hypot(ddx, ddz)
        on_dir = np.arctan2(vdx, vdz)

        d_calm = delta_angle(on_dir, self.head)
        turn = np.clip(d_calm, -CALM_TURN_CLAMP, CALM_TURN_CLAMP) * fs
        self.head = self.head + np.where(np.abs(d_calm) > deadband, turn, 0.0)

        mv = np.minimum(self.cap * CALM_CAP_MUL, dh * CALM_CHASE) * fs
        m_a = np.where(dh > 0.001, np.arctan2(ddx, ddz), self.head)
        step_x = np.sin(m_a) * mv
        step_z = np.cos(m_a) * mv
        fx, fz = np.sin(self.head), np.cos(self.head)
        into = fx * step_x + fz * step_z
        back = into < 0
        step_x = np.where(back, step_x - fx * into, step_x)
        step_z = np.where(back, step_z - fz * into, step_z)

        self.pos[:, 0] += step_x
        self.pos[:, 2] += step_z

        # ClampSafe
        dx = self.pos[:, 0] - self.anchor[0]
        dz = self.pos[:, 2] - self.anchor[2]
        rr = np.hypot(dx, dz)
        out = rr > self.safe_r
        k = np.where(out & (rr > 1e-4), self.safe_r / np.maximum(rr, 1e-6), 1.0)
        self.pos[:, 0] = self.anchor[0] + dx * k
        self.pos[:, 2] = self.anchor[2] + dz * k

        dyc = slot[:, 1] - self.pos[:, 1]
        self.pos[:, 1] += np.clip(dyc * CALM_VERT_EASE, -mv * CALM_VERT_RATIO, mv * CALM_VERT_RATIO)
        self.pos[:, 1] = np.clip(self.pos[:, 1], self.floor_y, self.cap_y)

        per_sec = 1.0 / max(sim_dt, 1e-4)
        self.vel = np.stack([step_x * per_sec, np.zeros(self.n), step_z * per_sec], -1)

        # 🔴 THE DIVER BUBBLE (FishSchoolSystem:925-939, DiveLightMath.BubblePush).
        # Applied AFTER the sim, straight onto the position, and NOT scaled by dt at all:
        # a fish at the centre is teleported a full bubble radius (16 u) outward in one
        # frame. It only exists in tour mode, it only touches SCHOOL fish (a whale shark
        # is never displaced), and every instrument so far filmed a school with a
        # STATIONARY camera — so no rig has ever exercised it.
        if self.diver is not None:
            dx = self.pos[:, 0] - self.diver[0]
            dz = self.pos[:, 2] - self.diver[2]
            d = np.hypot(dx, dz)
            push = np.where((d < self.bubble) & (d > 0.01),
                            (self.bubble - d) / self.bubble * self.bubble, 0.0)
            hit = push > 0
            self.pos[hit, 0] += dx[hit] / d[hit] * push[hit]
            self.pos[hit, 2] += dz[hit] / d[hit] * push[hit]

        return self.draw(dt, sim_dt, drawrot)

    # ── the draw layer (bank + DrawRot), i.e. what the eye actually gets ────
    def draw(self, dt, sim_dt, drawrot=True):
        yaw = self.head
        d_yaw = delta_angle(yaw, self.yaw_prev)
        rate = self.yaw_rate + (d_yaw / max(sim_dt, 1e-4) - self.yaw_rate) * YAWRATE_LERP
        self.yaw_prev = yaw.copy()
        self.yaw_rate = rate
        bank = -self.max_bank * np.tanh(rate / BANK_REF)
        q = q_yaw_bank(yaw, bank)
        if self.draw_q is None:
            self.draw_q = q
        elif drawrot:
            k = 1.0 - math.exp(-dt / DRAWROT_TAU)
            self.draw_q = q_slerp(self.draw_q, q, k)
        else:
            self.draw_q = q
        return q_yaw_of(self.draw_q), bank


# ══════════════════════════════════════════════════════════════════════════════
#  REYNOLDS POD — pod:yellowtail (ปลากะมง)
# ══════════════════════════════════════════════════════════════════════════════
class Pod:
    """BoidsJob.Execute, non-formation branch (pods keep the Reynolds path).

    Geometry from MarineMath.SchoolGeometryFor(pod:yellowtail, s):
        animal 20.8·s u, R 105.9·s u, ±26.5·s u, 24.96·s u/s, N = 50.
    """

    def __init__(self, n=50, seed=11, item_scale=1.0, think_every=1):
        rng = np.random.default_rng(seed)
        s = item_scale
        self.n = n
        self.flen = 20.8 * s
        self.home_r = 105.9 * s * 1.2
        self.neighbor_r = self.flen * 4.0
        self.sep_r = self.flen * 1.0
        self.max_speed = 24.96 * s
        self.vert_half = 105.9 * s * 0.25
        self.anchor = np.array([0.0, -60.0, 0.0])
        self.cap_y = -10.0 - self.flen
        self.think_every = think_every

        ang = rng.random(n) * 2 * math.pi
        rad = np.sqrt(rng.random(n)) * self.home_r * 0.8
        self.pos = np.stack([self.anchor[0] + np.cos(ang) * rad,
                             self.anchor[1] + (rng.random(n) - 0.5) * self.vert_half,
                             self.anchor[2] + np.sin(ang) * rad], -1)
        h = rng.random(n) * 2 * math.pi
        self.vel = np.stack([np.cos(h) * self.max_speed,
                             np.zeros(n),
                             np.sin(h) * self.max_speed], -1)
        self.phase = rng.random(n) * 2 * math.pi
        self.t = 0.0
        self.frame = 0
        self.draw_q = None
        self.yaw_prev = np.arctan2(self.vel[:, 0], self.vel[:, 2])
        self.yaw_rate = np.zeros(n)
        self.max_bank = math.radians(22.0)

    def step(self, dt, deadband=DEADBAND, fixed_step=False, drawrot=True):
        fs = fs_of(dt)
        sim_dt = BASE_STEP * fs
        self.t += dt
        self.frame += 1

        think = (self.frame % self.think_every) == 0
        if not think:
            self.pos = self.pos + self.vel * sim_dt
            self._clamp()
            return self.draw(dt, sim_dt, drawrot)

        p, v = self.pos, self.vel
        d = p[:, None, :] - p[None, :, :]
        dist = np.linalg.norm(d, axis=-1)
        np.fill_diagonal(dist, np.inf)
        sep_m = (dist < self.sep_r) & (dist > 1e-4)
        sep = np.sum(np.where(sep_m[..., None], d / np.maximum(dist, 1e-6)[..., None] ** 2, 0.0), 1)
        nb = dist < self.neighbor_r
        cnt = nb.sum(1)
        ali = np.where(cnt[:, None] > 0,
                       np.sum(np.where(nb[..., None], v[None, :, :], 0.0), 1) / np.maximum(cnt, 1)[:, None],
                       0.0)
        coh = np.where(cnt[:, None] > 0,
                       np.sum(np.where(nb[..., None], p[None, :, :], 0.0), 1) / np.maximum(cnt, 1)[:, None] - p,
                       0.0)

        def nsafe(a):
            m = np.linalg.norm(a, axis=-1, keepdims=True)
            return np.where(m > 1e-6, a / np.maximum(m, 1e-9), 0.0)

        steer = nsafe(ali) * W_ALIGN + nsafe(coh) * W_COH + nsafe(sep) * W_SEP

        to_a = self.anchor[None, :] - p
        ad = np.linalg.norm(to_a, axis=-1)
        pull = W_ANCHOR * np.where(ad > self.home_r,
                                   1.0 + (ad - self.home_r) / self.home_r,
                                   ad / self.home_r * 0.15)
        steer += to_a / np.maximum(ad, 1e-6)[:, None] * pull[:, None]

        wph = self.phase + self.t * 0.7
        steer += np.stack([np.cos(wph), np.sin(wph * 0.5) * 0.4, np.sin(wph)], -1) * W_WANDER

        dy_a = p[:, 1] - self.anchor[1]
        steer[:, 1] -= np.where(np.abs(dy_a) > self.vert_half, np.sign(dy_a) * 2.0, 0.0)

        desired = v + steer * sim_dt
        cur_h = np.arctan2(v[:, 2], v[:, 0])
        des_h = np.arctan2(desired[:, 2], desired[:, 0])
        cap = TURN_CAP_PF * fs
        d_turn = delta_angle(des_h, cur_h)
        turned = cur_h + np.clip(d_turn, -cap, cap)
        new_h = np.where(np.abs(d_turn) > deadband, turned, cur_h)

        speed = self.max_speed
        nv = np.stack([np.cos(new_h) * speed,
                       np.clip(desired[:, 1], -speed * 0.55, speed * 0.55),
                       np.sin(new_h) * speed], -1)
        self.pos = p + nv * sim_dt
        self.vel = nv
        self._clamp()
        return self.draw(dt, sim_dt, drawrot)

    def _clamp(self):
        # ClampHome (tangent slide) + ClampVertical
        dx = self.pos[:, 0] - self.anchor[0]
        dz = self.pos[:, 2] - self.anchor[2]
        rr = np.hypot(dx, dz)
        out = (rr > self.home_r) & (rr > 1e-4)
        if out.any():
            k = self.home_r / np.maximum(rr, 1e-6)
            self.pos[out, 0] = self.anchor[0] + dx[out] * k[out]
            self.pos[out, 2] = self.anchor[2] + dz[out] * k[out]
            nx, nz = dx / np.maximum(rr, 1e-6), dz / np.maximum(rr, 1e-6)
            outward = self.vel[:, 0] * nx + self.vel[:, 2] * nz
            hit = out & (outward > 0)
            self.vel[hit, 0] -= nx[hit] * outward[hit]
            self.vel[hit, 2] -= nz[hit] * outward[hit]
        dy = self.pos[:, 1] - self.anchor[1]
        hi = dy > self.vert_half
        lo = dy < -self.vert_half
        self.pos[hi, 1] = self.anchor[1] + self.vert_half
        self.pos[lo, 1] = self.anchor[1] - self.vert_half
        self.vel[hi & (self.vel[:, 1] > 0), 1] = 0.0
        self.vel[lo & (self.vel[:, 1] < 0), 1] = 0.0
        over = self.pos[:, 1] > self.cap_y
        self.pos[over, 1] = self.cap_y

    def draw(self, dt, sim_dt, drawrot=True):
        # pods are drawn from LookRotation(vel) — yaw = atan2(x, z) in Unity terms
        yaw = np.arctan2(self.vel[:, 0], self.vel[:, 2])
        d_yaw = delta_angle(yaw, self.yaw_prev)
        rate = self.yaw_rate + (d_yaw / max(sim_dt, 1e-4) - self.yaw_rate) * YAWRATE_LERP
        self.yaw_prev = yaw.copy()
        self.yaw_rate = rate
        bank = -self.max_bank * np.tanh(rate / BANK_REF)
        q = q_yaw_bank(yaw, bank)
        if self.draw_q is None:
            self.draw_q = q
        elif drawrot:
            k = 1.0 - math.exp(-dt / DRAWROT_TAU)
            self.draw_q = q_slerp(self.draw_q, q, k)
        else:
            self.draw_q = q
        return q_yaw_of(self.draw_q), bank


# ══════════════════════════════════════════════════════════════════════════════
#  run + measure
# ══════════════════════════════════════════════════════════════════════════════
def run(school, dts, deadband=DEADBAND, drawrot=True):
    times, yaws, banks, poss = [], [], [], []
    t = 0.0
    for dt in dts:
        yaw, bank = school.step(float(dt), deadband=deadband, drawrot=drawrot)
        t += float(dt)
        times.append(t)
        yaws.append(yaw.copy())
        banks.append(bank.copy())
        poss.append(school.pos.copy())
    return (np.asarray(times), np.asarray(yaws), np.asarray(banks), np.asarray(poss))


def resample(times, sig, hz=120.0):
    """Put a variable-dt signal on the display's uniform clock — the eye samples
    wall-clock time, not frames, so every profile must be compared there."""
    grid = np.arange(times[0], times[-1], 1.0 / hz)
    unwrapped = np.unwrap(sig, axis=0)
    out = np.empty((grid.size, sig.shape[1]))
    for k in range(sig.shape[1]):
        out[:, k] = np.interp(grid, times, unwrapped[:, k])
    return grid, out


def shimmer(times, yaw, pos, body_len, hz=120.0, win_s=0.2):
    """AMPLITUDE of the fast wobble — the number that decides 'can the eye see it'.

    High-pass by subtracting a 0.2 s moving average: whatever is left is motion too
    fast to be swimming. Reported as degrees of yaw and as a FRACTION OF BODY LENGTH
    of sideways position, because 'u/s²' says nothing about visibility.
    """
    grid, y = resample(times, yaw, hz)
    _, px = resample(times, pos[:, :, 0], hz)
    _, pz = resample(times, pos[:, :, 2], hz)
    w = max(3, int(win_s * hz) | 1)
    k = np.ones(w) / w

    def hp(sig):
        pad = np.pad(sig, ((w // 2, w // 2), (0, 0)), mode="edge")
        smooth = np.stack([np.convolve(pad[:, i], k, mode="valid") for i in range(sig.shape[1])], -1)
        return sig - smooth[:sig.shape[0]]

    hy = hp(y)
    hx, hzp = hp(px), hp(pz)
    return dict(
        yaw_shimmer_deg=float(np.degrees(np.sqrt(np.mean(hy ** 2)))),
        pos_shimmer_bl=float(np.sqrt(np.mean(hx ** 2 + hzp ** 2)) / body_len),
    )


def frame_metrics(times, yaw, pos):
    """Instrument-free numbers, read straight off the frames the device drew.

    No resampling anywhere: a wall-clock velocity is (Δposition / Δt) between two
    frames the eye actually saw, so nothing here can be an artefact of how the rig
    samples. This is the honest version of 'is the motion smooth in wall time'.
    """
    dt = np.diff(times)[:, None]
    dp = np.diff(pos[:, :, [0, 2]], axis=0)
    v = dp / dt[..., None]                                   # u/s per frame, per fish
    sp = np.linalg.norm(v, axis=-1)
    dv = np.linalg.norm(np.diff(v, axis=0), axis=-1)         # velocity change per frame
    mean_sp = np.maximum(np.mean(sp, 0), 1e-9)
    yw = np.unwrap(yaw, axis=0)
    rate = np.diff(yw, axis=0) / dt                          # rad/s per frame
    d_rate = np.abs(np.diff(rate, axis=0))
    return dict(
        speed_cv=float(np.mean(np.std(sp, 0) / mean_sp)),        # 'พุ่งๆ หยุดๆ'
        dv_rel=float(np.mean(np.mean(dv, 0) / mean_sp)),         # velocity churn per frame
        yaw_jerk=float(np.degrees(np.mean(d_rate))),             # deg/s change of yaw rate per frame
    )


def metrics(times, yaw, pos, hz=120.0):
    """Numbers that describe 'ส่ายหัว/กระตุก', on the display clock."""
    grid, y = resample(times, yaw, hz)
    rate = np.diff(y, axis=0) * hz                      # rad/s
    # 1. wag: how often the yaw RATE reverses sign with meaningful amplitude
    sig = np.abs(rate) > math.radians(2.0)              # ≥2°/s counts as moving
    flip = (np.sign(rate[1:]) != np.sign(rate[:-1])) & sig[1:] & sig[:-1]
    wag_hz = flip.sum(0) / (grid[-1] - grid[0])
    # 2. how hard the reversals are
    accel = np.diff(rate, axis=0) * hz                  # rad/s²
    rms_acc = np.sqrt(np.mean(accel ** 2, 0))
    # 3. fraction of yaw-rate energy above 4 Hz (the 'shimmer' band)
    win = np.hanning(rate.shape[0])[:, None]
    spec = np.abs(np.fft.rfft(rate * win, axis=0)) ** 2
    freq = np.fft.rfftfreq(rate.shape[0], 1.0 / hz)
    hf = spec[freq > 4.0].sum(0) / np.maximum(spec[freq > 0.2].sum(0), 1e-12)
    # 4. position judder: RMS of the per-sample acceleration of the body
    gridp, px = resample(times, pos[:, :, 0], hz)
    _, pz = resample(times, pos[:, :, 2], hz)
    ax = np.diff(px, 2, axis=0) * hz * hz
    az = np.diff(pz, 2, axis=0) * hz * hz
    body_acc = np.sqrt(np.mean(ax ** 2 + az ** 2, 0))
    return dict(
        wag_hz=float(np.mean(wag_hz)),
        yaw_rate_deg=float(np.degrees(np.mean(np.abs(rate)))),
        rms_acc_deg=float(np.degrees(np.mean(rms_acc))),
        hf_frac=float(np.mean(hf)),
        body_acc=float(np.mean(body_acc)),
    )


PROFILES = ["fixed60", "fixed30", "gauss", "vsync", "device"]


# ── the control: is the judder real, or is it the rig's own resampling? ──────
def judder_vs_truth(factory, dts, deadband=DEADBAND, drawrot=True, seconds=None):
    """Compare a dt profile against the SAME motion sampled on a smooth clock.

    Ground truth = the school run at a fixed 8.33 ms (Fs = 0.5, the finest step the
    Fs clamp allows), i.e. the trajectory the sim is trying to describe. Sampling
    THAT at the profile's own frame times and pushing it through the identical
    resample→acceleration pipeline gives the artefact floor: whatever the metric
    reports for a perfectly dt-compensated motion drawn on those very frames.

    Anything above that floor is judder the device would actually show.
    """
    total = float(np.sum(dts)) if seconds is None else seconds
    gt_dts = np.full(int(total / (MS_BASE * 0.5)) + 4, MS_BASE * 0.5)
    gt_t, gt_yaw, _, gt_pos = run(factory(), gt_dts, deadband=deadband, drawrot=drawrot)
    t, yaw, _, pos = run(factory(), dts, deadband=deadband, drawrot=drawrot)

    keep = t <= gt_t[-1]
    t, yaw, pos = t[keep], yaw[keep], pos[keep]

    # ground truth, sampled on exactly the frames this profile drew
    ctrl_pos = np.stack([np.stack([np.interp(t, gt_t, gt_pos[:, k, ax])
                                   for k in range(gt_pos.shape[1])], -1)
                         for ax in (0, 1, 2)], -1)
    ctrl_yaw = np.stack([np.interp(t, gt_t, np.unwrap(gt_yaw[:, k]))
                         for k in range(gt_yaw.shape[1])], -1)

    real = metrics(t, yaw, pos)
    ctrl = metrics(t, ctrl_yaw, ctrl_pos)
    return real, ctrl


def report(args):
    schools = (("barracuda(calm)", lambda **kw: CalmSchool(seed=7, mode_lock=args.mode, **kw)),
               ("trevally(pod)", lambda **kw: Pod(seed=11, **kw)))
    rows = []
    for name, factory in schools:
        for prof in PROFILES:
            rng = np.random.default_rng(1234)
            dts = dt_sequence(prof, args.seconds, rng)
            times, yaw, bank, pos = run(factory(), dts, deadband=args.deadband,
                                        drawrot=not args.no_drawrot)
            rows.append((name, prof, len(dts),
                         metrics(times, yaw, pos), frame_metrics(times, yaw, pos)))

    print(f"\n{'school':17s} {'dt profile':10s} {'frames':>7s} "
          f"{'speedCV':>8s} {'dvRel':>7s} {'yawJerk':>9s} {'wag/s':>7s} {'HF>4Hz':>7s} {'bodyAcc':>9s}")
    print("-" * 92)
    base = {}
    for name, prof, nf, m, fm in rows:
        if prof == "fixed60":
            base[name] = fm
        b = base.get(name, fm)
        mark = ""
        if prof != "fixed60" and b["speed_cv"] > 1e-9:
            mark = f"  ×{fm['speed_cv'] / b['speed_cv']:.1f} speedCV"
        print(f"{name:17s} {prof:10s} {nf:7d} {fm['speed_cv']:8.3f} {fm['dv_rel']:7.3f} "
              f"{fm['yaw_jerk']:8.1f}° {m['wag_hz']:7.2f} {m['hf_frac']:7.3f} "
              f"{m['body_acc']:9.2f}{mark}")
    print("\nFRAME-LEVEL (no resampling — cannot be a rig artefact):")
    print("  speedCV = per-fish stddev/mean of wall-clock speed  ('พุ่งๆ หยุดๆ')")
    print("  dvRel   = mean |Δvelocity| per frame ÷ mean speed   (direction/speed churn)")
    print("  yawJerk = mean change of yaw RATE between frames, deg/s")
    print("DISPLAY-CLOCK (resampled to 120 Hz):")
    print("  wag/s   = yaw-rate sign reversals per second        ('ส่ายหัวไปมา')")
    print("  HF>4Hz  = share of yaw-rate energy above 4 Hz       bodyAcc = RMS body accel u/s²")


def truth_report(args):
    schools = (("barracuda(calm)", lambda **kw: CalmSchool(seed=7, mode_lock=args.mode or "cluster", **kw)),
               ("trevally(pod)", lambda **kw: Pod(seed=11, **kw)))
    print(f"\n{'school':17s} {'dt profile':10s} {'bodyAcc':>9s} {'floor':>9s} {'excess':>8s} "
          f"{'wag/s':>7s} {'floor':>7s} {'HF>4Hz':>8s} {'floor':>7s}")
    print("-" * 88)
    for name, factory in schools:
        for prof in PROFILES:
            rng = np.random.default_rng(1234)
            dts = dt_sequence(prof, args.seconds, rng)
            real, ctrl = judder_vs_truth(factory, dts, deadband=args.deadband,
                                         drawrot=not args.no_drawrot)
            ex = real["body_acc"] / max(ctrl["body_acc"], 1e-9)
            print(f"{name:17s} {prof:10s} {real['body_acc']:9.2f} {ctrl['body_acc']:9.2f} "
                  f"{ex:7.1f}× {real['wag_hz']:7.2f} {ctrl['wag_hz']:7.2f} "
                  f"{real['hf_frac']:8.3f} {ctrl['hf_frac']:7.3f}")
    print("\n'floor' = the SAME metric on a perfectly dt-compensated motion drawn on the")
    print("same frames (ground truth = 120 Hz run). excess >> 1 means the judder is real.")


def variants_report(args):
    """POSITIVE CONTROL.

    A rig that reports 'no jitter' proves nothing until it is shown to light up on a
    configuration the user HAS seen shaking. Build ≤344 (no deadband, no DrawRot) is
    exactly that: the user called it 'ปลาหมุน/ส่ายหัวไปมาถี่ๆ'. If the rig cannot tell
    ≤344 from 345, the rig is blind and every number above is worthless.
    """
    variants = [
        ("<=344 raw (user: shakes)", 0.0, False),
        ("+deadband only", DEADBAND, False),
        ("+DrawRot only", 0.0, True),
        ("345 (deadband+DrawRot)", DEADBAND, True),
    ]
    schools = (("barracuda(calm)", lambda **kw: CalmSchool(seed=7, mode_lock=args.mode, **kw)),
               ("trevally(pod)", lambda **kw: Pod(seed=11, **kw)))
    print(f"\n{'school':17s} {'variant':26s} {'dt':8s} {'wag/s':>7s} "
          f"{'yawShim':>8s} {'posShim':>9s} {'HF>4Hz':>7s}")
    print("-" * 88)
    for name, factory in schools:
        for label, db, dr in variants:
            for prof in ("fixed60", "device"):
                rng = np.random.default_rng(1234)
                dts = dt_sequence(prof, args.seconds, rng)
                sch = factory()
                times, yaw, bank, pos = run(sch, dts, deadband=db, drawrot=dr)
                m = metrics(times, yaw, pos)
                sh = shimmer(times, yaw, pos, sch.flen)
                print(f"{name:17s} {label:26s} {prof:8s} {m['wag_hz']:7.2f} "
                      f"{sh['yaw_shimmer_deg']:7.2f}° {sh['pos_shimmer_bl'] * 100:8.2f}% "
                      f"{m['hf_frac']:7.3f}")
        print()
    print("yawShim = RMS yaw wobble faster than 0.2 s, in DEGREES (this is what 'ส่ายหัว' is)")
    print("posShim = same for sideways position, as a % of BODY LENGTH ('กระตุก')")


def make_chart(args):
    """One picture for the verdict: the traces on top of each other, and the amplitudes
    next to something the eye is known to see."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, axes = plt.subplots(1, 3, figsize=(16.5, 5.2), facecolor="white",
                             gridspec_kw={"width_ratios": [1.1, 1.1, 1.3]})
    for ax, (title, fac) in zip(axes[:2], (("barracuda (calm formation)", lambda: CalmSchool(seed=7)),
                                           ("trevally / pla-kamong (pod)", lambda: Pod(seed=11)))):
        for prof, colour, lw in (("fixed60", "#1f77b4", 2.4), ("device", "#d62728", 1.2)):
            rng = np.random.default_rng(1234)
            dts = dt_sequence(prof, 8.0, rng)
            t, yaw, _, pos = run(fac(), dts)
            ax.plot(t, np.degrees(np.unwrap(yaw[:, 0])), color=colour, lw=lw,
                    label=f"dt = {prof}")
        ax.set_title(f"{title}\ndrawn heading of one fish", fontsize=11)
        ax.set_xlabel("seconds")
        ax.set_ylabel("heading (deg)")
        ax.legend(fontsize=9)
        ax.grid(alpha=0.25)

    ax = axes[2]
    labels, vals, colours = [], [], []
    for name, fac, lab in (("barracuda", lambda: CalmSchool(seed=7), "barra"),
                           ("trevally", lambda: Pod(seed=11), "trevally")):
        for vlabel, db, dr in (("≤344, 60fps", 0.0, False), ("≤344, device dt", 0.0, False),
                               ("345, device dt", DEADBAND, True)):
            prof = "fixed60" if "60fps" in vlabel else "device"
            rng = np.random.default_rng(1234)
            dts = dt_sequence(prof, 20.0, rng)
            sch = fac()
            t, yaw, _, pos = run(sch, dts, deadband=db, drawrot=dr)
            labels.append(f"{lab}\n{vlabel}")
            vals.append(shimmer(t, yaw, pos, sch.flen)["yaw_shimmer_deg"])
            colours.append("#7f7f7f" if "60fps" in vlabel else ("#d62728" if "≤344" in vlabel else "#2ca02c"))
    labels.append("plank-waggle\n(fallback path)")
    vals.append(math.degrees(0.18))          # WiggleAmp, the one known-visible wobble in the app
    colours.append("#ff7f0e")
    ax.bar(range(len(vals)), vals, color=colours)
    ax.axhline(2.0, ls="--", color="k", lw=1)
    ax.text(0.02, 2.25, "roughly where the eye starts to notice", fontsize=9)
    ax.set_xticks(range(len(labels)))
    ax.set_xticklabels(labels, fontsize=7.5, rotation=20, ha="right")
    ax.set_ylabel("fast yaw wobble, RMS (deg)")
    ax.set_title("amplitude of the shake\n(dt jitter vs a wobble users DID report)", fontsize=11)
    ax.grid(alpha=0.25, axis="y")

    fig.suptitle("dt-variance rig — does frame-time jitter make the schools shake?", fontsize=13)
    fig.tight_layout()
    fig.savefig(args.chart, dpi=110)
    print(f"wrote {args.chart}")


def make_gif(args):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.animation import FuncAnimation, PillowWriter

    which = args.gif_school
    runs = []
    for prof in args.gif_profiles.split(","):
        rng = np.random.default_rng(1234)
        dts = dt_sequence(prof, args.seconds, rng)
        sch = CalmSchool(seed=7, mode_lock=args.mode) if which == "calm" else Pod(seed=11)
        times, yaw, bank, pos = run(sch, dts, deadband=args.deadband)
        grid, y = resample(times, yaw, hz=60.0)
        gp, px = resample(times, pos[:, :, 0], hz=60.0)
        _, pz = resample(times, pos[:, :, 2], hz=60.0)
        runs.append((prof, grid, y, px, pz, metrics(times, yaw, pos)))

    frames = min(len(r[1]) for r in runs)
    fig, axes = plt.subplots(1, len(runs), figsize=(6 * len(runs), 6.4), facecolor="#06131b")
    if len(runs) == 1:
        axes = [axes]
    span = 220 if which == "calm" else 160
    quivs = []
    for ax, (prof, grid, y, px, pz, m) in zip(axes, runs):
        ax.set_facecolor("#06131b")
        ax.set_xlim(-span, span)
        ax.set_ylim(-span, span)
        ax.set_xticks([])
        ax.set_yticks([])
        ax.set_title(f"{prof}   wag {m['wag_hz']:.1f}/s   HF {m['hf_frac']:.2f}",
                     color="w", fontsize=13)
        q = ax.quiver(px[0], pz[0], np.sin(y[0]), np.cos(y[0]),
                      color="#7fe3ff", scale=28, width=0.005, pivot="mid")
        quivs.append(q)
    fig.suptitle(f"{'barracuda calm-formation' if which == 'calm' else 'trevally pod'} "
                 f"— top view, same wall-clock 60 Hz sampling", color="w", fontsize=14)

    def upd(i):
        for q, (prof, grid, y, px, pz, m) in zip(quivs, runs):
            q.set_offsets(np.c_[px[i], pz[i]])
            q.set_UVC(np.sin(y[i]), np.cos(y[i]))
        return quivs

    ani = FuncAnimation(fig, upd, frames=range(0, frames, args.gif_stride), blit=False)
    ani.save(args.gif, writer=PillowWriter(fps=args.gif_fps), dpi=70)
    print(f"wrote {args.gif}  ({frames // args.gif_stride} frames)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=float, default=20.0)
    ap.add_argument("--deadband", type=float, default=DEADBAND)
    ap.add_argument("--mode", default=None, help="lock the barracuda formation mode (cluster/vortex/...)")
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--truth", action="store_true", help="judder vs artefact floor")
    ap.add_argument("--variants", action="store_true", help="positive control: <=344 vs 345")
    ap.add_argument("--no-drawrot", action="store_true", help="draw straight from the sim (pre-e232745)")
    ap.add_argument("--gif", default=None)
    ap.add_argument("--chart", default=None, help="verdict PNG")
    ap.add_argument("--gif-school", default="calm", choices=["calm", "pod"])
    ap.add_argument("--gif-profiles", default="fixed60,device")
    ap.add_argument("--gif-fps", type=int, default=30)
    ap.add_argument("--gif-stride", type=int, default=2)
    args = ap.parse_args()
    if args.variants:
        variants_report(args)
        return
    if args.truth:
        truth_report(args)
        return
    if args.chart:
        make_chart(args)
        return
    if args.gif:
        make_gif(args)
    if args.report or not args.gif:
        report(args)


if __name__ == "__main__":
    main()


# ── map-driven geometry (so the rig can be pointed at a REAL dive site) ──────
def school_params_from_map(site, asset="school:barracuda"):
    """MarineMath.SchoolGeometryFor + FishSchoolSystem.Configure, for one placed item.

    🔴 The rig ran on Chang's numbers (item scale 9.2) for the whole first pass. The user
    reports the shake on Harddeep, where the SAME asset is placed at 3.87 — a different
    fish length, radius and cruise speed. Never assume one map's geometry stands for
    another's; read the item.
    """
    env = site["env"]
    it = next(i for i in site["items"] if i["assetId"] == asset)
    s = float(it["s"][0])
    fish_local, web_count, form_r, swim_mul = 1.862, 200, 0.6, 0.06   # MarineMath.SpeciesFor
    flen = fish_local * s
    R = max(8.0 * flen, fish_local * max(2.8, web_count * 0.07) * form_r * s)
    speed = fish_local * 0.065 * swim_mul * 60.0 * s
    return dict(
        scale=s, flen=flen, R=R, max_speed=speed, cap=speed / 60.0,
        vert_half=R * 0.275, safe_r=R * 3.2,
        anchor=[float(it["p"][0]), float(it["p"][1]), float(it["p"][2])],
        cap_y=float(env["waterLevel"]) - flen, floor_y=flen,
        settle_d=3.0 * flen, water=float(env["waterLevel"]),
    )


def calm_from_map(site, seed=7, diver_speed=30.0, diver=False, **kw):
    """A CalmSchool wearing a real map's numbers."""
    p = school_params_from_map(site)
    s = CalmSchool(seed=seed, **kw)
    s.flen, s.R, s.cap = p["flen"], p["R"], p["cap"]
    s.safe_r, s.cap_y, s.floor_y = p["safe_r"], p["cap_y"], p["floor_y"]
    s.anchor = np.array(p["anchor"], dtype=float)
    s.drift_speed = p["max_speed"] * 0.5
    s.diver_speed = diver_speed
    # re-seed the slots at the new radius (SlotFor uses R and flen)
    rng = np.random.default_rng(seed)
    r0, r1, r2, r6 = (rng.random(s.n) for _ in range(4))
    s.cx = (r0 - 0.5) * s.R * 2.0
    s.cy = (r1 - 0.5) * s.R * 0.55
    s.cz = (r2 - 0.5) * s.R * 2.0
    s.yspread = (r0 - 0.5) * s.flen * 1.4
    s.tr_jit = r6 * 0.35
    gx, gy, gz, vx, vz = s.form_target(s.mode, 0.0)
    s.pos = np.stack([s.anchor[0] + gx, s.anchor[1] + gy, s.anchor[2] + gz], -1)
    s.head = np.arctan2(vx, vz)
    s.yaw_prev = s.head.copy()
    s.draw_q = None
    if diver:
        s.diver = s.anchor + np.array([-s.R * 1.6, 0.0, 0.0])
        s.diver_dir = np.array([1.0, 0.0, 0.0])
    return s
