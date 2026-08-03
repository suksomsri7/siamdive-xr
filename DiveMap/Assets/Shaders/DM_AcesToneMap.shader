// The last thing that happens to a frame: the film curve the web has always rendered through.
//
// 🔴 WHY. builder.html:485 sets `renderer.toneMapping = THREE.ACESFilmicToneMapping` with
// `toneMappingExposure = 1.05`; Unity had nothing at all. Without it, light adds up in a straight
// line and then gets written to the screen: highlights clip flat, midtones go chalky, and the
// shading gradient that carries SURFACE DETAIL is the first thing to be crushed — which is what
// "texture พื้นผิวแย่มาก … บนเว็บแสดงผลดีกว่ามาก" means on a build whose textures are four times
// the web's resolution. ACES is an S-curve: a toe that keeps the deep water off the floor and a
// shoulder that gives a lit flank somewhere to go instead of white.
//
// 🔎 The arithmetic is a straight port of three.js r160's ACESFilmicToneMapping (Stephen Hill's
// RRT+ODT fit), including its `exposure / 0.6`. DiveMap.Core.ToneMap is the same curve in C# and
// ToneMapTests pins the two together — a formula that lives in two languages drifts in one of them.
//
// 🔎 WHERE IT RUNS. As an OnRenderImage blit on the scene camera only. The uGUI canvas is
// ScreenSpaceOverlay (UiShell:149), which Unity composites AFTER every camera and after image
// effects, so the UI is not tone mapped and its colours stay exactly as authored. That is not a
// happy accident it is the reason the effect is a camera image effect rather than a global one.
//
// 🔎 COLOUR SPACE. In a linear project the source RenderTexture is sRGB, so tex2D hands this
// scene-linear light and the write to the destination re-encodes. That is the same order three.js
// uses (tone map in linear, then outputColorSpace = SRGB) and it is the reason this shader must
// not do any gamma maths of its own.
Shader "DiveMap/AcesToneMap"
{
    Properties
    {
        _MainTex  ("Source", 2D) = "white" {}
        // builder.html:485. Pushed from C# (AcesToneMapping) so the number lives in one place.
        _Exposure ("Exposure", Float) = 1.05
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Exposure;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // three.js tonemapping_pars_fragment.glsl.js — GLSL mat3 takes COLUMNS, so these are
            // written as float3x3 rows of the same matrices.
            static const float3x3 ACESInputMat = float3x3(
                0.59719, 0.35458, 0.04823,
                0.07600, 0.90834, 0.13383,
                0.02840, 0.13383, 0.83777);

            static const float3x3 ACESOutputMat = float3x3(
                 1.60475, -0.53108, -0.07367,
                -0.10208,  1.10813, -0.00605,
                -0.00327, -0.07276,  1.07602);

            float3 RRTAndODTFit(float3 v)
            {
                float3 a = v * (v + 0.0245786) - 0.000090537;
                float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
                return a / b;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                // ...including three.js's / 0.6. Without it the app is ~1.5 stops darker than the
                // web at the same authored exposure, and the number in the file still says 1.05.
                float3 c = src.rgb * (_Exposure / 0.6);
                c = mul(ACESInputMat, c);
                c = RRTAndODTFit(c);
                c = mul(ACESOutputMat, c);
                return fixed4(saturate(c), src.a);
            }
            ENDCG
        }
    }

    // No custom shader in this project is allowed to be the only thing standing between the build
    // and a magenta screen (it has happened twice on iOS). AcesToneMapping checks shader.isSupported
    // and skips the whole effect if this fails, and the material lives in Resources + Always
    // Included Shaders so it cannot be stripped in the first place.
    FallBack Off
}
