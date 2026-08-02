// A fish that swims by bending, instead of one that is rotated from side to side.
//
// 🔴 What this replaces. FishSchoolSystem applied the whole "swimming" motion as a rigid yaw on
// the transform — `rot *= Quaternion.Euler(0, sin(t)*10°, 0)` — so every fish waggled as one solid
// plank, nose and tail swinging together. Reported as "ปลาไม่ว่าย": the fish were moving, and it
// still did not read as swimming, because that is not what swimming looks like. A fish sends a
// travelling wave down its body; the head barely deviates and the tail does almost all of it.
//
// Why a vertex shader and not a skeleton. The single-animal marine GLBs carry no rig at all
// (`skins 0, anims 0`), and the ones that DO ship a rig are drawn as hundreds of static
// instances, where skinning is not on the table. Auto-rigging is what produced the swim cycle
// the user already rejected on the web. Bending the mesh on the GPU needs no rig, no readable
// mesh (Draco meshes are decoded straight to VRAM), costs a handful of ALU per vertex, and
// survives the GPU instancing the schools depend on.
//
// This is the SCHOOL variant: one texture, because it is drawn 1,100 times a frame. The hero
// animals use DM_FishWaveDetail, which keeps their normal/emissive/roughness maps. Both share
// DM_FishWave.cginc so the motion can never differ between them.
Shader "DiveMap/FishWave"
{
    Properties
    {
        _Color      ("Color", Color) = (1,1,1,1)
        _MainTex    ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic   ("Metallic", Range(0,1)) = 0.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        // Body length in MODEL units. The library authors every fish nose-to-tail along +Z at
        // about 1.9 units; the wave is expressed as a fraction of this so one set of numbers fits
        // a sardine and a whale shark.
        _WaveLen    ("Body length (model units)", Float) = 1.9
        // Half-wingspan, for the pectoral-flap mode only.
        _WaveSpan   ("Half wingspan (model units)", Float) = 0.95
        // Sideways travel of the tail tip, as a fraction of body length. 0.10 is a lazy cruise;
        // much past 0.2 and it reads as panic. SwimStyle picks this per species.
        _WaveAmp    ("Tip amplitude (× length)", Range(0,0.5)) = 0.10
        // How many waves fit along the body. Under 1 the whole fish leans; over 2 it looks like
        // an eel. Most fish are near 1.
        _WaveCycles ("Waves along body", Range(0.2,3)) = 0.95
        // 0 = the nose moves as much as the tail (wrong). 1 = the envelope is applied (right).
        _WaveAnchor ("Head anchored", Range(0,1)) = 1.0
        // How far the nose swings the OTHER way — the recoil that puts the pivot at the centre
        // of mass instead of welding the head in place.
        _WaveRecoil ("Head recoil", Range(0,0.4)) = 0.10
        // Slow amplitude drift, so a hundred fish are not a hundred metronomes.
        _WaveGust   ("Amplitude drift", Range(0,0.6)) = 0.28
        // Cruise = 1. Below 1 the animal is gliding, above 1 it is sprinting.
        _WaveEffort ("Effort", Range(0,3)) = 1.0
        // Beat phase in radians — INTEGRATED ON THE CPU (SwimStyle.BeatPhaseStep), never
        // _Time.y × speed, because that form cannot have its speed changed without a jump.
        _WavePhase  ("Beat phase (rad)", Float) = 0.0
        // 0 = axial body wave · 1 = pectoral flap.
        _WaveMode   ("Gait (0 body, 1 wing)", Float) = 0
        _WaveFwd    ("Nose axis (object space)", Vector) = (0,0,1,0)
        _WaveSide   ("Lateral axis (object space)", Vector) = (1,0,0,0)
        _WaveDir    ("Thrust axis (object space)", Vector) = (1,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200
        Cull [_Cull]

        CGPROGRAM
        // Standard lighting so these fish sit in the same light, fog and reflection as every other
        // object — a custom lighting model here would make the schools the one thing that does not
        // react when the headlamp goes on.
        #pragma surface surf Standard vertex:vert addshadow fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.0

        #include "DM_FishWave.cginc"

        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; };

        half  _Glossiness, _Metallic;
        fixed4 _Color;

        // 🔴 Nothing here writes o.Normal, and that is deliberate rather than an omission. The
        // moment a surface shader assigns o.Normal, Unity regenerates the pass with a full
        // tangent-space basis — three float4 interpolators instead of one float3 world normal —
        // for EVERY vertex. This pass runs 1,100 instances a frame. The hero animals, which are
        // one object each and can afford it, get their normal map from DM_FishWaveDetail; both
        // shaders share DM_FishWave.cginc so the motion is identical either way.
        void vert(inout appdata_full v)
        {
            DM_FishBend(v.vertex, v.normal);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }

    // If this shader is ever stripped from a build, Standard draws the fish unbent rather than
    // magenta — the failure this project has already paid for once.
    FallBack "Standard"
}
