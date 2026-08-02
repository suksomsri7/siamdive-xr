// The body wave itself. Shared by DM_FishWave (schools — cheap, one texture) and
// DM_FishWaveDetail (the hero animals — keeps the model's normal/emissive/roughness maps),
// so the two can never drift apart.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// 🔴 What was wrong with the first version of this, and why the fish still read as stiff.
//
// The per-instance phase was hashed from the instance's WORLD POSITION:
//
//     phase = frac(sin(dot(origin, float3(12.9898, 78.233, 37.719))) * 43758.5453) * 2π
//
// That hash is designed to be chaotic — that is the whole point of a hash — and the origin
// of a swimming fish MOVES. A scad cruises 10 u/s, so its origin advances ~0.17 u per frame,
// which changes the argument of that sin by up to 6.8 radians and re-rolls the phase from
// scratch. Sixty times a second. Measured on the real numbers, eight consecutive frames of
// one scad gave 0.44, 0.58, 3.14, 2.59, 1.84, 5.32, 2.84, 6.12 rad — white noise, not a
// travelling wave. Even a fish creeping at 0.001 u/frame re-rolls it completely.
//
// So the body never waved. It snapped to a random bend every frame, which at 5-11 % of body
// length averages out to a blur with no direction in it — visually indistinguishable from a
// rigid fish, and exactly the report: "ไม่สมูท ตัวแข็งมาก".
//
// The phase now comes from the instance's SCALE, which the school renderer already jitters
// per fish (0.85-1.15 × for a school, 0.55-1.35 × for a pod) and never changes afterwards.
// Same free per-instance randomness, zero upload, and it is CONSTANT for the life of the fish.
//
// The beat itself is no longer sin(_Time.y · speed) either: _WavePhase is integrated on the
// CPU. That is what makes it legal to change the beat rate at runtime — with _Time.y the tail
// would teleport the moment the rate moved (see SwimStyle.BeatPhaseStep).
// ─────────────────────────────────────────────────────────────────────────────────────────

#ifndef DM_FISHWAVE_INCLUDED
#define DM_FISHWAVE_INCLUDED

float  _WaveLen;      // body length (Body/Fluke) in MODEL units
float  _WaveSpan;     // half-wingspan (Wing) in MODEL units
float  _WaveAmp;      // tip travel to one side, as a fraction of _WaveLen / _WaveSpan
float  _WaveCycles;   // wavelengths along the body / half-wing
float  _WaveAnchor;   // 0 = the nose moves as much as the tail (wrong), 1 = pinned (right)
float  _WaveRecoil;   // how far the nose swings the OTHER way
float  _WaveGust;     // slow amplitude drift, so the beat is not a metronome
float  _WaveEffort;   // 1 = cruise · <1 gliding · >1 sprinting (drives amplitude)
float  _WavePhase;    // beat phase in radians, integrated on the CPU
float  _WaveMode;     // 0 = axial body wave (fish, sharks, whales) · 1 = pectoral flap (rays)
float4 _WaveFwd;      // object-space nose direction  (glTF convention: +Z)
float4 _WaveSide;     // object-space lateral axis     (glTF convention: +X)
float4 _WaveDir;      // object-space thrust direction: sideways for a fish, up for a fluke/wing

// A per-instance constant. See the header: it must NOT be derived from anything that moves.
float DM_InstancePhase()
{
    // Column 0 of the object→world matrix is scale.x × right, so its length is the instance's
    // own uniform scale — jittered per fish at Configure and fixed from then on.
    float3 c0 = float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20);
    float  s  = length(c0) + 1e-4;
    return frac(sin(s * 91.7213) * 43758.5453) * 6.2831853;
}

// Rotate a normal by a small angle in the plane spanned by (a → b). Linearised: the surface
// tangent along `a` tips toward `b` by `theta`, and the normal turns with it. Without this the
// shape moves while the shading stays put and the fish reads as a decal sliding around.
float3 DM_TipNormal(float3 n, float3 a, float3 b, float theta)
{
    return normalize(n + theta * (b * dot(n, a) - a * dot(n, b)));
}

void DM_FishBend(inout float4 vertex, inout float3 normal)
{
    float amp0 = _WaveAmp * max(_WaveEffort, 0.0);
    if (amp0 < 1e-5) return;   // a crab does not swim

    float3 fwd  = normalize(_WaveFwd.xyz  + float3(0, 0, 1e-5));
    float3 side = normalize(_WaveSide.xyz + float3(1e-5, 0, 0));
    float3 dir  = normalize(_WaveDir.xyz  + float3(1e-5, 0, 0));

    float ph = DM_InstancePhase() + _WavePhase;

    // Not a metronome: a slow, fixed-rate swell over the amplitude. Fixed rate, so reading it
    // straight off _Time is safe here in a way the beat itself is not.
    float amp = amp0 * (1.0 + _WaveGust * sin(_Time.y * 0.71 + ph * 1.7));

    float tau = 6.2831853 * _WaveCycles;

    if (_WaveMode > 0.5)
    {
        // ── Pectoral flap (manta, eagle ray, turtle) ──────────────────────────────
        // The wave runs ACROSS the animal, from the shoulder out to the wingtip, and the
        // displacement is vertical. Nothing about the tail is involved. Getting this wrong is
        // what makes a manta look like a floating carpet.
        float span = max(_WaveSpan, 1e-4);
        float sp   = dot(vertex.xyz, side);
        float w    = saturate(abs(sp) / span);

        float env  = pow(w, 1.7);           // the shoulder barely moves, the tip does it all
        float wave = sin(ph - w * tau);     // the tip LAGS the shoulder — that is the flap
        float A    = amp * span;

        vertex.xyz += dir * (A * env * wave);

        float dEnv  = 1.7 * pow(w, 0.7);
        float dWave = -tau * cos(ph - w * tau);
        float dD    = A * (dEnv * wave + env * dWave);
        float theta = dD * sign(sp) / span;
        normal = DM_TipNormal(normal, side, dir, theta);
        return;
    }

    // ── Axial body wave (fish, sharks — sideways; whales — up and down) ───────────
    float len = max(_WaveLen, 1e-4);
    float s   = dot(vertex.xyz, fwd);
    float u   = saturate(0.5 - s / len);    // 0 at the nose, 1 at the tail

    // Squared, so the deflection piles up in the back third the way a real body wave does —
    // linear makes the fish look hinged in the middle. The recoil term lets the NOSE swing
    // slightly the other way, which puts the pivot near the centre of mass instead of welding
    // the head to the water. Real fish do this and the eye notices when they don't.
    float env = lerp(1.0, u * u - _WaveRecoil * (1.0 - u) * (1.0 - u), saturate(_WaveAnchor));

    float wave = sin(ph - u * tau);         // crest starts at the head and runs aft
    float A    = amp * len;

    vertex.xyz += dir * (A * env * wave);

    float dEnv  = lerp(0.0, 2.0 * u + 2.0 * _WaveRecoil * (1.0 - u), saturate(_WaveAnchor));
    float dWave = -tau * cos(ph - u * tau);
    float dD    = A * (dEnv * wave + env * dWave);
    float theta = -dD / len;                // du/ds = −1/len
    normal = DM_TipNormal(normal, fwd, dir, theta);
}

#endif // DM_FISHWAVE_INCLUDED
