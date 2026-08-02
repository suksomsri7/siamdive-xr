// A fish that swims by bending, instead of one that is rotated from side to side.
//
// 🔴 What this replaces. FishSchoolSystem applied the whole "swimming" motion as a rigid yaw on
// the transform — `rot *= Quaternion.Euler(0, sin(t)*10°, 0)` — so every fish waggled as one solid
// plank, nose and tail swinging together. Reported as "ปลาไม่ว่าย": the fish were moving, and it
// still did not read as swimming, because that is not what swimming looks like. A fish sends a
// travelling wave down its body; the head barely deviates and the tail does almost all of it.
//
// Why a vertex shader and not a skeleton. None of the marine GLBs has a rig — every source file
// in the library reports `skins 0, anims 0` — so there is nothing to animate. Auto-rigging is what
// produced the swim cycle the user already rejected on the web. Bending the mesh on the GPU needs
// no rig, no readable mesh (Draco meshes are decoded straight to VRAM), costs nothing per fish,
// and survives the GPU instancing the schools depend on.
//
// The phase comes from the INSTANCE POSITION, not from a per-instance buffer. Without it a school
// beats in perfect unison, which looks stranger than not moving at all; with it, no two fish in a
// hundred share a stroke and no extra data has to be uploaded per frame.
Shader "DiveMap/FishWave"
{
    Properties
    {
        _Color      ("Color", Color) = (1,1,1,1)
        _MainTex    ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic   ("Metallic", Range(0,1)) = 0.0

        // Body length in MODEL units. The library authors every fish nose-to-tail along +Z at
        // about 1.9 units; the wave is expressed as a fraction of this so one set of numbers fits
        // a sardine and a whale shark.
        _WaveLen    ("Body length (model units)", Float) = 1.9
        // Sideways travel of the tail tip, as a fraction of body length. 0.10 is a lazy cruise;
        // much past 0.2 and it reads as panic.
        _WaveAmp    ("Tail amplitude (× length)", Range(0,0.4)) = 0.10
        // Tail-beats per second × 2π. Small fish beat faster than large ones.
        _WaveSpeed  ("Beat speed", Range(0,30)) = 7.0
        // How many waves fit along the body. Under 1 the whole fish leans; over 2 it looks like
        // an eel. Most fish are near 1.
        _WaveCycles ("Waves along body", Range(0.3,3)) = 1.0
        // 0 = the nose moves as much as the tail (wrong). 1 = the nose is pinned (right).
        _WaveAnchor ("Head anchored", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        // Standard lighting so these fish sit in the same light, fog and reflection as every other
        // object — a custom lighting model here would make the schools the one thing that does not
        // react when the headlamp goes on.
        #pragma surface surf Standard vertex:vert addshadow fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.0

        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; };

        half  _Glossiness, _Metallic;
        fixed4 _Color;
        float _WaveLen, _WaveAmp, _WaveSpeed, _WaveCycles, _WaveAnchor;

        void vert(inout appdata_full v)
        {
            float len = max(_WaveLen, 0.0001);

            // 0 at the nose (+Z), 1 at the tail (−Z). The mesh is centred, so z spans ±len/2.
            float u = saturate(0.5 - v.vertex.z / len);

            // Squared, so the deflection is concentrated in the back third the way a real body
            // wave is. Linear makes the fish look hinged in the middle.
            float envelope = lerp(1.0, u * u, saturate(_WaveAnchor));

            // Per-instance phase from the instance's world position. unity_ObjectToWorld's last
            // column is the translation, and it differs for every fish in the school.
            float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
            float phase = frac(sin(dot(origin, float3(12.9898, 78.233, 37.719))) * 43758.5453) * 6.2831853;

            float wave = sin(_Time.y * _WaveSpeed
                             + (v.vertex.z / len) * 6.2831853 * _WaveCycles
                             + phase);

            v.vertex.x += wave * envelope * _WaveAmp * len;

            // Bend the normal with the surface, or the lighting stays flat while the shape moves
            // and the fish reads as a printed decal sliding about.
            float slope = _WaveAmp * len * envelope * 6.2831853 * _WaveCycles / len;
            v.normal = normalize(float3(v.normal.x - wave * slope * 0.5, v.normal.y, v.normal.z));
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
