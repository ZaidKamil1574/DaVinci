// Unlit emissive LED for server racks and port status lights.
//
// Blinking is computed on the GPU from _Time, so hundreds of server LEDs cost no CPU and need no
// real lights. Each LED's phase and rate come from its vertex colour alpha, baked into the rack's
// LED strip mesh, so instanced or batched racks never blink in lockstep. Port LEDs set _BaseColor
// and _BlinkRate through a MaterialPropertyBlock instead.
//
// Written to work with SRP batching, GPU instancing and single-pass instanced stereo (Quest).
Shader "DataCenter/Status LED"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.2, 1, 0.4, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.6
        _BlinkRate ("Blink Rate (Hz, 0 = steady)", Float) = 0
        _Duty ("Duty Cycle", Range(0.05, 1)) = 0.55
        _OffLevel ("Off Brightness", Range(0, 1)) = 0.12
        [Toggle(_VERTEX_TINT)] _VertexTint ("Use Vertex Colour Tint and Phase", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VERTEX_TINT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Intensity;
                half _BlinkRate;
                half _Duty;
                half _OffLevel;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                half3 tint = _BaseColor.rgb;
                half phase = 0;
                half rate = _BlinkRate;

                #if defined(_VERTEX_TINT)
                    tint = input.color.rgb;
                    phase = input.color.a;
                    // Vertex alpha also varies the rate, from steady-ish to busy activity lights.
                    rate = lerp(0.4, 4.0, frac(input.color.a * 7.31));
                #endif

                half on = 1;
                if (rate > 0.001)
                    on = step(frac(_Time.y * rate + phase), _Duty);

                output.color = half4(tint * lerp(_OffLevel, 1.0, on) * _Intensity, 1);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
